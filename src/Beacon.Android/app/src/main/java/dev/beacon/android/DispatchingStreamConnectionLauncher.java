package dev.beacon.android;

public final class DispatchingStreamConnectionLauncher implements StreamConnectionLauncher {
    private final MainThreadDispatcher dispatcher;
    private final StreamConnectionLauncher inner;

    public DispatchingStreamConnectionLauncher(MainThreadDispatcher dispatcher, StreamConnectionLauncher inner) {
        this.dispatcher = dispatcher;
        this.inner = inner;
    }

    @Override
    public void launch(String launchUri) {
        dispatcher.dispatch(() -> inner.launch(launchUri));
    }
}
