using Beacon.Core.Streaming;
using Beacon.Server.Security;

namespace Beacon.Server.Tests.Security;

public sealed class StreamTicketProvisioningServiceTests
{
    [Fact]
    public async Task AuthorizationExceptionRevokesTheNewTicketBeforeReturningFailure()
    {
        var tickets = new StreamTicketService();
        var service = new StreamTicketProvisioningService(
            tickets,
            new ThrowingAuthorizer(throwOnAuthorize: true, throwOnRevoke: false));

        StreamTicketProvisioningResult result = await service.ProvisionAsync(
            "z-fold-7",
            "session-1",
            planRevision: 8,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Ticket);
        Assert.Single(tickets.GetPendingWorkerRevocations("z-fold-7", "session-1"));
    }

    [Fact]
    public async Task RevocationExceptionReturnsFailureAndLeavesRevocationPending()
    {
        var tickets = new StreamTicketService();
        _ = tickets.Issue(
            "z-fold-7",
            "session-1",
            planRevision: 8,
            [1, 2, 3, 4],
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(2));
        var service = new StreamTicketProvisioningService(
            tickets,
            new ThrowingAuthorizer(throwOnAuthorize: false, throwOnRevoke: true));

        StreamTicketProvisioningResult result = await service.RevokeSessionAsync(
            "z-fold-7",
            "session-1",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(tickets.GetPendingWorkerRevocations("z-fold-7", "session-1"));
    }

    private sealed class ThrowingAuthorizer(bool throwOnAuthorize, bool throwOnRevoke)
        : IStreamSessionAuthorizer
    {
        public Task<StreamWorkerAuthorizationContext> GetContextAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StreamWorkerAuthorizationContext([1, 2, 3, 4]));

        public Task<StreamWorkerAuthorizationResult> AuthorizeAsync(
            StreamWorkerAuthorization authorization,
            CancellationToken cancellationToken) =>
            throwOnAuthorize
                ? Task.FromException<StreamWorkerAuthorizationResult>(new InvalidOperationException("authorize failed"))
                : Task.FromResult(StreamWorkerAuthorizationResult.Accepted);

        public Task<StreamWorkerAuthorizationResult> RevokeAsync(
            StreamWorkerRevocation revocation,
            CancellationToken cancellationToken) =>
            throwOnRevoke
                ? Task.FromException<StreamWorkerAuthorizationResult>(new InvalidOperationException("revoke failed"))
                : Task.FromResult(StreamWorkerAuthorizationResult.Accepted);
    }
}
