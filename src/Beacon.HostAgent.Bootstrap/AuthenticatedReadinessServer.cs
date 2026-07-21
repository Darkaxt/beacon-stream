using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Beacon.HostAgent.Update;
using Microsoft.Win32.SafeHandles;

namespace Beacon.HostAgent.Bootstrap;

internal sealed class AuthenticatedReadinessServer : IAsyncDisposable
{
    private readonly string versionId;
    private readonly NamedPipeServerStream pipe;

    public AuthenticatedReadinessServer(SecurityIdentifier owner, string versionId)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (string.IsNullOrWhiteSpace(versionId))
        {
            throw new ArgumentException("Host Agent version id is required.", nameof(versionId));
        }
        this.versionId = versionId;
        PipeName = $"beacon-host-agent-ready-{Guid.NewGuid():N}";
        pipe = NamedPipeServerStreamAcl.Create(
            PipeName,
            PipeDirection.In,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: HostAgentBootstrapReadinessProtocol.MaximumFrameBytes,
            outBufferSize: 0,
            CreateSecurity(owner),
            HandleInheritability.None,
            additionalAccessRights: 0);
    }

    public string PipeName { get; }

    public async Task WaitAsync(int expectedProcessId)
    {
        await pipe.WaitForConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out uint actualProcessId))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to identify bootstrap readiness client process.");
        }
        if (actualProcessId != checked((uint)expectedProcessId))
        {
            throw new InvalidDataException("Bootstrap readiness connected from another process.");
        }

        HostAgentBootstrapReadiness readiness = await HostAgentBootstrapReadinessProtocol.ReadAsync(
            pipe,
            CancellationToken.None).ConfigureAwait(false);
        if (readiness.ProcessId != expectedProcessId
            || !string.Equals(readiness.VersionId, versionId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Bootstrap readiness identity does not match its child.");
        }
    }

    public ValueTask DisposeAsync() => pipe.DisposeAsync();

    private static PipeSecurity CreateSecurity(SecurityIdentifier owner)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        AddFullControl(security, owner);
        AddFullControl(
            security,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, domainSid: null));
        AddFullControl(
            security,
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, domainSid: null));
        return security;
    }

    private static void AddFullControl(PipeSecurity security, SecurityIdentifier identity) =>
        security.AddAccessRule(new PipeAccessRule(
            identity,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);
}
