using Beacon.HostAgent.Contracts;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.HostAgent;

public interface IHostAgentDriverUpdateClient
{
    Task<SudoVdaUpdatePayload> StartAsync(
        string packageId,
        Guid transactionId,
        CancellationToken cancellationToken);

    Task<SudoVdaUpdatePayload> QueryAsync(
        Guid transactionId,
        CancellationToken cancellationToken);
}

public sealed class HostAgentDriverUpdateClient(
    IHostAgentConnection connection,
    IWindowsDisplayLeaseSession leases) : IHostAgentDriverUpdateClient
{
    public async Task<SudoVdaUpdatePayload> StartAsync(
        string packageId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Driver package id is required.", nameof(packageId));
        }
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Driver update transaction id is required.", nameof(transactionId));
        }

        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.InstallStagedSudoVdaPackage,
            new InstallStagedSudoVdaPackagePayload(
                packageId.Trim(),
                transactionId,
                leases.Snapshot.LeaseCount),
            cancellationToken).ConfigureAwait(false);
        return ReadResult(response);
    }

    public async Task<SudoVdaUpdatePayload> QueryAsync(
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        if (transactionId == Guid.Empty)
        {
            throw new ArgumentException("Driver update transaction id is required.", nameof(transactionId));
        }
        HostAgentResponse response = await connection.SendAsync(
            HostAgentOperation.QuerySudoVdaUpdate,
            new QuerySudoVdaUpdatePayload(transactionId),
            cancellationToken).ConfigureAwait(false);
        return ReadResult(response);
    }

    private static SudoVdaUpdatePayload ReadResult(HostAgentResponse response)
    {
        if (!response.Success)
        {
            throw new HostAgentDriverUpdateException(
                response.ResultCode,
                string.IsNullOrWhiteSpace(response.Diagnostic)
                    ? "Host Agent driver update request failed."
                    : response.Diagnostic);
        }
        return HostAgentProtocol.ReadPayload<SudoVdaUpdatePayload>(response.Payload);
    }
}

public sealed class HostAgentDriverUpdateException : InvalidOperationException
{
    public HostAgentDriverUpdateException(string resultCode, string message)
        : base(message)
    {
        ResultCode = resultCode;
    }

    public string ResultCode { get; }
}
