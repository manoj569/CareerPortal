using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Features.Memberships;

public enum ApplicationAccessStatus { Granted = 1, LoginRequired, PaymentRequired }

public sealed record ApplicationAccessResponse(
    ApplicationAccessStatus Status, string Message, string? ApplicationUrl = null,
    Guid? CompanyId = null);
public sealed record MembershipResponse(
    Guid Id, string PlanName, MembershipStatus Status, DateTime StartsAtUtc,
    DateTime? EndsAtUtc, bool AutoRenew, Guid CompanyId, string CompanyName);
public sealed record MembershipHistoryResponse(
    Guid Id, Guid MembershipId, MembershipStatus? PreviousStatus,
    MembershipStatus CurrentStatus, DateTime OccurredAtUtc, string? Reason);
public sealed record HistoryQuery(int PageNumber = 1, int PageSize = 20);
public sealed record MembershipHistoryPage(PagedResponse<MembershipHistoryResponse> Page);
