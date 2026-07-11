using System.Security.Cryptography;
using System.Text.Json;

namespace Beacon.Server.Security;

public enum ClientRegistrationState
{
    Pending,
    Approved,
    Revoked,
}

public sealed class PendingClientRegistration
{
    internal PendingClientRegistration(string registrationId, string clientId, string name)
    {
        RegistrationId = registrationId;
        ClientId = clientId;
        Name = name;
    }

    public string RegistrationId { get; }

    public string ClientId { get; }

    public string Name { get; }

    public ClientRegistrationState State { get; internal set; } = ClientRegistrationState.Pending;

    internal TaskCompletionSource ApprovalSignal { get; } = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    internal ApprovedClientCredential? Delivery { get; set; }

    public override string ToString() =>
        $"Client registration {RegistrationId} for {ClientId}: {State}.";
}

public sealed class ApprovedClientCredential
{
    internal ApprovedClientCredential(string clientId, string credential)
    {
        ClientId = clientId;
        Credential = credential;
    }

    public string ClientId { get; }

    public string Credential { get; }

    public override string ToString() => $"Approved Beacon credential for {ClientId}: [redacted].";
}

public sealed class ClientCredentialService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly object gate = new();
    private readonly string? path;
    private readonly Dictionary<string, PendingClientRegistration> registrations =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, PersistedClientCredential> credentials =
        new(StringComparer.OrdinalIgnoreCase);

    public ClientCredentialService(string? path = null)
    {
        this.path = string.IsNullOrWhiteSpace(path) ? null : Path.GetFullPath(path);
        if (this.path is not null && File.Exists(this.path))
        {
            try
            {
                PersistedClientCredential[] stored = JsonSerializer.Deserialize<PersistedClientCredential[]>(
                    File.ReadAllText(this.path),
                    JsonOptions) ?? [];
                foreach (PersistedClientCredential credential in stored)
                {
                    ValidatePersistedCredential(credential);
                    credentials[credential.ClientId] = credential;
                }
            }
            catch (Exception error) when (
                error is JsonException or FormatException or ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException("Beacon client credential store could not be loaded.", error);
            }
        }
    }

    public PendingClientRegistration RequestRegistration(string clientId, string? name)
    {
        string resolvedClientId = RequireText(clientId, nameof(clientId));
        string resolvedName = string.IsNullOrWhiteSpace(name) ? resolvedClientId : name.Trim();
        lock (gate)
        {
            PendingClientRegistration? existing = registrations.Values.FirstOrDefault(registration =>
                registration.State == ClientRegistrationState.Pending
                && string.Equals(registration.ClientId, resolvedClientId, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                return existing;
            }
            var registration = new PendingClientRegistration(
                Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
                resolvedClientId,
                resolvedName);
            registrations[registration.RegistrationId] = registration;
            return registration;
        }
    }

    public PendingClientRegistration? GetRegistration(string registrationId)
    {
        lock (gate)
        {
            return registrations.GetValueOrDefault(registrationId);
        }
    }

    public IReadOnlyList<PendingClientRegistration> GetPendingRegistrations()
    {
        lock (gate)
        {
            return registrations.Values
                .Where(registration => registration.State == ClientRegistrationState.Pending)
                .OrderBy(registration => registration.ClientId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public void Approve(string registrationId)
    {
        PendingClientRegistration registration;
        ApprovedClientCredential approved;
        lock (gate)
        {
            registration = registrations.GetValueOrDefault(registrationId)
                ?? throw new KeyNotFoundException("Client registration was not found.");
            if (registration.State != ClientRegistrationState.Pending)
            {
                throw new InvalidOperationException("Client registration is not pending.");
            }

            byte[] credentialBytes = RandomNumberGenerator.GetBytes(32);
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            try
            {
                string credential = Convert.ToBase64String(credentialBytes);
                byte[] hash = HashCredential(salt, credentialBytes);
                credentials[registration.ClientId] = new PersistedClientCredential(
                    registration.ClientId,
                    Convert.ToBase64String(salt),
                    Convert.ToBase64String(hash),
                    Revoked: false);
                Persist();
                registration.State = ClientRegistrationState.Approved;
                approved = new ApprovedClientCredential(registration.ClientId, credential);
                registration.Delivery = approved;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(credentialBytes);
                CryptographicOperations.ZeroMemory(salt);
            }
        }
        registration.ApprovalSignal.TrySetResult();
    }

    public async Task<ApprovedClientCredential> WaitForApprovalAsync(
        string registrationId,
        CancellationToken cancellationToken)
    {
        PendingClientRegistration registration;
        lock (gate)
        {
            registration = registrations.GetValueOrDefault(registrationId)
                ?? throw new KeyNotFoundException("Client registration was not found.");
        }
        await registration.ApprovalSignal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            ApprovedClientCredential delivery = registration.Delivery
                ?? throw new InvalidOperationException("Approved credential was already delivered.");
            registration.Delivery = null;
            return delivery;
        }
    }

    public bool Authenticate(string clientId, string? credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            return false;
        }
        PersistedClientCredential? stored;
        lock (gate)
        {
            stored = credentials.GetValueOrDefault(clientId);
        }
        if (stored is null || stored.Revoked)
        {
            return false;
        }

        byte[] submitted;
        try
        {
            submitted = Convert.FromBase64String(credential);
        }
        catch (FormatException)
        {
            return false;
        }
        byte[]? salt = null;
        byte[]? actual = null;
        byte[]? expected = null;
        try
        {
            salt = Convert.FromBase64String(stored.Salt);
            actual = HashCredential(salt, submitted);
            expected = Convert.FromBase64String(stored.Hash);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(submitted);
            if (salt is not null)
            {
                CryptographicOperations.ZeroMemory(salt);
            }
            if (actual is not null)
            {
                CryptographicOperations.ZeroMemory(actual);
            }
            if (expected is not null)
            {
                CryptographicOperations.ZeroMemory(expected);
            }
        }
    }

    public string? Authenticate(string? credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            return null;
        }
        string[] clientIds;
        lock (gate)
        {
            clientIds = credentials.Keys.ToArray();
        }
        foreach (string clientId in clientIds)
        {
            if (Authenticate(clientId, credential))
            {
                return clientId;
            }
        }
        return null;
    }

    public void Revoke(string clientId)
    {
        lock (gate)
        {
            if (credentials.TryGetValue(clientId, out PersistedClientCredential? credential))
            {
                credentials[clientId] = credential with { Revoked = true };
                Persist();
            }
        }
    }

    private void Persist()
    {
        if (path is null)
        {
            return;
        }
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(credentials.Values.OrderBy(value => value.ClientId), JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static byte[] HashCredential(byte[] salt, byte[] credential)
    {
        byte[] material = new byte[salt.Length + credential.Length];
        salt.CopyTo(material, 0);
        credential.CopyTo(material, salt.Length);
        try
        {
            return SHA256.HashData(material);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static void ValidatePersistedCredential(PersistedClientCredential credential)
    {
        _ = RequireText(credential.ClientId, nameof(credential.ClientId));
        byte[]? salt = null;
        byte[]? hash = null;
        try
        {
            salt = Convert.FromBase64String(credential.Salt);
            hash = Convert.FromBase64String(credential.Hash);
            if (salt.Length != 32 || hash.Length != 32)
            {
                throw new InvalidDataException("Persisted credential salt and hash must be 32 bytes.");
            }
        }
        finally
        {
            if (salt is not null)
            {
                CryptographicOperations.ZeroMemory(salt);
            }
            if (hash is not null)
            {
                CryptographicOperations.ZeroMemory(hash);
            }
        }
    }

    private static string RequireText(string value, string parameter) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameter)
            : value.Trim();

    private sealed record PersistedClientCredential(
        string ClientId,
        string Salt,
        string Hash,
        bool Revoked);
}
