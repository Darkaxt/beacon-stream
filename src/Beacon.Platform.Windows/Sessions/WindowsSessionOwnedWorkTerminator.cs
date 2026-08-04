using Beacon.Core.Sessions;

namespace Beacon.Platform.Windows.Sessions;

public sealed class WindowsSessionOwnedWorkTerminator(IWindowsSessionActivityApi activityApi)
    : ISessionOwnedWorkTerminator
{
    public async Task<SessionOwnedWorkTerminationResult> TerminateAsync(
        SessionOwnershipRecord record,
        SessionActivitySnapshot activity,
        CancellationToken cancellationToken)
    {
        int[] processIds = activity.OwnedProcessIds
            .Where(processId => processId > 0 && processId != activityApi.CurrentProcessId)
            .Distinct()
            .Order()
            .ToArray();
        if (processIds.Length == 0)
        {
            return SessionOwnedWorkTerminationResult.Fail(
                $"Owned work for session '{record.Plan.SessionId}' has no safe process identity.");
        }

        var terminated = new List<int>();
        var failed = new List<int>();
        foreach (int processId in processIds)
        {
            if (await activityApi.TerminateProcessAsync(processId, cancellationToken))
            {
                terminated.Add(processId);
            }
            else
            {
                failed.Add(processId);
            }
        }

        return failed.Count == 0
            ? SessionOwnedWorkTerminationResult.Ok(terminated)
            : SessionOwnedWorkTerminationResult.Fail(
                $"Owned process termination failed for process id(s): {string.Join(", ", failed)}.",
                terminated);
    }
}
