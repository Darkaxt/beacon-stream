using System.Security.Cryptography;
using Beacon.Server.State;
using Beacon.StreamWorker.Contracts.Worker.V1;
using Google.Protobuf;

namespace Beacon.Server.Security;

public enum StreamTicketFailure
{
    None,
    NotFound,
    AlreadyUsed,
    Revoked,
    Expired,
    ClientMismatch,
    SessionMismatch,
    PlanRevisionMismatch,
    WorkerMismatch,
}

public sealed record StreamTicketValidation(bool Success, StreamTicketFailure Failure)
{
    public static StreamTicketValidation Accepted { get; } = new(true, StreamTicketFailure.None);

    public static StreamTicketValidation Reject(StreamTicketFailure failure) => new(false, failure);
}

public sealed class IssuedStreamTicket
{
    internal IssuedStreamTicket(string ticketId, string ticket, DateTimeOffset expiresAt)
    {
        TicketId = ticketId;
        Ticket = ticket;
        ExpiresAt = expiresAt;
    }

    public string TicketId { get; }

    public string Ticket { get; }

    public DateTimeOffset ExpiresAt { get; }

    public override string ToString() => $"Stream ticket {TicketId}, expires {ExpiresAt:O}: [redacted].";
}

public sealed record PendingStreamTicketRevocation(
    string TicketId,
    string SessionId,
    byte[] TicketHash);

public sealed class StreamTicketService
{
    private readonly object gate = new();
    private readonly Dictionary<string, StreamTicketRecord> ticketsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StreamTicketRecord> ticketsByHash = new(StringComparer.Ordinal);

    public IssuedStreamTicket Issue(
        string clientId,
        string sessionId,
        ulong planRevision,
        ReadOnlySpan<byte> workerInstanceId,
        DateTimeOffset issuedAt,
        TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromMinutes(5))
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Ticket lifetime must be at most five minutes.");
        }
        if (workerInstanceId.IsEmpty)
        {
            throw new ArgumentException("Worker instance id is required.", nameof(workerInstanceId));
        }
        if (planRevision == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(planRevision), "Plan revision must be nonzero.");
        }

        byte[] ticket = RandomNumberGenerator.GetBytes(32);
        byte[] hash = SHA256.HashData(ticket);
        string ticketId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var record = new StreamTicketRecord
        {
            TicketId = ticketId,
            TicketHash = hash,
            ClientId = RequireText(clientId, nameof(clientId)),
            SessionId = RequireText(sessionId, nameof(sessionId)),
            PlanRevision = planRevision,
            WorkerInstanceId = workerInstanceId.ToArray(),
            IssuedAt = issuedAt,
            ExpiresAt = issuedAt.Add(lifetime),
        };
        lock (gate)
        {
            ticketsById[ticketId] = record;
            ticketsByHash[Convert.ToHexString(hash)] = record;
        }
        try
        {
            return new IssuedStreamTicket(ticketId, Convert.ToBase64String(ticket), record.ExpiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ticket);
        }
    }

    public IssuedStreamTicket ReplaceForReconnect(
        string clientId,
        string sessionId,
        ulong planRevision,
        ReadOnlySpan<byte> workerInstanceId,
        DateTimeOffset issuedAt,
        TimeSpan lifetime)
    {
        lock (gate)
        {
            foreach (StreamTicketRecord record in ticketsById.Values)
            {
                if (string.Equals(record.ClientId, clientId, StringComparison.Ordinal)
                    && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal)
                    && !record.Consumed)
                {
                    record.Revoked = true;
                }
            }
        }
        return Issue(clientId, sessionId, planRevision, workerInstanceId, issuedAt, lifetime);
    }

    public AuthorizeTicket CreateWorkerAuthorization(string ticketId)
    {
        StreamTicketRecord record;
        lock (gate)
        {
            record = ticketsById.GetValueOrDefault(ticketId)
                ?? throw new KeyNotFoundException("Stream ticket was not found.");
        }
        return new AuthorizeTicket
        {
            TicketHash = ByteString.CopyFrom(record.TicketHash),
            ClientId = record.ClientId,
            PlanRevision = record.PlanRevision,
            ExpiresAtUnixMs = checked((ulong)record.ExpiresAt.ToUnixTimeMilliseconds()),
            WorkerInstanceId = ByteString.CopyFrom(record.WorkerInstanceId),
        };
    }

    public StreamTicketValidation Consume(
        string submittedTicket,
        string clientId,
        string sessionId,
        ulong planRevision,
        ReadOnlySpan<byte> workerInstanceId,
        DateTimeOffset now)
    {
        byte[] submitted;
        try
        {
            submitted = Convert.FromBase64String(submittedTicket);
        }
        catch (FormatException)
        {
            return StreamTicketValidation.Reject(StreamTicketFailure.NotFound);
        }
        try
        {
            byte[] hash = SHA256.HashData(submitted);
            lock (gate)
            {
                if (!ticketsByHash.TryGetValue(Convert.ToHexString(hash), out StreamTicketRecord? record))
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.NotFound);
                }
                if (record.Revoked)
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.Revoked);
                }
                if (record.Consumed)
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.AlreadyUsed);
                }
                if (now >= record.ExpiresAt)
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.Expired);
                }
                if (!string.Equals(record.ClientId, clientId, StringComparison.Ordinal))
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.ClientMismatch);
                }
                if (!string.Equals(record.SessionId, sessionId, StringComparison.Ordinal))
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.SessionMismatch);
                }
                if (record.PlanRevision != planRevision)
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.PlanRevisionMismatch);
                }
                if (!CryptographicOperations.FixedTimeEquals(record.WorkerInstanceId, workerInstanceId))
                {
                    return StreamTicketValidation.Reject(StreamTicketFailure.WorkerMismatch);
                }
                record.Consumed = true;
                return StreamTicketValidation.Accepted;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(submitted);
        }
    }

    public void Revoke(string ticketId)
    {
        lock (gate)
        {
            if (ticketsById.TryGetValue(ticketId, out StreamTicketRecord? record))
            {
                record.Revoked = true;
            }
        }
    }

    public void RevokeUnusedForSession(string clientId, string sessionId)
    {
        lock (gate)
        {
            foreach (StreamTicketRecord record in ticketsById.Values)
            {
                if (!record.Consumed
                    && string.Equals(record.ClientId, clientId, StringComparison.Ordinal)
                    && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal))
                {
                    record.Revoked = true;
                }
            }
        }
    }

    public IReadOnlyList<PendingStreamTicketRevocation> GetPendingWorkerRevocations(
        string clientId,
        string sessionId)
    {
        lock (gate)
        {
            return ticketsById.Values
                .Where(record => record.Revoked
                    && !record.WorkerRevocationSent
                    && string.Equals(record.ClientId, clientId, StringComparison.Ordinal)
                    && string.Equals(record.SessionId, sessionId, StringComparison.Ordinal))
                .Select(record => new PendingStreamTicketRevocation(
                    record.TicketId,
                    record.SessionId,
                    (byte[])record.TicketHash.Clone()))
                .ToArray();
        }
    }

    public void MarkWorkerRevocationSent(string ticketId)
    {
        lock (gate)
        {
            if (ticketsById.TryGetValue(ticketId, out StreamTicketRecord? record))
            {
                record.WorkerRevocationSent = true;
            }
        }
    }

    private static string RequireText(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameter)
            : value.Trim();
}
