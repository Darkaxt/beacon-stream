using System.IO.Pipes;
using System.Security.Principal;

namespace Beacon.HostAgent.Tests;

public sealed class WindowsHostAgentCallerVerifierTests
{
    [Fact]
    public async Task CurrentOwnerClientIsAcceptedFromKernelIdentity()
    {
        SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows identity has no SID.");
        string pipeName = $"beacon-host-agent-verifier-{Guid.NewGuid():N}";
        PipeSecurity security = HostAgentPipeIdentity.CreateSecurity(owner);
        await using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        Task connect = client.ConnectAsync(CancellationToken.None);
        await server.WaitForConnectionAsync(CancellationToken.None);
        await connect;

        HostAgentCallerVerification result =
            new WindowsHostAgentCallerVerifier(owner).Verify(server);

        Assert.True(result.Accepted, result.Diagnostic);
        Assert.Equal(owner, result.CallerSid);
        Assert.True(result.ProcessId > 0);
    }

    [Fact]
    public async Task DifferentExpectedOwnerIsRejected()
    {
        SecurityIdentifier current = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Current Windows identity has no SID.");
        var other = new SecurityIdentifier("S-1-5-21-100-200-300-1001");
        string pipeName = $"beacon-host-agent-verifier-{Guid.NewGuid():N}";
        PipeSecurity security = HostAgentPipeIdentity.CreateSecurity(current);
        await using NamedPipeServerStream server = NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            security);
        await using var client = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        Task connect = client.ConnectAsync(CancellationToken.None);
        await server.WaitForConnectionAsync(CancellationToken.None);
        await connect;

        HostAgentCallerVerification result =
            new WindowsHostAgentCallerVerifier(other).Verify(server);

        Assert.False(result.Accepted);
        Assert.Equal(current, result.CallerSid);
        Assert.Contains("owner SID", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }
}
