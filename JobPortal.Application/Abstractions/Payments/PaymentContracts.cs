using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Payments;

public interface IPaymentService
{
    Task<PaymentOrderResponse> CreateOrderAsync(Guid userId, CreatePaymentOrderRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResponse> ConfirmAsync(Guid userId, Guid paymentId, ConfirmRazorpayPaymentRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<PaymentResponse>> GetPaymentsAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
    Task<PagedResponse<PaymentHistoryResponse>> GetHistoryAsync(Guid userId, HistoryQuery query, CancellationToken cancellationToken = default);
}

public interface IRazorpayGateway
{
    string KeyId { get; }
    Task<RazorpayOrder> CreateOrderAsync(long amountInMinorUnits, string currencyCode, string receipt, CancellationToken cancellationToken = default);
    bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
}

public sealed record RazorpayOrder(string Id, long Amount, string Currency, string Receipt);

public interface IMembershipPlanProvider
{
    MembershipPlan GetDefaultPlan();
}

public sealed record MembershipPlan(string Name, decimal Amount, string CurrencyCode, int DurationDays);
