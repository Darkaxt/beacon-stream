using Beacon.Core.Input;
using Beacon.Platform.Windows.Input;

namespace Beacon.Platform.Windows.Tests.Input;

public sealed class WindowsVirtualControllerTests
{
    [Fact]
    public async Task CompleteBatchMapsControlsAndSubmitsOneReport()
    {
        var factory = new FakeXboxControllerFactory();
        await using var api = new WindowsVirtualControllerApi(factory);

        WindowsVirtualControllerResult result = await api.ApplyAsync(
            "session-1",
            [
                new ClientControllerInput(0, 1, 1),
                new ClientControllerInput(0, 12, 1),
                new ClientControllerInput(0, 16, 128),
                new ClientControllerInput(0, 18, -32768),
                new ClientControllerInput(0, 21, 32767),
            ],
            CancellationToken.None);

        Assert.True(result.Success, result.Error);
        FakeXboxController target = Assert.Single(factory.Targets);
        Assert.Equal(1, target.ConnectCount);
        Assert.Equal(
            [
                "button:Up:True",
                "button:A:True",
                "trigger:LeftTrigger:128",
                "axis:LeftThumbX:-32768",
                "axis:RightThumbY:32767",
            ],
            target.Changes);
        Assert.Equal(1, target.SubmitCount);
        Assert.Equal(1, api.ActiveSessionCount);
    }

    [Fact]
    public async Task ReconnectRetainsOneTargetPerSession()
    {
        var factory = new FakeXboxControllerFactory();
        await using var api = new WindowsVirtualControllerApi(factory);

        await api.ApplyAsync(
            "session-1",
            [new ClientControllerInput(0, 12, 1)],
            CancellationToken.None);
        await api.ApplyAsync(
            "session-1",
            [new ClientControllerInput(0, 12, 0)],
            CancellationToken.None);
        await api.ApplyAsync(
            "session-2",
            [new ClientControllerInput(0, 13, 1)],
            CancellationToken.None);

        Assert.Equal(2, factory.Targets.Count);
        Assert.Equal(2, factory.Targets[0].SubmitCount);
        Assert.Equal(1, factory.Targets[1].SubmitCount);
    }

    [Theory]
    [InlineData(1, 12, 1)]
    [InlineData(0, 12, 2)]
    [InlineData(0, 16, 256)]
    [InlineData(0, 18, 32768)]
    [InlineData(0, 22, 0)]
    public async Task InvalidControlIsRejectedBeforeTargetCreation(
        uint controllerIndex,
        uint controlId,
        int value)
    {
        var factory = new FakeXboxControllerFactory();
        await using var api = new WindowsVirtualControllerApi(factory);

        WindowsVirtualControllerResult result = await api.ApplyAsync(
            "session-1",
            [new ClientControllerInput(controllerIndex, controlId, value)],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("controller-input-invalid", result.ResultCode);
        Assert.Empty(factory.Targets);
    }

    [Fact]
    public async Task ReleaseIsSessionScopedAndIdempotent()
    {
        var factory = new FakeXboxControllerFactory();
        await using var api = new WindowsVirtualControllerApi(factory);
        await api.ApplyAsync(
            "session-1",
            [new ClientControllerInput(0, 12, 1)],
            CancellationToken.None);
        FakeXboxController target = Assert.Single(factory.Targets);

        await api.ReleaseSessionAsync("session-1", CancellationToken.None);
        await api.ReleaseSessionAsync("session-1", CancellationToken.None);

        Assert.Equal(1, target.DisconnectCount);
        Assert.Equal(1, target.DisposeCount);
        Assert.Equal(0, api.ActiveSessionCount);
    }

    [Fact]
    public async Task ReleasedSessionRejectsLateInputUntilNewLaunchPreparesIt()
    {
        var factory = new FakeXboxControllerFactory();
        await using var api = new WindowsVirtualControllerApi(factory);
        ClientControllerInput input = new(0, 12, 1);
        await api.ApplyAsync("session-1", [input], CancellationToken.None);
        await api.ReleaseSessionAsync("session-1", CancellationToken.None);

        WindowsVirtualControllerResult late = await api.ApplyAsync(
            "session-1", [input], CancellationToken.None);

        Assert.False(late.Success);
        Assert.Equal("controller-session-ended", late.ResultCode);
        Assert.Single(factory.Targets);
        Assert.Equal(0, api.ActiveSessionCount);

        await api.PrepareSessionAsync("session-1", CancellationToken.None);
        WindowsVirtualControllerResult relaunched = await api.ApplyAsync(
            "session-1", [input], CancellationToken.None);

        Assert.True(relaunched.Success, relaunched.Error);
        Assert.Equal(2, factory.Targets.Count);
        Assert.Equal(1, api.ActiveSessionCount);
    }

    [Fact]
    public async Task DriverCreationFailureReturnsActionableResult()
    {
        var factory = new FakeXboxControllerFactory
        {
            Failure = new InvalidOperationException("ViGEmBus is unavailable."),
        };
        await using var api = new WindowsVirtualControllerApi(factory);

        WindowsVirtualControllerResult result = await api.ApplyAsync(
            "session-1",
            [new ClientControllerInput(0, 12, 1)],
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("vigem-unavailable", result.ResultCode);
        Assert.Contains("ViGEm", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeXboxControllerFactory : IWindowsXboxControllerFactory
    {
        public List<FakeXboxController> Targets { get; } = [];

        public Exception? Failure { get; init; }

        public IWindowsXboxController Create()
        {
            if (Failure is not null) throw Failure;
            var target = new FakeXboxController();
            Targets.Add(target);
            return target;
        }

        public void Dispose()
        {
        }
    }

    private sealed class FakeXboxController : IWindowsXboxController
    {
        public List<string> Changes { get; } = [];

        public int ConnectCount { get; private set; }

        public int SubmitCount { get; private set; }

        public int DisconnectCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Connect() => ConnectCount++;

        public void SetButton(WindowsXboxButton button, bool pressed) =>
            Changes.Add($"button:{button}:{pressed}");

        public void SetAxis(WindowsXboxAxis axis, short value) =>
            Changes.Add($"axis:{axis}:{value}");

        public void SetTrigger(WindowsXboxTrigger trigger, byte value) =>
            Changes.Add($"trigger:{trigger}:{value}");

        public void SubmitReport() => SubmitCount++;

        public void Disconnect() => DisconnectCount++;

        public void Dispose() => DisposeCount++;
    }
}
