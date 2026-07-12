using System.Reflection;
using Beacon.Core.Input;
using Beacon.Server.Api;

namespace Beacon.Server.Tests;

public sealed class PublicApiCompatibilityTests
{
    [Fact]
    public void ClientInputRequestRetainsExactConstructionGetterAndDeconstruction()
    {
        IReadOnlyList<ClientInputEvent> events =
        [
            new ClientInputEvent("pointer", "move", PointerId: 1, X: 0.5, Y: 0.25)
        ];

        var request = new ClientInputRequest(42, events);
        IReadOnlyList<ClientInputEvent> getter = request.Events;
        (long sequence, IReadOnlyList<ClientInputEvent> deconstructed) = request;

        Assert.Equal(42, request.Sequence);
        Assert.Same(events, getter);
        Assert.Equal(42, sequence);
        Assert.Same(events, deconstructed);
        Assert.NotNull(typeof(ClientInputRequest).GetConstructor(
            [typeof(long), typeof(IReadOnlyList<ClientInputEvent>)]));
        PropertyInfo? eventsProperty = typeof(ClientInputRequest).GetProperty(nameof(ClientInputRequest.Events));
        Assert.NotNull(eventsProperty);
        Assert.Equal(typeof(IReadOnlyList<ClientInputEvent>), eventsProperty.PropertyType);
        MethodInfo? deconstruct = typeof(ClientInputRequest).GetMethod(
            "Deconstruct",
            [
                typeof(long).MakeByRefType(),
                typeof(IReadOnlyList<ClientInputEvent>).MakeByRefType()
            ]);
        Assert.NotNull(deconstruct);
        Assert.True(deconstruct.IsPublic);
        Assert.Equal(typeof(void), deconstruct.ReturnType);
    }
}
