using System.Security.Cryptography;
using Beacon.Server.Security;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Server.Tests.Security;

public sealed class StreamTicketServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 1, 0, 0, TimeSpan.Zero);
    private static readonly byte[] WorkerInstance = [1, 2, 3, 4];

    [Fact]
    public void TicketIsHighEntropyHashOnlyProvisionedAndSingleUse()
    {
        var service = new StreamTicketService();
        IssuedStreamTicket issued = service.Issue(
            "z-fold-7",
            "session-1",
            planRevision: 8,
            WorkerInstance,
            Now,
            TimeSpan.FromMinutes(2));

        Assert.True(Convert.FromBase64String(issued.Ticket).Length >= 32);
        AuthorizeTicket authorization = service.CreateWorkerAuthorization(issued.TicketId);
        Assert.Equal(32, authorization.TicketHash.Length);
        Assert.False(CryptographicOperations.FixedTimeEquals(
            Convert.FromBase64String(issued.Ticket),
            authorization.TicketHash.Span));
        Assert.DoesNotContain(issued.Ticket, issued.ToString());

        StreamTicketValidation accepted = service.Consume(
            issued.Ticket,
            "z-fold-7",
            "session-1",
            planRevision: 8,
            WorkerInstance,
            Now.AddSeconds(1));
        StreamTicketValidation replay = service.Consume(
            issued.Ticket,
            "z-fold-7",
            "session-1",
            planRevision: 8,
            WorkerInstance,
            Now.AddSeconds(2));

        Assert.True(accepted.Success);
        Assert.Equal(StreamTicketFailure.AlreadyUsed, replay.Failure);
    }

    [Theory]
    [InlineData("other-client", "session-1", 8, StreamTicketFailure.ClientMismatch)]
    [InlineData("z-fold-7", "session-2", 8, StreamTicketFailure.SessionMismatch)]
    [InlineData("z-fold-7", "session-1", 9, StreamTicketFailure.PlanRevisionMismatch)]
    public void TicketRejectsBindingMismatch(
        string clientId,
        string sessionId,
        ulong revision,
        StreamTicketFailure expected)
    {
        var service = new StreamTicketService();
        IssuedStreamTicket issued = service.Issue(
            "z-fold-7",
            "session-1",
            8,
            WorkerInstance,
            Now,
            TimeSpan.FromMinutes(2));

        StreamTicketValidation result = service.Consume(
            issued.Ticket,
            clientId,
            sessionId,
            revision,
            WorkerInstance,
            Now.AddSeconds(1));

        Assert.False(result.Success);
        Assert.Equal(expected, result.Failure);
    }

    [Fact]
    public void TicketRejectsWorkerMismatchExpiryAndRevocationWithoutEndingSession()
    {
        var service = new StreamTicketService();
        IssuedStreamTicket workerBound = service.Issue(
            "z-fold-7", "session-1", 8, WorkerInstance, Now, TimeSpan.FromMinutes(2));
        IssuedStreamTicket expiring = service.Issue(
            "z-fold-7", "session-2", 9, WorkerInstance, Now, TimeSpan.FromSeconds(1));
        IssuedStreamTicket revoked = service.Issue(
            "z-fold-7", "session-3", 10, WorkerInstance, Now, TimeSpan.FromMinutes(2));
        service.Revoke(revoked.TicketId);

        Assert.Equal(
            StreamTicketFailure.WorkerMismatch,
            service.Consume(workerBound.Ticket, "z-fold-7", "session-1", 8, [9], Now).Failure);
        Assert.Equal(
            StreamTicketFailure.Expired,
            service.Consume(expiring.Ticket, "z-fold-7", "session-2", 9, WorkerInstance, Now.AddSeconds(2)).Failure);
        Assert.Equal(
            StreamTicketFailure.Revoked,
            service.Consume(revoked.Ticket, "z-fold-7", "session-3", 10, WorkerInstance, Now).Failure);
    }

    [Fact]
    public void ReconnectReplacesPriorUnusedTicket()
    {
        var service = new StreamTicketService();
        IssuedStreamTicket first = service.Issue(
            "z-fold-7", "session-1", 8, WorkerInstance, Now, TimeSpan.FromMinutes(2));
        IssuedStreamTicket replacement = service.ReplaceForReconnect(
            "z-fold-7", "session-1", 8, WorkerInstance, Now.AddSeconds(1), TimeSpan.FromMinutes(2));

        Assert.NotEqual(first.Ticket, replacement.Ticket);
        PendingStreamTicketRevocation pending = Assert.Single(
            service.GetPendingWorkerRevocations("z-fold-7", "session-1"));
        Assert.Equal(first.TicketId, pending.TicketId);
        service.MarkWorkerRevocationSent(pending.TicketId);
        Assert.Empty(service.GetPendingWorkerRevocations("z-fold-7", "session-1"));
        Assert.Equal(
            StreamTicketFailure.Revoked,
            service.Consume(first.Ticket, "z-fold-7", "session-1", 8, WorkerInstance, Now.AddSeconds(2)).Failure);
        Assert.True(service.Consume(
            replacement.Ticket,
            "z-fold-7",
            "session-1",
            8,
            WorkerInstance,
            Now.AddSeconds(2)).Success);
    }

    [Fact]
    public void ZeroPlanRevisionIsRejectedBeforeTicketIssuance()
    {
        var service = new StreamTicketService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Issue(
            "z-fold-7",
            "session-1",
            planRevision: 0,
            WorkerInstance,
            Now,
            TimeSpan.FromMinutes(2)));
    }
}
