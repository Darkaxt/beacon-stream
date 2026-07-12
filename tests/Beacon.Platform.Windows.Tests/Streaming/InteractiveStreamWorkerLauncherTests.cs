using Beacon.Platform.Windows.Streaming;
using Microsoft.Win32.SafeHandles;

namespace Beacon.Platform.Windows.Tests.Streaming;

public sealed class InteractiveStreamWorkerLauncherTests
{
    [Fact]
    public void AssignmentFailureTerminatesAndWaitsForSuspendedProcessBeforeRethrowing()
    {
        var calls = new List<string>();
        var processApi = new RecordingInteractiveProcessApi(calls);
        var launcher = new InteractiveStreamWorkerLauncher(processApi);
        using var processHandle = new SafeFileHandle(new IntPtr(1), ownsHandle: false);
        using var threadHandle = new SafeFileHandle(new IntPtr(2), ownsHandle: false);
        var assignmentFailure = new InvalidOperationException("assignment failed");

        InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(() =>
            launcher.AssignAndResumeSuspendedProcess(
                processHandle,
                threadHandle,
                _ =>
                {
                    calls.Add("assign");
                    throw assignmentFailure;
                }));

        Assert.Same(assignmentFailure, thrown);
        Assert.Equal(["assign", "terminate", "wait"], calls);
    }

    private sealed class RecordingInteractiveProcessApi(List<string> calls) :
        IInteractiveStreamWorkerProcessApi
    {
        public uint ResumeThread(SafeFileHandle threadHandle)
        {
            calls.Add("resume");
            return 1;
        }

        public void TerminateProcess(SafeFileHandle processHandle) => calls.Add("terminate");

        public void WaitForExit(SafeFileHandle processHandle) => calls.Add("wait");
    }
}
