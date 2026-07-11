using System.Reflection;
using Beacon.Platform.Windows.Input;
using Beacon.Platform.Windows.Streaming;
using Beacon.StreamWorker.Contracts.Worker.V1;

namespace Beacon.Platform.Windows.Tests;

public sealed class PublicApiCompatibilityTests
{
    [Fact]
    public void StreamWorkerHostRetainsExactOriginalMembers()
    {
        Type contract = typeof(IStreamWorkerHost);

        Assert.Equal(
            ["IsReady", "WorkerInstanceId"],
            contract.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.NotNull(contract.GetMethod(
            nameof(IStreamWorkerHost.EnsureReadyAsync),
            [typeof(CancellationToken)]));
        Assert.NotNull(contract.GetMethod(
            nameof(IStreamWorkerHost.SendAsync),
            [typeof(WorkerIpcEnvelope), typeof(CancellationToken)]));
        Assert.NotNull(contract.GetMethod(
            nameof(IStreamWorkerHost.ShutdownAsync),
            [typeof(CancellationToken)]));
        Assert.Equal(
            3,
            contract.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Count(method => !method.IsSpecialName));
    }

    [Fact]
    public void OriginalPublicConstructorsRemainAvailable()
    {
        Assert.NotNull(typeof(WindowsInputCommand).GetConstructor(
        [
            typeof(WindowsInputCommandKind),
            typeof(int?),
            typeof(int?),
            typeof(string),
            typeof(bool?),
            typeof(string),
            typeof(string)
        ]));
        Assert.NotNull(typeof(StreamWorkerNamedPipeClient).GetConstructor(
        [
            typeof(Stream),
            typeof(Task<int>),
            typeof(uint)
        ]));
        Assert.NotNull(typeof(StreamWorkerStreamingBackend).GetConstructor(
        [
            typeof(IStreamWorkerHost)
        ]));

        var command = new WindowsInputCommand(
            Kind: WindowsInputCommandKind.KeyboardKey,
            X: null,
            Y: null,
            Button: null,
            Pressed: true,
            Key: "w",
            Code: "KeyW");
        Assert.Equal("KeyW", command.Code);
        Assert.Null(command.WheelDelta);
        Assert.Null(command.ScanCode);
    }

    [Fact]
    public void WindowsInputCommandRetainsExactSevenOutputDeconstruct()
    {
        MethodInfo? deconstruct = typeof(WindowsInputCommand).GetMethod(
            "Deconstruct",
            [
                typeof(WindowsInputCommandKind).MakeByRefType(),
                typeof(int?).MakeByRefType(),
                typeof(int?).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(bool?).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType()
            ]);

        Assert.NotNull(deconstruct);
        Assert.True(deconstruct.IsPublic);
        Assert.Equal(typeof(void), deconstruct.ReturnType);
    }
}
