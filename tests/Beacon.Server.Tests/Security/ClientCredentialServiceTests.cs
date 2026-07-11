using Beacon.Server.Security;

namespace Beacon.Server.Tests.Security;

public sealed class ClientCredentialServiceTests
{
    [Fact]
    public async Task RegistrationStaysPendingUntilExplicitApproval()
    {
        var service = new ClientCredentialService();
        PendingClientRegistration pending = service.RequestRegistration("z-fold-7", "Z Fold 7");

        Assert.Equal(ClientRegistrationState.Pending, service.GetRegistration(pending.RegistrationId)?.State);
        Assert.False(service.Authenticate("z-fold-7", "not-a-credential"));

        Task<ApprovedClientCredential> completion = service.WaitForApprovalAsync(
            pending.RegistrationId,
            CancellationToken.None);
        Assert.False(completion.IsCompleted);

        service.Approve(pending.RegistrationId);
        ApprovedClientCredential approved = await completion;

        Assert.Equal("z-fold-7", approved.ClientId);
        Assert.True(service.Authenticate("z-fold-7", approved.Credential));
        Assert.False(service.Authenticate("other-client", approved.Credential));
        Assert.DoesNotContain(approved.Credential, service.GetRegistration(pending.RegistrationId)!.ToString());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.WaitForApprovalAsync(pending.RegistrationId, CancellationToken.None));
    }

    [Fact]
    public async Task CredentialPersistsOnlyAsSaltedHashAndCanBeRevoked()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beacon-credentials-{Guid.NewGuid():N}.json");
        try
        {
            var first = new ClientCredentialService(path);
            PendingClientRegistration pending = first.RequestRegistration("z-fold-7", "Z Fold 7");
            first.Approve(pending.RegistrationId);
            ApprovedClientCredential approved = await first.WaitForApprovalAsync(
                pending.RegistrationId,
                CancellationToken.None);

            string persisted = File.ReadAllText(path);
            Assert.DoesNotContain(approved.Credential, persisted, StringComparison.Ordinal);

            var second = new ClientCredentialService(path);
            Assert.True(second.Authenticate("z-fold-7", approved.Credential));

            second.Revoke("z-fold-7");

            Assert.False(second.Authenticate("z-fold-7", approved.Credential));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[{\"clientId\":\"z-fold-7\",\"salt\":\"YQ==\",\"hash\":\"Yg==\",\"revoked\":false}]")]
    public void CorruptCredentialStoreFailsClosed(string contents)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beacon-credentials-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, contents);

            InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
                new ClientCredentialService(path));

            Assert.Contains("credential store", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(contents, File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
