using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Payments;

public sealed record CreatePaymentOrderRequest;
public sealed record PaymentOrderResponse(
    Guid PaymentId, Guid MembershipId, string ProviderOrderId, string KeyId,
    long AmountInMinorUnits, string CurrencyCode, string Receipt);
public sealed record ConfirmRazorpayPaymentRequest(
    string RazorpayOrderId, string RazorpayPaymentId, string RazorpaySignature);
public sealed record PaymentResponse(
    Guid Id, decimal Amount, string CurrencyCode, PaymentStatus Status,
    PaymentProvider Provider, string? ProviderOrderId, string? ProviderPaymentId,
    DateTime? PaidAtUtc, Guid? MembershipId, DateTime CreatedAtUtc);
public sealed record PaymentHistoryResponse(
    Guid Id, Guid PaymentId, PaymentStatus? PreviousStatus, PaymentStatus CurrentStatus,
    DateTime OccurredAtUtc, string? ProviderEventId, string? Reason);
public sealed record PaymentHistoryPage(PagedResponse<PaymentHistoryResponse> Page);
