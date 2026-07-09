package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.Assert.assertEquals;

public final class StreamConnectionLaunchUriTest {
    @Test
    public void extractReturnsServerConnectionLaunchUri() {
        String uri = StreamConnectionLaunchUri.extract(
            "{\"stream\":{\"connection\":{\"launchUri\":\"moonlight://stream/z-fold-7\"}}}");

        assertEquals("moonlight://stream/z-fold-7", uri);
    }

    @Test
    public void extractReturnsEmptyWhenConnectionIsMissing() {
        assertEquals("", StreamConnectionLaunchUri.extract("{}"));
    }

    @Test
    public void extractReturnsEmptyWhenLaunchUriIsBlank() {
        assertEquals("", StreamConnectionLaunchUri.extract("{\"stream\":{\"connection\":{\"launchUri\":\"\"}}}"));
    }

    @Test
    public void descriptorExtractsProtocolLaunchUriAndEndpoints() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"launchUri\":\"moonlight://stream/z-fold-7\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"},{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

        assertEquals("gamestream", descriptor.protocol());
        assertEquals("moonlight://stream/z-fold-7", descriptor.launchUri());
        assertEquals("rtsp=rtsp://127.0.0.1:48010, audio=udp://127.0.0.1:48000", descriptor.endpointSummary());
        assertEquals(2, descriptor.endpoints().size());
        assertEquals("rtsp", descriptor.endpoints().get(0).role());
        assertEquals("rtsp://127.0.0.1:48010", descriptor.endpoints().get(0).uri());
        assertEquals("", descriptor.missingLaunchUriDiagnostic());
    }

    @Test
    public void descriptorReportsEndpointOnlyConnectionWithoutLaunchUri() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}]}}}");

        assertEquals("gamestream", descriptor.protocol());
        assertEquals("", descriptor.launchUri());
        assertEquals("rtsp=rtsp://127.0.0.1:48010", descriptor.endpointSummary());
        assertEquals(
            "Stream connection did not include a launch URI. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010",
            descriptor.missingLaunchUriDiagnostic());
    }

    @Test
    public void dispatchingLauncherPostsLaunchToDispatcher() {
        RecordingDispatcher dispatcher = new RecordingDispatcher();
        RecordingLauncher inner = new RecordingLauncher();
        DispatchingStreamConnectionLauncher launcher = new DispatchingStreamConnectionLauncher(dispatcher, inner);

        launcher.launch("moonlight://stream/z-fold-7");

        assertEquals("", inner.launchedUri);
        assertEquals(1, dispatcher.pendingCount());

        dispatcher.runPending();

        assertEquals("moonlight://stream/z-fold-7", inner.launchedUri);
    }

    @Test
    public void dispatchingLauncherPreservesLaunchOrder() {
        RecordingDispatcher dispatcher = new RecordingDispatcher();
        RecordingLauncher inner = new RecordingLauncher();
        DispatchingStreamConnectionLauncher launcher = new DispatchingStreamConnectionLauncher(dispatcher, inner);

        launcher.launch("moonlight://one");
        launcher.launch("moonlight://two");
        dispatcher.runPending();

        assertEquals("moonlight://one,moonlight://two", inner.launchedUris());
    }

    private static final class RecordingDispatcher implements MainThreadDispatcher {
        private final List<Runnable> pending = new ArrayList<>();

        @Override
        public void dispatch(Runnable action) {
            pending.add(action);
        }

        int pendingCount() {
            return pending.size();
        }

        void runPending() {
            for (Runnable action : pending) {
                action.run();
            }
            pending.clear();
        }
    }

    private static final class RecordingLauncher implements StreamConnectionLauncher {
        private final List<String> launchedUris = new ArrayList<>();
        String launchedUri = "";

        @Override
        public void launch(String launchUri) {
            launchedUri = launchUri;
            launchedUris.add(launchUri);
        }

        String launchedUris() {
            return String.join(",", launchedUris);
        }
    }
}
