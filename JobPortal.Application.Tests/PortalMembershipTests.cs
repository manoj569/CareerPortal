using FluentValidation;
using JobPortal.Application.Abstractions.Payments;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Application.Features.Memberships;
using JobPortal.Application.Features.Payments;
using JobPortal.Domain.Entities;
using JobPortal.Domain.Enums;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class PortalMembershipTests
{
    private static readonly Guid UserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task ActiveMembershipGrantsJobsFromDifferentCompanies()
    {
        var repository = new FakeMembershipRepository
        {
            Membership = new Membership { UserId = UserId, Status = MembershipStatus.Active },
            Jobs =
            {
                ["first"] = new(Guid.NewGuid(), "https://example.test/first"),
                ["second"] = new(Guid.NewGuid(), "https://example.test/second")
            }
        };
        var service = new MembershipService(repository, new FakeUnitOfWork());

        var first = await service.GetApplicationAccessAsync(UserId, "first");
        var second = await service.GetApplicationAccessAsync(UserId, "second");

        Assert.Equal(ApplicationAccessStatus.Granted, first.Status);
        Assert.Equal(ApplicationAccessStatus.Granted, second.Status);
        Assert.Equal(2, repository.RecordedApplications.Count);
    }

    [Fact]
    public async Task MissingMembershipRequiresPaymentAndAnonymousUserRequiresLogin()
    {
        var repository = AvailableRepository();
        var service = new MembershipService(repository, new FakeUnitOfWork());

        Assert.Equal(ApplicationAccessStatus.LoginRequired,
            (await service.GetApplicationAccessAsync(null, "job")).Status);
        Assert.Equal(ApplicationAccessStatus.PaymentRequired,
            (await service.GetApplicationAccessAsync(UserId, "job")).Status);
    }

    [Fact]
    public async Task UnavailableJobNeverExposesApplicationUrl()
    {
        var service = new MembershipService(
            new FakeMembershipRepository
            {
                Membership = new Membership { UserId = UserId, Status = MembershipStatus.Active }
            },
            new FakeUnitOfWork());

        await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetApplicationAccessAsync(UserId, "hidden-archived-or-expired"));
    }

    [Fact]
    public async Task ExpiredMembershipDoesNotGrantAccess()
    {
        var repository = AvailableRepository();
        repository.Membership = new Membership
        {
            UserId = UserId,
            Status = MembershipStatus.Active,
            EndsAtUtc = DateTime.UtcNow.AddMinutes(-1)
        };
        var service = new MembershipService(repository, new FakeUnitOfWork());

        var result = await service.GetApplicationAccessAsync(UserId, "job");

        Assert.Equal(ApplicationAccessStatus.PaymentRequired, result.Status);
    }

    [Fact]
    public async Task ExistingActiveMembershipRejectsDuplicateOrder()
    {
        var memberships = AvailableRepository();
        memberships.Membership = new Membership { UserId = UserId, Status = MembershipStatus.Active };
        var service = CreatePaymentService(memberships, new FakePaymentRepository(), new FakeRazorpayGateway());

        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateOrderAsync(UserId, new CreatePaymentOrderRequest()));
    }

    [Fact]
    public async Task PendingMembershipRejectsDuplicateOrder()
    {
        var memberships = AvailableRepository();
        memberships.Membership = new Membership { UserId = UserId, Status = MembershipStatus.Pending };
        var service = CreatePaymentService(memberships, new FakePaymentRepository(), new FakeRazorpayGateway());

        await Assert.ThrowsAsync<ConflictException>(
            () => service.CreateOrderAsync(UserId, new CreatePaymentOrderRequest()));
    }

    [Fact]
    public async Task OrderUsesServerPlanAndConfirmationActivatesPortalMembership()
    {
        var memberships = AvailableRepository();
        var payments = new FakePaymentRepository();
        var gateway = new FakeRazorpayGateway();
        var service = CreatePaymentService(memberships, payments, gateway);

        var order = await service.CreateOrderAsync(UserId, new CreatePaymentOrderRequest());
        var response = await service.ConfirmAsync(UserId, order.PaymentId,
            new ConfirmRazorpayPaymentRequest(order.ProviderOrderId, "pay_1", new('a', 64)));

        Assert.Equal(12345, gateway.RequestedAmount);
        Assert.Equal(PaymentStatus.Paid, response.Status);
        Assert.Equal(MembershipStatus.Active, memberships.Membership!.Status);
        Assert.NotNull(memberships.Membership.EndsAtUtc);
    }

    [Fact]
    public async Task InvalidSignatureDoesNotActivateMembership()
    {
        var memberships = AvailableRepository();
        var payments = new FakePaymentRepository();
        var gateway = new FakeRazorpayGateway { SignatureIsValid = false };
        var service = CreatePaymentService(memberships, payments, gateway);
        var order = await service.CreateOrderAsync(UserId, new CreatePaymentOrderRequest());

        await Assert.ThrowsAsync<BadRequestException>(() => service.ConfirmAsync(
            UserId, order.PaymentId,
            new ConfirmRazorpayPaymentRequest(order.ProviderOrderId, "pay_1", new('a', 64))));
        Assert.NotEqual(MembershipStatus.Active, memberships.Membership!.Status);
    }

    private static FakeMembershipRepository AvailableRepository() => new()
    {
        Jobs = { ["job"] = new(Guid.NewGuid(), "https://example.test/apply") }
    };

    private static PaymentService CreatePaymentService(
        FakeMembershipRepository memberships, FakePaymentRepository payments, FakeRazorpayGateway gateway) =>
        new(payments, memberships, gateway, new FakePlanProvider(), new FakeUnitOfWork(),
            new CreatePaymentOrderRequestValidator(), new ConfirmRazorpayPaymentRequestValidator(),
            TimeProvider.System);

    private sealed class FakeMembershipRepository : IMembershipRepository
    {
        public Dictionary<string, AvailableJobAccess> Jobs { get; init; } = [];
        public List<Guid> RecordedApplications { get; } = [];
        public Membership? Membership { get; set; }

        public Task<AvailableJobAccess?> GetAvailableJobAsync(string slug, CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs.GetValueOrDefault(slug));
        public Task<Membership?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Membership is { Status: MembershipStatus.Active } membership &&
                (!membership.EndsAtUtc.HasValue || membership.EndsAtUtc > DateTime.UtcNow) ? membership : null);
        public Task<Membership?> GetPortalMembershipForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Membership);
        public Task<Membership?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Membership?.Id == id ? Membership : null);
        public Task AddAsync(Membership membership, CancellationToken cancellationToken = default)
        {
            Membership = membership;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyCollection<MembershipResponse>> GetMembershipsForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<MembershipResponse>>([]);
        public Task<(IReadOnlyCollection<MembershipHistoryResponse> Items, int TotalCount)> GetHistoryAsync(
            Guid userId, HistoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<MembershipHistoryResponse>)[], 0));
        public Task RecordApplicationAsync(Guid userId, Guid jobId, CancellationToken cancellationToken = default)
        {
            RecordedApplications.Add(jobId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private Payment? _payment;
        public Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
        {
            _payment = payment;
            payment.MembershipId = payment.Membership?.Id;
            return Task.CompletedTask;
        }
        public Task<Payment?> GetOwnedAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_payment?.Id == id && _payment.UserId == userId ? _payment : null);
        public Task<(IReadOnlyCollection<PaymentResponse> Items, int TotalCount)> GetForUserAsync(
            Guid userId, HistoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<PaymentResponse>)[], 0));
        public Task<(IReadOnlyCollection<PaymentHistoryResponse> Items, int TotalCount)> GetHistoryAsync(
            Guid userId, HistoryQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(((IReadOnlyCollection<PaymentHistoryResponse>)[], 0));
    }

    private sealed class FakeRazorpayGateway : IRazorpayGateway
    {
        public string KeyId => "key_test";
        public long RequestedAmount { get; private set; }
        public bool SignatureIsValid { get; init; } = true;
        public Task<RazorpayOrder> CreateOrderAsync(long amountInMinorUnits, string currencyCode, string receipt, CancellationToken cancellationToken = default)
        {
            RequestedAmount = amountInMinorUnits;
            return Task.FromResult(new RazorpayOrder("order_1", amountInMinorUnits, currencyCode, receipt));
        }
        public bool VerifyPaymentSignature(string orderId, string paymentId, string signature) => SignatureIsValid;
    }

    private sealed class FakePlanProvider : IMembershipPlanProvider
    {
        public MembershipPlan GetDefaultPlan() => new("Portal", 123.45m, "INR", 30);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
