using System.Security.Principal;
using Beacon.HostAgent.Contracts;
using Beacon.HostAgent.Update;

namespace Beacon.HostAgent.Bootstrap;

internal sealed class BootstrapSupervisor(
    SecurityIdentifier owner,
    BootstrapStorage storage,
    HostAgentUpdateStateStore state,
    HostAgentUpdateJournal journal,
    IHostAgentUpdateActivator activator,
    IBootstrapPlatform platform)
{
    public async Task<int> RunAsync()
    {
        HostAgentSelectedVersion current = state.ReadCurrent()
            ?? throw new InvalidDataException("Selected Host Agent version is unavailable.");
        HostAgentSelectedVersion? previous = null;
        HostAgentPendingUpdate? pending = state.ReadPending();
        LaunchMode mode = RecoverMode(current, pending, journal);
        if (pending is not null)
        {
            previous = new HostAgentSelectedVersion(
                pending.PreviousVersionId,
                pending.PreviousSourceCommit);
        }

        while (true)
        {
            await using IBootstrapChildLaunch launch = platform.Launch(
                owner,
                current,
                storage.GetVersionExecutable(current.VersionId));
            Task first = await Task.WhenAny(launch.Readiness, launch.ExitCode).ConfigureAwait(false);
            if (ReferenceEquals(first, launch.ExitCode))
            {
                int earlyExit = await launch.ExitCode.ConfigureAwait(false);
                if (mode == LaunchMode.Candidate && pending is not null && previous is not null)
                {
                    state.WriteCurrent(previous);
                    Write(
                        pending,
                        HostAgentUpdateState.Installing,
                        "host-agent-update-rolling-back",
                        activeVersionId: current.VersionId);
                    current = previous;
                    mode = LaunchMode.Rollback;
                    continue;
                }
                if (mode == LaunchMode.Rollback && pending is not null)
                {
                    Write(
                        pending,
                        HostAgentUpdateState.Degraded,
                        "host-agent-update-rollback-startup-failed",
                        activeVersionId: current.VersionId);
                }
                return earlyExit;
            }

            try
            {
                await launch.Readiness.ConfigureAwait(false);
            }
            catch (Exception) when (mode == LaunchMode.Candidate && pending is not null && previous is not null)
            {
                state.WriteCurrent(previous);
                Write(
                    pending,
                    HostAgentUpdateState.Installing,
                    "host-agent-update-rolling-back",
                    activeVersionId: current.VersionId);
                current = previous;
                mode = LaunchMode.Rollback;
                continue;
            }
            catch (Exception) when (mode == LaunchMode.Rollback && pending is not null)
            {
                Write(
                    pending,
                    HostAgentUpdateState.Degraded,
                    "host-agent-update-rollback-readiness-failed",
                    activeVersionId: current.VersionId);
                return BootstrapExitCodes.InvalidState;
            }
            if (mode == LaunchMode.Candidate && pending is not null)
            {
                Write(
                    pending,
                    HostAgentUpdateState.Succeeded,
                    "host-agent-update-succeeded",
                    activeVersionId: current.VersionId);
                state.ClearPending();
                pending = null;
                previous = null;
                mode = LaunchMode.Current;
            }
            else if (mode == LaunchMode.Rollback && pending is not null)
            {
                Write(
                    pending,
                    HostAgentUpdateState.RolledBack,
                    "host-agent-update-rolled-back",
                    activeVersionId: current.VersionId);
                state.ClearPending();
                pending = null;
                previous = null;
                mode = LaunchMode.Current;
            }

            int exitCode = await launch.ExitCode.ConfigureAwait(false);
            if (exitCode != BootstrapExitCodes.ApplyUpdate)
            {
                return exitCode;
            }

            pending = state.ReadPending();
            if (pending is null
                || !journal.TryRead(pending.TransactionId, out HostAgentUpdatePayload? staged)
                || staged is null
                || staged.State != HostAgentUpdateState.Staged
                || !string.Equals(staged.PackageId, pending.PackageId, StringComparison.Ordinal)
                || !string.Equals(pending.PreviousVersionId, current.VersionId, StringComparison.Ordinal))
            {
                return BootstrapExitCodes.InvalidState;
            }

            previous = current;
            try
            {
                Write(
                    pending,
                    HostAgentUpdateState.Installing,
                    "host-agent-update-installing",
                    activeVersionId: current.VersionId);
                HostAgentSelectedVersion candidate = await activator.ActivateAsync(pending, current)
                    .ConfigureAwait(false);
                state.WriteCurrent(candidate);
                Write(
                    pending,
                    HostAgentUpdateState.AwaitingReadiness,
                    "host-agent-update-awaiting-readiness",
                    activeVersionId: candidate.VersionId);
                current = candidate;
                mode = LaunchMode.Candidate;
            }
            catch (Exception)
            {
                Write(
                    pending,
                    HostAgentUpdateState.Degraded,
                    "host-agent-update-activation-failed",
                    activeVersionId: current.VersionId);
                return BootstrapExitCodes.ActivationFailed;
            }
        }
    }

    private static LaunchMode RecoverMode(
        HostAgentSelectedVersion current,
        HostAgentPendingUpdate? pending,
        HostAgentUpdateJournal journal)
    {
        if (pending is null
            || !journal.TryRead(pending.TransactionId, out HostAgentUpdatePayload? update)
            || update is null)
        {
            return LaunchMode.Current;
        }
        if (update.State == HostAgentUpdateState.AwaitingReadiness
            && string.Equals(current.VersionId, pending.PackageId, StringComparison.Ordinal))
        {
            return LaunchMode.Candidate;
        }
        return update.State == HostAgentUpdateState.Installing
            && string.Equals(update.Diagnostic, "host-agent-update-rolling-back", StringComparison.Ordinal)
            && string.Equals(current.VersionId, pending.PreviousVersionId, StringComparison.Ordinal)
                ? LaunchMode.Rollback
                : LaunchMode.Current;
    }

    private void Write(
        HostAgentPendingUpdate pending,
        HostAgentUpdateState updateState,
        string diagnostic,
        string? activeVersionId)
    {
        journal.Write(new HostAgentUpdatePayload(
            pending.TransactionId,
            pending.PackageId,
            updateState,
            diagnostic,
            pending.SourceCommit,
            pending.PreviousVersionId,
            activeVersionId));
    }

    private enum LaunchMode
    {
        Current,
        Candidate,
        Rollback
    }
}
