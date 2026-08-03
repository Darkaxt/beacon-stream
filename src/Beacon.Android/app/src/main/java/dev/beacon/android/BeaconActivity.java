package dev.beacon.android;

import android.app.Activity;
import android.content.res.Configuration;
import android.os.Bundle;
import android.view.Display;
import android.view.Gravity;
import android.view.HapticFeedbackConstants;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.view.SurfaceView;
import android.widget.TextView;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class BeaconActivity extends Activity {
    private static final String[] LOCAL_THEME_VALUES = new String[] { "system", "dark", "light" };
    private static final String[] TOUCH_LAYOUT_VALUES = new String[] { "default", "compact", "edge" };
    private static final String[] UI_DENSITY_VALUES = new String[] { "comfortable", "dense", "large" };

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final CountDownLatch workerCleanupComplete = new CountDownLatch(1);
    private final List<BeaconGameCatalog.GameEntry> gameEntries = new ArrayList<>();
    private final BeaconTouchInputMapper touchInputMapper = new BeaconTouchInputMapper();
    private final AndroidGamepadMapper gamepadInputMapper = new AndroidGamepadMapper();
    private final AndroidDeviceCapabilityProbe capabilityProbe = AndroidDeviceCapabilityProbe.system();
    private final AutomaticBenchmarkGate automaticBenchmarkGate = new AutomaticBenchmarkGate();

    private BeaconViewModelSession modelSession;
    private AndroidBenchmarkChangeMonitor benchmarkChangeMonitor;
    private AndroidBenchmarkFingerprintProbe benchmarkFingerprintProbe;
    private AndroidBenchmarkNetworkState benchmarkNetwork =
        AndroidBenchmarkNetworkState.disconnected();
    private boolean automaticBenchmarkEnabled;
    private boolean lifecycleForeground;
    private volatile boolean automaticPresenceEnabled;
    private AndroidDeviceTelemetryProbe telemetryProbe;
    private BeaconLocalSettingsStore localSettingsStore;
    private BeaconLocalSettings localSettings;
    private BeaconLocalSettingsUiState uiState;
    private LinearLayout rootLayout;
    private EditText serverUrl;
    private EditText clientId;
    private EditText publicKeyFingerprint;
    private Spinner touchLayout;
    private Spinner uiDensity;
    private Spinner localTheme;
    private CheckBox multitouchEnabled;
    private CheckBox controllerOverlayEnabled;
    private CheckBox hapticsEnabled;
    private CheckBox wakeLockEnabled;
    private CheckBox decoderDebugOverlayEnabled;
    private EditText gameId;
    private Spinner gameSelector;
    private ArrayAdapter<String> gameAdapter;
    private TextView decoderDebugOverlay;
    private TextView controllerOverlayMarker;
    private View touchSurfaceView;
    private SurfaceView videoSurfaceView;
    private AndroidSurfaceViewProvider videoSurfaceProvider;
    private TextView status;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        localSettingsStore = new BeaconLocalSettingsStore(
            new SharedPreferencesLocalSettingsStorage(getSharedPreferences("beacon", MODE_PRIVATE)));
        localSettings = localSettingsStore.load();
        uiState = BeaconLocalSettingsUiState.from(localSettings, systemDarkTheme());
        telemetryProbe = AndroidDeviceTelemetryProbe.system(this);
        applyWindowFlags(uiState);
        setContentView(createContent());
        modelSession = new BeaconViewModelSession(this::createModel);
        benchmarkFingerprintProbe = new AndroidBenchmarkFingerprintProbe(
            BeaconNetworkIdentityHasher.system(this),
            new AndroidSystemBenchmarkHardwareSource(this, new AndroidMediaCodecCatalog()));
        benchmarkChangeMonitor = new AndroidBenchmarkChangeMonitor(this);
        benchmarkChangeMonitor.start(this::onBenchmarkEnvironmentChanged);
    }

    @Override
    protected void onStart() {
        super.onStart();
        lifecycleForeground = true;
        if (automaticPresenceEnabled) {
            runAction("Reconnect", model -> {
                model.setForegroundDesired(true);
                if (model.onForeground(readCapabilities())) {
                    runOnUiThread(this::enableAutomaticBenchmark);
                }
            });
        }
    }

    @Override
    protected void onStop() {
        lifecycleForeground = false;
        disableAutomaticBenchmark();
        if (automaticPresenceEnabled && modelSession != null) {
            BeaconClientConfig config = readClientConfig();
            executeWithModel(config, model -> {
                model.setForegroundDesired(false);
                try {
                    model.reconcileBackground();
                } catch (IOException ignored) {
                    // Lifecycle departure is best-effort on the current request transport.
                }
            });
        }
        super.onStop();
    }

    @Override
    protected void onDestroy() {
        if (benchmarkChangeMonitor != null) {
            benchmarkChangeMonitor.close();
        }
        if (modelSession != null) {
            BeaconViewModelSession closingSession = modelSession;
            executor.execute(() -> {
                try {
                    closingSession.close();
                } finally {
                    workerCleanupComplete.countDown();
                }
            });
        } else {
            workerCleanupComplete.countDown();
        }
        if (videoSurfaceProvider != null) {
            videoSurfaceProvider.close();
        }

        executor.shutdown();
        super.onDestroy();
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        if (modelSession == null || !isGamepadSource(event.getSource())) {
            return super.dispatchKeyEvent(event);
        }

        boolean pressed;
        if (event.getAction() == KeyEvent.ACTION_DOWN) {
            pressed = true;
        } else if (event.getAction() == KeyEvent.ACTION_UP) {
            pressed = false;
        } else {
            return super.dispatchKeyEvent(event);
        }

        BeaconApiClient.InputBatch batch = gamepadInputMapper.mapButton(
            event.getKeyCode(),
            pressed);
        BeaconViewModel model = currentModelIfPresent();
        if (batch == null || model == null || !model.hasActiveStream()) {
            return super.dispatchKeyEvent(event);
        }
        if (event.getRepeatCount() == 0) {
            sendGamepadInput(batch);
        }
        return true;
    }

    @Override
    public boolean dispatchGenericMotionEvent(MotionEvent event) {
        if (modelSession == null || event.getActionMasked() != MotionEvent.ACTION_MOVE ||
            !isGamepadSource(event.getSource())) {
            return super.dispatchGenericMotionEvent(event);
        }

        BeaconViewModel model = currentModelIfPresent();
        if (model == null || !model.hasActiveStream()) {
            return super.dispatchGenericMotionEvent(event);
        }

        InputDevice device = event.getDevice();
        float leftTrigger = Math.max(
            event.getAxisValue(MotionEvent.AXIS_LTRIGGER),
            event.getAxisValue(MotionEvent.AXIS_BRAKE));
        float rightTrigger = Math.max(
            event.getAxisValue(MotionEvent.AXIS_RTRIGGER),
            event.getAxisValue(MotionEvent.AXIS_GAS));
        float stickFlat = maxFlat(
            device,
            event.getSource(),
            MotionEvent.AXIS_X,
            MotionEvent.AXIS_Y,
            MotionEvent.AXIS_Z,
            MotionEvent.AXIS_RZ);
        float triggerFlat = maxFlat(
            device,
            event.getSource(),
            MotionEvent.AXIS_LTRIGGER,
            MotionEvent.AXIS_BRAKE,
            MotionEvent.AXIS_RTRIGGER,
            MotionEvent.AXIS_GAS);
        sendGamepadInput(gamepadInputMapper.mapAxes(
            event.getAxisValue(MotionEvent.AXIS_X),
            event.getAxisValue(MotionEvent.AXIS_Y),
            event.getAxisValue(MotionEvent.AXIS_Z),
            event.getAxisValue(MotionEvent.AXIS_RZ),
            leftTrigger,
            rightTrigger,
            stickFlat,
            triggerFlat));
        return true;
    }

    private View createContent() {
        ScrollView scrollView = new ScrollView(this);
        LinearLayout root = new LinearLayout(this);
        rootLayout = root;
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(
            uiState.contentPaddingPx(),
            uiState.contentPaddingPx(),
            uiState.contentPaddingPx(),
            uiState.contentPaddingPx());
        root.setBackgroundColor(uiState.backgroundColor());
        scrollView.addView(root);

        TextView title = text("Beacon", uiState.titleTextSizeSp(), true);
        root.addView(title);
        root.addView(touchSurface());

        addLocalSettingsControls(root);

        serverUrl = input("Server URL", "https://10.0.2.2:5001");
        clientId = input("Client ID", "z-fold-7");
        publicKeyFingerprint = input("Server public-key fingerprint", "");
        gameId = input("Game ID", "steam-shortcut:3767414131");
        gameSelector = new Spinner(this);
        gameAdapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, new ArrayList<>());
        gameAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        gameSelector.setAdapter(gameAdapter);
        root.addView(serverUrl);
        root.addView(clientId);
        root.addView(publicKeyFingerprint);
        root.addView(gameId);
        root.addView(gameSelector);
        decoderDebugOverlay = text("", 12, false);
        root.addView(decoderDebugOverlay);
        updateDecoderDebugOverlay();
        controllerOverlayMarker = text("Controller overlay enabled", uiState.bodyTextSizeSp(), false);
        root.addView(controllerOverlayMarker);
        updateControllerOverlay();

        root.addView(localButton("Connect", this::connect));
        root.addView(button("Load Games", model -> {
            model.loadGames();
            setGameEntries(model.latestGameEntries());
        }));
        root.addView(localButton("Run Benchmark", () -> scheduleBenchmark("manual", benchmarkNetwork)));
        root.addView(button("Plan", model -> model.preflightAndPlan(
            readCapabilities(),
            readTelemetry(),
            readGame())));
        root.addView(button("Launch", model -> {
            BeaconApiClient.ClientCapabilities capabilities = readCapabilities();
            model.preflightBenchmarkAndLaunch(
                capabilities,
                readTelemetry(),
                benchmarkFingerprintProbe.create(
                    "sessionPreflight",
                    serverUrl.getText().toString(),
                    capabilities,
                    benchmarkNetwork),
                AndroidDeviceBenchmarkRunner.system(this),
                readGame());
        }));
        root.addView(button("Send Pointer", model -> model.sendInput(BeaconApiClient.InputBatch.pointerTap(1, 0.5, 0.5))));
        root.addView(button("Send Escape", model -> model.sendInput(BeaconApiClient.InputBatch.keyboardPress(2, "Escape", "Escape"))));
        root.addView(button("Stop Stream", model -> model.stopStream()));
        root.addView(button("Disconnect", model -> model.disconnect()));
        root.addView(button("Reconnect", model -> model.reconnect()));
        root.addView(button("Quit", model -> leaveAndQuit(model)));
        root.addView(button("Emergency Restore", model -> model.emergencyRestore()));

        status = text("Idle", uiState.bodyTextSizeSp(), false);
        status.setGravity(Gravity.START);
        root.addView(status);

        return scrollView;
    }

    private void addLocalSettingsControls(LinearLayout root) {
        touchLayout = spinner(TOUCH_LAYOUT_VALUES, valueIndex(TOUCH_LAYOUT_VALUES, localSettings.touchLayout));
        uiDensity = spinner(UI_DENSITY_VALUES, valueIndex(UI_DENSITY_VALUES, localSettings.uiDensity));
        localTheme = spinner(LOCAL_THEME_VALUES, valueIndex(LOCAL_THEME_VALUES, localSettings.localTheme));
        multitouchEnabled = checkbox("Enable multitouch", localSettings.multitouchEnabled);
        controllerOverlayEnabled = checkbox("Show controller overlay", localSettings.controllerOverlayEnabled);
        hapticsEnabled = checkbox("Enable haptics", localSettings.hapticsEnabled);
        wakeLockEnabled = checkbox("Keep screen awake", localSettings.wakeLockEnabled);
        decoderDebugOverlayEnabled = checkbox("Show decoder debug overlay", localSettings.decoderDebugOverlayEnabled);
        root.addView(text("Touch layout", uiState.bodyTextSizeSp(), false));
        root.addView(touchLayout);
        root.addView(text("UI density", uiState.bodyTextSizeSp(), false));
        root.addView(uiDensity);
        root.addView(text("Theme", uiState.bodyTextSizeSp(), false));
        root.addView(localTheme);
        root.addView(multitouchEnabled);
        root.addView(controllerOverlayEnabled);
        root.addView(hapticsEnabled);
        root.addView(wakeLockEnabled);
        root.addView(decoderDebugOverlayEnabled);
        root.addView(localButton("Save Local Settings", this::saveLocalSettings));
    }

    private View touchSurface() {
        FrameLayout surface = new FrameLayout(this);
        touchSurfaceView = surface;
        int surfaceHeight = uiState.touchSurfaceMinHeightPx();
        surface.setMinimumHeight(surfaceHeight);
        surface.setLayoutParams(new LinearLayout.LayoutParams(
            LinearLayout.LayoutParams.MATCH_PARENT,
            surfaceHeight));
        surface.setBackgroundColor(uiState.surfaceColor());
        videoSurfaceView = new SurfaceView(this);
        surface.addView(videoSurfaceView, new FrameLayout.LayoutParams(
            FrameLayout.LayoutParams.MATCH_PARENT,
            FrameLayout.LayoutParams.MATCH_PARENT));
        videoSurfaceProvider = new AndroidSurfaceViewProvider(videoSurfaceView);
        surface.setOnTouchListener((view, event) -> {
            BeaconApiClient.InputBatch batch = mapTouchEvent(event, view.getWidth(), view.getHeight());
            if (batch == null) {
                return false;
            }

            if (uiState.hapticsEnabled() && pointerDown(event)) {
                view.performHapticFeedback(HapticFeedbackConstants.VIRTUAL_KEY);
            }

            runAction("Touch Input", model -> model.sendInput(batch));
            return true;
        });
        return surface;
    }

    private BeaconApiClient.InputBatch mapTouchEvent(MotionEvent event, int surfaceWidth, int surfaceHeight) {
        String action = pointerAction(event);
        if (action.isEmpty()) {
            return null;
        }

        if (!uiState.multitouchEnabled() && event.getPointerCount() > 1) {
            int pointerIndex = Math.min(event.getActionIndex(), event.getPointerCount() - 1);
            return touchInputMapper.map(
                action,
                event.getPointerId(pointerIndex),
                event.getX(pointerIndex),
                event.getY(pointerIndex),
                surfaceWidth,
                surfaceHeight);
        }

        if (event.getActionMasked() == MotionEvent.ACTION_MOVE ||
            event.getActionMasked() == MotionEvent.ACTION_CANCEL) {
            return mapAllPointers(action, event, surfaceWidth, surfaceHeight);
        }

        int pointerIndex = event.getActionIndex();
        return touchInputMapper.map(
            action,
            event.getPointerId(pointerIndex),
            event.getX(pointerIndex),
            event.getY(pointerIndex),
            surfaceWidth,
            surfaceHeight);
    }

    private BeaconApiClient.InputBatch mapAllPointers(
        String action,
        MotionEvent event,
        int surfaceWidth,
        int surfaceHeight) {
        int pointerCount = event.getPointerCount();
        int[] pointerIds = new int[pointerCount];
        float[] xs = new float[pointerCount];
        float[] ys = new float[pointerCount];
        for (int i = 0; i < pointerCount; i++) {
            pointerIds[i] = event.getPointerId(i);
            xs[i] = event.getX(i);
            ys[i] = event.getY(i);
        }

        return touchInputMapper.mapPointers(action, pointerIds, xs, ys, surfaceWidth, surfaceHeight);
    }

    private EditText input(String hint, String value) {
        EditText editText = new EditText(this);
        editText.setHint(hint);
        editText.setText(value);
        editText.setSingleLine(true);
        editText.setTextColor(uiState.textColor());
        editText.setHintTextColor(uiState.hintColor());
        return editText;
    }

    private TextView text(String value, int sizeSp, boolean title) {
        TextView textView = new TextView(this);
        textView.setText(value);
        textView.setTextSize(sizeSp);
        textView.setTextColor(uiState.textColor());
        textView.setPadding(0, title ? 0 : 12, 0, 12);
        return textView;
    }

    private CheckBox checkbox(String label, boolean checked) {
        CheckBox checkBox = new CheckBox(this);
        checkBox.setText(label);
        checkBox.setTextColor(uiState.textColor());
        checkBox.setChecked(checked);
        return checkBox;
    }

    private Spinner spinner(String[] values, int selectedIndex) {
        Spinner spinner = new Spinner(this);
        ArrayAdapter<String> adapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, values);
        adapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        spinner.setAdapter(adapter);
        spinner.setSelection(selectedIndex);
        return spinner;
    }

    private Button button(String label, BeaconAction action) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setOnClickListener(view -> runAction(label, action));
        return button;
    }

    private Button localButton(String label, Runnable action) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setOnClickListener(view -> action.run());
        return button;
    }

    private static String pointerAction(MotionEvent event) {
        switch (event.getActionMasked()) {
            case MotionEvent.ACTION_DOWN:
            case MotionEvent.ACTION_POINTER_DOWN:
                return "down";
            case MotionEvent.ACTION_MOVE:
                return "move";
            case MotionEvent.ACTION_UP:
            case MotionEvent.ACTION_POINTER_UP:
            case MotionEvent.ACTION_CANCEL:
                return "up";
            default:
                return "";
        }
    }

    private static boolean pointerDown(MotionEvent event) {
        return event.getActionMasked() == MotionEvent.ACTION_DOWN ||
            event.getActionMasked() == MotionEvent.ACTION_POINTER_DOWN;
    }

    private void runAction(String label, BeaconAction action) {
        status.setText(label + "...");
        BeaconClientConfig config;
        try {
            config = readClientConfig();
        } catch (RuntimeException failure) {
            setStatus(label + " failed: " + failure.getMessage());
            return;
        }
        executeWithModel(config, model -> {
            try {
                action.run(model);
                String error = model.latestError().isEmpty() ? "" : "\nError: " + model.latestError();
                setStatus(model.status() + "\nGames: " + model.latestGames() +
                    "\nPlan: " + model.latestPlan() + "\nStream: " + model.latestStream() +
                    error);
            } catch (IOException | RuntimeException ex) {
                setStatus(label + " failed: " + ex.getMessage());
            }
        });
    }

    private void connect() {
        disableAutomaticBenchmark();
        automaticPresenceEnabled = true;
        runAction("Connect", current -> {
            current.setForegroundDesired(true);
            automaticPresenceEnabled = current.onForeground(readCapabilities());
            if (automaticPresenceEnabled) {
                runOnUiThread(this::enableAutomaticBenchmark);
            } else {
                current.setForegroundDesired(false);
            }
        });
    }

    private void onBenchmarkEnvironmentChanged(AndroidBenchmarkNetworkState network) {
        runOnUiThread(() -> {
            benchmarkNetwork = network;
            if (automaticBenchmarkEnabled) scheduleBenchmark("automatic", network);
        });
    }

    private void enableAutomaticBenchmark() {
        if (!lifecycleForeground) return;
        automaticBenchmarkEnabled = true;
        automaticBenchmarkGate.enable();
        scheduleBenchmark("automatic", benchmarkNetwork);
    }

    private void disableAutomaticBenchmark() {
        automaticBenchmarkEnabled = false;
        automaticBenchmarkGate.disable();
    }

    private void scheduleBenchmark(
        String trigger,
        AndroidBenchmarkNetworkState network) {
        BeaconApiClient.ClientCapabilities capabilities;
        BeaconBenchmarkPrepareRequest request;
        BeaconClientConfig config;
        try {
            config = readClientConfig();
            capabilities = readCapabilities();
            request = benchmarkFingerprintProbe.create(
                trigger,
                config.serverUrl(),
                capabilities,
                network);
        } catch (RuntimeException failure) {
            setStatus("Benchmark facts failed: " + failure.getMessage());
            return;
        }

        String fingerprint = request.fingerprints().toString();
        boolean automatic = "automatic".equals(trigger);
        if (automatic && !automaticBenchmarkGate.begin(fingerprint)) return;
        status.setText((automatic ? "Automatic benchmark" : "Manual benchmark") + "...");
        executeWithModel(config, model -> {
            boolean started = false;
            try {
                model.refresh();
                model.reportCapabilities(capabilities);
                model.cancelBenchmark();
                BeaconApiClient.BeaconResult result = model.runBenchmarkAndWait(
                    request,
                    AndroidDeviceBenchmarkRunner.system(this));
                started = result.isSuccess();
                String error = started ? "" : "\nError: " + model.latestError();
                setStatus(model.status() + error);
            } catch (IOException | RuntimeException failure) {
                setStatus("Benchmark failed: " + failure.getMessage());
            } finally {
                if (automatic) automaticBenchmarkGate.finish(fingerprint, started);
            }
        });
    }

    private BeaconClientConfig readClientConfig() {
        return new BeaconClientConfig(
            serverUrl.getText().toString(),
            clientId.getText().toString(),
            publicKeyFingerprint.getText().toString());
    }

    private BeaconViewModel currentModelIfPresent() {
        try {
            return modelSession.current(readClientConfig());
        } catch (RuntimeException ignored) {
            return null;
        }
    }

    private void executeWithModel(
        BeaconClientConfig config,
        BeaconViewModelSession.ModelAction action) {
        if (!modelSession.isCurrent(config.clientId(), config.serverUrl())) {
            disableAutomaticBenchmark();
        }
        modelSession.execute(executor, config, action);
    }

    private void sendGamepadInput(BeaconApiClient.InputBatch batch) {
        BeaconClientConfig config;
        try {
            config = readClientConfig();
        } catch (RuntimeException failure) {
            setStatus("Controller input failed: " + failure.getMessage());
            return;
        }
        executeWithModel(config, model -> {
            try {
                if (model.hasActiveStream()) {
                    model.sendInput(batch);
                }
            } catch (RuntimeException failure) {
                setStatus("Controller input failed: " + failure.getMessage());
            }
        });
    }

    private static boolean isGamepadSource(int source) {
        return (source & InputDevice.SOURCE_GAMEPAD) == InputDevice.SOURCE_GAMEPAD ||
            (source & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK;
    }

    private static float maxFlat(
        InputDevice device,
        int source,
        int... axes) {
        float flat = 0f;
        if (device == null) return flat;
        for (int axis : axes) {
            InputDevice.MotionRange range = device.getMotionRange(axis, source);
            if (range != null) flat = Math.max(flat, range.getFlat());
        }
        return flat;
    }

    private void setStatus(String value) {
        runOnUiThread(() -> status.setText(value));
    }

    private void saveLocalSettings() {
        localSettings = BeaconLocalSettingsForm.update(
            localSettings,
            selectedValue(touchLayout, "default"),
            multitouchEnabled.isChecked(),
            controllerOverlayEnabled.isChecked(),
            hapticsEnabled.isChecked(),
            selectedValue(uiDensity, "comfortable"),
            selectedLocalTheme(),
            wakeLockEnabled.isChecked(),
            decoderDebugOverlayEnabled.isChecked());
        localSettingsStore.save(localSettings);
        uiState = BeaconLocalSettingsUiState.from(localSettings, systemDarkTheme());
        applyWindowFlags(uiState);
        applyTheme(rootLayout);
        updateDecoderDebugOverlay();
        updateControllerOverlay();
        status.setText("Local settings saved");
    }

    private void applyWindowFlags(BeaconLocalSettingsUiState state) {
        if (state.keepScreenAwake()) {
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        } else {
            getWindow().clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        }
    }

    private void applyTheme(View view) {
        if (view == null) {
            return;
        }

        if (view == rootLayout) {
            view.setBackgroundColor(uiState.backgroundColor());
            view.setPadding(
                uiState.contentPaddingPx(),
                uiState.contentPaddingPx(),
                uiState.contentPaddingPx(),
                uiState.contentPaddingPx());
        }

        if (view == touchSurfaceView) {
            view.setBackgroundColor(uiState.surfaceColor());
            int surfaceHeight = uiState.touchSurfaceMinHeightPx();
            view.setMinimumHeight(surfaceHeight);
            ViewGroup.LayoutParams layout = view.getLayoutParams();
            if (layout != null && layout.height != surfaceHeight) {
                layout.height = surfaceHeight;
                view.setLayoutParams(layout);
            }
        }

        if (view instanceof TextView textView) {
            textView.setTextColor(uiState.textColor());
        }

        if (view instanceof EditText editText) {
            editText.setHintTextColor(uiState.hintColor());
        }

        if (view instanceof LinearLayout linearLayout) {
            for (int i = 0; i < linearLayout.getChildCount(); i++) {
                applyTheme(linearLayout.getChildAt(i));
            }
        }
    }

    private void updateDecoderDebugOverlay() {
        if (decoderDebugOverlay == null) {
            return;
        }

        decoderDebugOverlay.setVisibility(uiState.debugOverlayVisible() ? View.VISIBLE : View.GONE);
        BeaconApiClient.ClientTelemetry telemetry = readTelemetry();
        decoderDebugOverlay.setText(
            "Battery " + (telemetry.batteryPercent == null ? "unknown" : telemetry.batteryPercent + "%") +
                " | network " + (telemetry.wifiBand.isEmpty() ? "unknown" : telemetry.wifiBand) +
                " | thermal " + (telemetry.thermalState.isEmpty() ? "unknown" : telemetry.thermalState));
    }

    private void updateControllerOverlay() {
        if (controllerOverlayMarker == null) {
            return;
        }

        controllerOverlayMarker.setVisibility(uiState.controllerOverlayEnabled() ? View.VISIBLE : View.GONE);
    }

    private void setGameEntries(List<BeaconGameCatalog.GameEntry> entries) {
        runOnUiThread(() -> {
            gameEntries.clear();
            gameEntries.addAll(entries);
            gameAdapter.clear();
            for (BeaconGameCatalog.GameEntry entry : gameEntries) {
                gameAdapter.add(entry.displayLabel());
            }

            gameAdapter.notifyDataSetChanged();
            if (!gameEntries.isEmpty()) {
                gameId.setText(gameEntries.get(0).id());
            }
        });
    }

    private BeaconViewModel createModel(BeaconClientConfig config) {
        return createModel(config, new BeaconApiClient(this, config));
    }

    private BeaconViewModel createModel(
        BeaconClientConfig config,
        BeaconViewModel.BeaconService service) {
        return new BeaconViewModel(
            config.clientId(),
            config.serverUrl(),
            service,
            failureObserver -> new BeaconVideoSession(
                videoSurfaceProvider,
                failureObserver),
            BeaconAudioSession::new);
    }

    BeaconViewModel createOwnedModelForInstrumentation(
        BeaconViewModel.BeaconService service) {
        BeaconClientConfig config = new BeaconClientConfig(
            "in-memory://instrumentation",
            "instrumentation-client");
        modelSession = new BeaconViewModelSession(
            fixtureConfig -> createModel(fixtureConfig, service));
        return modelSession.get(config);
    }

    boolean workerExecutorShutdown() {
        return executor.isShutdown();
    }

    void awaitWorkerCleanupForInstrumentation() throws InterruptedException {
        workerCleanupComplete.await();
    }

    AndroidSurfaceViewProvider videoSurfaceProviderForInstrumentation() {
        return videoSurfaceProvider;
    }

    SurfaceView videoSurfaceViewForInstrumentation() {
        return videoSurfaceView;
    }

    private BeaconApiClient.GameSelection readGame() {
        int index = gameSelector == null ? -1 : gameSelector.getSelectedItemPosition();
        if (index >= 0 && index < gameEntries.size()) {
            return BeaconApiClient.GameSelection.byGameId(gameEntries.get(index).id());
        }

        return BeaconApiClient.GameSelection.byGameId(textValue(gameId));
    }

    private BeaconApiClient.ClientCapabilities readCapabilities() {
        Display display = getWindowManager().getDefaultDisplay();
        if (display == null) {
            throw new IllegalStateException("Android display facts are unavailable.");
        }
        Display.Mode currentMode = display.getMode();
        List<BeaconApiClient.ClientDisplayMode> supportedModes = new ArrayList<>();
        for (Display.Mode mode : display.getSupportedModes()) {
            supportedModes.add(toClientDisplayMode(mode));
        }
        return capabilityProbe.read(
            toClientDisplayMode(currentMode),
            supportedModes,
            screenHdr10Supported());
    }

    private static BeaconApiClient.ClientDisplayMode toClientDisplayMode(Display.Mode mode) {
        return new BeaconApiClient.ClientDisplayMode(
            mode.getPhysicalWidth(),
            mode.getPhysicalHeight(),
            Math.max(1, Math.round(mode.getRefreshRate())));
    }

    private BeaconApiClient.ClientTelemetry readTelemetry() {
        if (telemetryProbe == null) {
            telemetryProbe = AndroidDeviceTelemetryProbe.system(this);
        }

        return telemetryProbe.read();
    }

    private void leaveAndQuit(BeaconViewModel model) throws IOException {
        automaticPresenceEnabled = false;
        disableAutomaticBenchmark();
        model.setForegroundDesired(false);
        try {
            model.reconcileBackground();
        } catch (IOException ignored) {
            // Presence departure is best-effort; explicit quit still has to run.
        }
        model.quit(new BeaconApiClient.QuitState(false));
    }

    @SuppressWarnings("deprecation")
    private boolean screenHdr10Supported() {
        Display display = getWindowManager().getDefaultDisplay();
        if (display == null) {
            return false;
        }

        Display.HdrCapabilities hdrCapabilities = display.getHdrCapabilities();
        if (hdrCapabilities == null) {
            return false;
        }

        for (int type : hdrCapabilities.getSupportedHdrTypes()) {
            if (type == Display.HdrCapabilities.HDR_TYPE_HDR10) {
                return true;
            }
        }

        return false;
    }

    private String selectedLocalTheme() {
        return selectedValue(localTheme, "system");
    }

    private String selectedValue(Spinner spinner, String fallback) {
        Object selected = spinner.getSelectedItem();
        return selected == null ? fallback : selected.toString();
    }

    private int valueIndex(String[] values, String value) {
        for (int i = 0; i < values.length; i++) {
            if (values[i].equalsIgnoreCase(value)) {
                return i;
            }
        }

        return 0;
    }

    private boolean systemDarkTheme() {
        int nightMode = getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        return nightMode == Configuration.UI_MODE_NIGHT_YES;
    }

    private String textValue(EditText editText) {
        return editText.getText().toString().trim();
    }

    private interface BeaconAction {
        void run(BeaconViewModel model) throws IOException;
    }
}
