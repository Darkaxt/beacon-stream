using Beacon.Core.Input;

namespace Beacon.Core.Tests.Input;

public sealed class ClientInputEventTests
{
    [Fact]
    public void StreamFactoriesPreserveExactProtocolNeutralValues()
    {
        ClientInputEvent pointer = ClientInputEvent.StreamPointer(
            ClientPointerAction.Scroll, -41, 73, -120, 2);
        ClientInputEvent keyboard = ClientInputEvent.StreamKeyboard(0xE04D, pressed: true);
        ClientInputEvent controller = ClientInputEvent.StreamController(3, 19, -32768);
        ClientInputEvent touch = ClientInputEvent.StreamTouch(
            7, ClientTouchAction.Move, 11, 13, 17, 19, 23);

        Assert.Equal(new ClientPointerInput(ClientPointerAction.Scroll, -41, 73, -120, 2), pointer.Pointer);
        Assert.Equal(new ClientKeyboardInput(0xE04D, true), keyboard.Keyboard);
        Assert.Equal(new ClientControllerInput(3, 19, -32768), controller.Controller);
        Assert.Equal(
            new ClientTouchInput(7, ClientTouchAction.Move, 11, 13, 17, 19, 23),
            touch.Touch);
    }

    [Fact]
    public void DiagnosticRenderingRedactsLegacyAndStreamPayloadValues()
    {
        const string canary = "INPUT-CANARY-9f32";
        var legacy = new ClientInputEvent("keyboard", "press", Key: canary, Code: canary);
        ClientInputEvent stream = ClientInputEvent.StreamTouch(
            938475, ClientTouchAction.Down, 123456, 654321, 777777, 888888, 999999);

        string rendered = $"{legacy}|{stream}";

        Assert.DoesNotContain(canary, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("938475", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("123456", rendered, StringComparison.Ordinal);
        Assert.Contains("redacted", rendered, StringComparison.OrdinalIgnoreCase);
    }
}
