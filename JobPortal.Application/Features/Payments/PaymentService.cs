using FluentValidation;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Common.Validation;
using JobPortal.Application.Features.Memberships;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Payments;

public sealed class PaymentService(
    IPaymentRepository payments,
    IMembershipRepository memberships,
    IRazorpayGateway razorpay,
    IMembershipPlanProvider plans,
    IUnitOfWork unitOfWork,
    IValidator<CreatePaymentOrderRequest> createOrderValidator,
    IValidator<ConfirmRazorpayPaymentRequest> confirmValidator,
    TimeProvider timeProvider) : IPaymentService
{
    public async Task<PaymentOrderResponse> CreateOrderAsync(
        Guid userId, CreatePaymentOrderRequest request, CancellationToken cancellationToken = default)
    {
        await createOrderValidator.ValidateAndThrowAsync(request, cancellationToken);
        if (await memberships.GetActiveForUserAsync(userId, cancellationToken) is not null)
            throw new ConflictException("An active portal membership already exists.");

        var utcNow = UtcNow;
        var plan = plans.GetDefaultPlan();
        var membership = await memberships.GetPortalMembershipForUserAsync(userId, cancellationToken);
        if (membership?.Status == MembershipStatus.Pending)
            throw new ConflictException("A portal membership payment order is already pending.");
        if (membership is null)
        {
            membership = new Membership
            {
                UserId = userId,
                PlanName = plan.Name,
                Status = MembershipStatus.Pending,
                StartsAtUtc = utcNow
            };
            membership.History.Add(NewMembershipHistory(membership, null, MembershipStatus.Pending, userId, "Payment initiated."));
            await memberships.AddAsync(membership, cancellationToken);
        }
        else
        {
            var previous = membership.Status;
            membership.Status = MembershipStatus.Pending;
            membership.PlanName = plan.Name;
            membership.History.Add(NewMembershipHistory(membership, previous, MembershipStatus.Pending, userId, "Payment re-initiated."));
        }

        var payment = new Payment
        {
            UserId = userId,
            Membership = membership,
            Amount = plan.Amount,
            CurrencyCode = plan.CurrencyCode.ToUpperInvariant(),
            Provider = PaymentProvider.Razorpay,
            Status = PaymentStatus.Pending,
            ProviderReceipt = $"membership_{Guid.NewGuid():N}"[..31]
        };
        payment.History.Add(NewPaymentHistory(payment, null, PaymentStatus.Pending, userId, "Order initiated."));
        await payments.AddAsync(payment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var amount = checked((long)decimal.Round(payment.Amount * 100m, 0, MidpointRounding.AwayFromZero));
            var order = await razorpay.CreateOrderAsync(amount, payment.CurrencyCode, payment.ProviderReceipt, cancellationToken);
            payment.ProviderOrderId = order.Id;
            payment.TransactionReference = order.Id;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return new PaymentOrderResponse(payment.Id, membership.Id, order.Id, razorpay.KeyId,
                order.Amount, order.Currency, order.Receipt);
        }
        catch
        {
            payment.Status = PaymentStatus.Failed;
            payment.History.Add(NewPaymentHistory(payment, PaymentStatus.Pending, PaymentStatus.Failed, userId, "Provider order creation failed."));
            var previous = membership.Status;
            membership.Status = MembershipStatus.Suspended;
            membership.History.Add(NewMembershipHistory(membership, previous, MembershipStatus.Suspended, userId, "Provider order creation failed."));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<PaymentResponse> ConfirmAsync(
        Guid userId, Guid paymentId, ConfirmRazorpayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        await confirmValidator.ValidateAndThrowAsync(request, cancellationToken);
        var payment = await payments.GetOwnedAsync(paymentId, userId, cancellationToken)
            ?? throw new NotFoundException("Payment was not found.");
        if (payment.Status == PaymentStatus.Paid) return ToResponse(payment);
        if (payment.Status != PaymentStatus.Pending || payment.ProviderOrderId != request.RazorpayOrderId)
            throw new ConflictException("Payment is not in a confirmable state.");
        if (!razorpay.VerifyPaymentSignature(request.RazorpayOrderId, request.RazorpayPaymentId, request.RazorpaySignature))
            throw new BadRequestException("Payment signature verification failed.", "invalid_payment_signature");

        var utcNow = UtcNow;
        payment.Status = PaymentStatus.Paid;
        payment.ProviderPaymentId = request.RazorpayPaymentId;
        payment.PaidAtUtc = utcNow;
        payment.History.Add(NewPaymentHistory(payment, PaymentStatus.Pending, PaymentStatus.Paid, userId, "Signature verified."));

        var membership = payment.Membership ?? throw new ConflictException("Payment has no membership.");
        var previous = membership.Status;
        var plan = plans.GetDefaultPlan();
        membership.Status = MembershipStatus.Active;
        membership.StartsAtUtc = utcNow;
        membership.EndsAtUtc = utcNow.AddDays(plan.DurationDays);
        membership.History.Add(NewMembershipHistory(membership, previous, MembershipStatus.Active, userId, "Payment completed."));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToResponse(payment);
    }

    public async Task<PagedResponse<PaymentResponse>> GetPaymentsAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await payments.GetForUserAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    public async Task<PagedResponse<PaymentHistoryResponse>> GetHistoryAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default)
    {
        RequestGuards.ValidatePagination(query.PageNumber, query.PageSize);
        var result = await payments.GetHistoryAsync(userId, query, cancellationToken);
        return new(result.Items, query.PageNumber, query.PageSize, result.TotalCount);
    }

    private static PaymentResponse ToResponse(Payment x) => new(
        x.Id, x.Amount, x.CurrencyCode, x.Status, x.Provider, x.ProviderOrderId,
        x.ProviderPaymentId, x.PaidAtUtc, x.MembershipId, x.CreatedAtUtc);
    private PaymentHistory NewPaymentHistory(Payment payment, PaymentStatus? previous, PaymentStatus current, Guid userId, string reason) =>
        new() { Payment = payment, UserId = userId, PreviousStatus = previous, CurrentStatus = current, OccurredAtUtc = UtcNow, Reason = reason };
    private MembershipHistory NewMembershipHistory(Membership membership, MembershipStatus? previous, MembershipStatus current, Guid userId, string reason) =>
        new() { Membership = membership, UserId = userId, PreviousStatus = previous, CurrentStatus = current, OccurredAtUtc = UtcNow, Reason = reason };
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
