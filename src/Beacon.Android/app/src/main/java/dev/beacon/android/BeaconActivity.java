package dev.beacon.android;

import android.app.Activity;
import android.content.res.Configuration;
import android.os.Bundle;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.ArrayAdapter;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.Spinner;
import android.widget.TextView;

import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class BeaconActivity extends Activity {
    private static final String[] LOCAL_THEME_VALUES = new String[] { "system", "dark", "light" };

    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final List<BeaconGameCatalog.GameEntry> gameEntries = new ArrayList<>();
    private final BeaconTouchInputMapper touchInputMapper = new BeaconTouchInputMapper();

    private BeaconLocalSettingsStore localSettingsStore;
    private BeaconLocalSettings localSettings;
    private BeaconLocalSettingsUiState uiState;
    private LinearLayout rootLayout;
    private EditText serverUrl;
    private EditText clientId;
    private Spinner localTheme;
    private CheckBox wakeLockEnabled;
    private CheckBox decoderDebugOverlayEnabled;
    private EditText width;
    private EditText height;
    private EditText refreshHz;
    private EditText hdrPreference;
    private EditText codecPreference;
    private EditText qualityMode;
    private EditText bitrateCap;
    private EditText audioMode;
    private EditText gameId;
    private Spinner gameSelector;
    private ArrayAdapter<String> gameAdapter;
    private EditText rttMs;
    private EditText packetLossPercent;
    private EditText decoderLoadPercent;
    private EditText estimatedBandwidthMbps;
    private EditText wifiBand;
    private EditText batteryPercent;
    private EditText thermalState;
    private TextView decoderDebugOverlay;
    private TextView touchSurfaceView;
    private TextView status;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        localSettingsStore = new BeaconLocalSettingsStore(
            new SharedPreferencesLocalSettingsStorage(getSharedPreferences("beacon", MODE_PRIVATE)));
        localSettings = localSettingsStore.load();
        uiState = BeaconLocalSettingsUiState.from(localSettings, systemDarkTheme());
        applyWindowFlags(uiState);
        setContentView(createContent());
    }

    @Override
    protected void onDestroy() {
        executor.shutdown();
        super.onDestroy();
    }

    private View createContent() {
        ScrollView scrollView = new ScrollView(this);
        LinearLayout root = new LinearLayout(this);
        rootLayout = root;
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(28, 28, 28, 28);
        root.setBackgroundColor(uiState.backgroundColor());
        scrollView.addView(root);

        TextView title = text("Beacon", 28, true);
        root.addView(title);

        addLocalSettingsControls(root);

        serverUrl = input("Server URL", "http://10.0.2.2:5000");
        clientId = input("Client ID", "z-fold-7");
        width = input("Preferred width", "2560");
        height = input("Preferred height", "1600");
        refreshHz = input("Preferred refresh Hz", "120");
        hdrPreference = input("HDR preference", "prefer");
        codecPreference = input("Codec preference", "auto");
        qualityMode = input("Quality mode", "auto");
        bitrateCap = input("Bitrate cap Mbps", "");
        audioMode = input("Audio mode", "stereo");
        gameId = input("Game ID", "steam-shortcut:3767414131");
        gameSelector = new Spinner(this);
        gameAdapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, new ArrayList<>());
        gameAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        gameSelector.setAdapter(gameAdapter);
        rttMs = input("RTT ms", "8");
        packetLossPercent = input("Packet loss percent", "0");
        decoderLoadPercent = input("Decoder load percent", "20");
        estimatedBandwidthMbps = input("Estimated bandwidth Mbps", "120");
        wifiBand = input("Wi-Fi band", "wifi-7");
        batteryPercent = input("Battery percent", "80");
        thermalState = input("Thermal state", "nominal");

        root.addView(serverUrl);
        root.addView(clientId);
        root.addView(width);
        root.addView(height);
        root.addView(refreshHz);
        root.addView(hdrPreference);
        root.addView(codecPreference);
        root.addView(qualityMode);
        root.addView(bitrateCap);
        root.addView(audioMode);
        root.addView(gameId);
        root.addView(gameSelector);
        root.addView(rttMs);
        root.addView(packetLossPercent);
        root.addView(decoderLoadPercent);
        root.addView(estimatedBandwidthMbps);
        root.addView(wifiBand);
        root.addView(batteryPercent);
        root.addView(thermalState);
        decoderDebugOverlay = text("", 12, false);
        root.addView(decoderDebugOverlay);
        updateDecoderDebugOverlay();

        root.addView(button("Hello / Refresh", model -> model.refresh()));
        root.addView(button("Load Games", model -> {
            model.loadGames();
            setGameEntries(model.latestGameEntries());
        }));
        root.addView(button("Patch Profile", model -> model.patchProfile(readPatch())));
        root.addView(button("Report Capabilities", model -> model.reportCapabilities(readCapabilities())));
        root.addView(button("Report Telemetry", model -> model.reportTelemetry(readTelemetry())));
        root.addView(button("Beacon Active", model -> model.beacon(true)));
        root.addView(button("Beacon Inactive", model -> model.beacon(false)));
        root.addView(button("Plan", model -> model.preflightAndPlan(
            readPatch(),
            readCapabilities(),
            readTelemetry(),
            readGame())));
        root.addView(button("Launch", model -> model.preflightAndLaunch(
            readPatch(),
            readCapabilities(),
            readTelemetry(),
            readGame())));
        root.addView(touchSurface());
        root.addView(button("Send Pointer", model -> model.sendInput(BeaconApiClient.InputBatch.pointerTap(1, 0.5, 0.5))));
        root.addView(button("Send Escape", model -> model.sendInput(BeaconApiClient.InputBatch.keyboardPress(2, "Escape", "Escape"))));
        root.addView(button("Stop Stream", model -> model.stopStream()));
        root.addView(button("Disconnect", model -> model.disconnect()));
        root.addView(button("Quit", model -> model.quit(new BeaconApiClient.QuitState(false))));
        root.addView(button("Emergency Restore", model -> model.emergencyRestore()));

        status = text("Idle", 14, false);
        status.setGravity(Gravity.START);
        root.addView(status);

        return scrollView;
    }

    private void addLocalSettingsControls(LinearLayout root) {
        localTheme = new Spinner(this);
        ArrayAdapter<String> themeAdapter = new ArrayAdapter<>(this, android.R.layout.simple_spinner_item, LOCAL_THEME_VALUES);
        themeAdapter.setDropDownViewResource(android.R.layout.simple_spinner_dropdown_item);
        localTheme.setAdapter(themeAdapter);
        localTheme.setSelection(localThemeIndex(localSettings.localTheme));
        wakeLockEnabled = checkbox("Keep screen awake", localSettings.wakeLockEnabled);
        decoderDebugOverlayEnabled = checkbox("Show decoder debug overlay", localSettings.decoderDebugOverlayEnabled);
        root.addView(localTheme);
        root.addView(wakeLockEnabled);
        root.addView(decoderDebugOverlayEnabled);
        root.addView(localButton("Save Local Settings", this::saveLocalSettings));
    }

    private View touchSurface() {
        TextView surface = text("Touch input surface", 18, false);
        touchSurfaceView = surface;
        surface.setGravity(Gravity.CENTER);
        surface.setMinHeight(360);
        surface.setBackgroundColor(uiState.surfaceColor());
        surface.setOnTouchListener((view, event) -> {
            BeaconApiClient.InputBatch batch = mapTouchEvent(event, view.getWidth(), view.getHeight());
            if (batch == null) {
                return false;
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

    private void runAction(String label, BeaconAction action) {
        status.setText(label + "...");
        BeaconViewModel model = createModel();
        executor.execute(() -> {
            try {
                action.run(model);
                String error = model.latestError().isEmpty() ? "" : "\nError: " + model.latestError();
                setStatus(model.status() + "\nGames: " + model.latestGames() + "\nPlan: " + model.latestPlan() + "\nStream: " + model.latestStream() + error);
            } catch (IOException | RuntimeException ex) {
                setStatus(label + " failed: " + ex.getMessage());
            }
        });
    }

    private void setStatus(String value) {
        runOnUiThread(() -> status.setText(value));
    }

    private void saveLocalSettings() {
        localSettings = BeaconLocalSettingsForm.update(
            localSettings,
            selectedLocalTheme(),
            wakeLockEnabled.isChecked(),
            decoderDebugOverlayEnabled.isChecked());
        localSettingsStore.save(localSettings);
        uiState = BeaconLocalSettingsUiState.from(localSettings, systemDarkTheme());
        applyWindowFlags(uiState);
        applyTheme(rootLayout);
        updateDecoderDebugOverlay();
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
        }

        if (view == touchSurfaceView) {
            view.setBackgroundColor(uiState.surfaceColor());
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
        decoderDebugOverlay.setText(
            "Decoder load " + textValue(decoderLoadPercent) +
                "% | bandwidth " + textValue(estimatedBandwidthMbps) +
                " Mbps | thermal " + textValue(thermalState));
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

    private BeaconViewModel createModel() {
        BeaconClientConfig config = new BeaconClientConfig(serverUrl.getText().toString(), clientId.getText().toString());
        return new BeaconViewModel(
            config.clientId(),
            config.serverUrl(),
            new BeaconApiClient(config),
            new DispatchingStreamConnectionLauncher(
                new AndroidMainThreadDispatcher(this),
                new AndroidIntentStreamConnectionLauncher(this)));
    }

    private BeaconApiClient.ProfilePatch readPatch() {
        BeaconApiClient.ProfilePatch patch = new BeaconApiClient.ProfilePatch();
        patch.preferredWidth = readInteger(width);
        patch.preferredHeight = readInteger(height);
        patch.preferredRefreshHz = readInteger(refreshHz);
        patch.hdrPreference = textValue(hdrPreference);
        patch.codecPreference = textValue(codecPreference);
        patch.qualityMode = textValue(qualityMode);
        patch.bitrateCapMbps = readInteger(bitrateCap);
        patch.audioMode = textValue(audioMode);
        patch.keepAppRunningOnDisconnect = false;
        return patch;
    }

    private BeaconApiClient.GameSelection readGame() {
        int index = gameSelector == null ? -1 : gameSelector.getSelectedItemPosition();
        if (index >= 0 && index < gameEntries.size()) {
            return BeaconApiClient.GameSelection.byGameId(gameEntries.get(index).id());
        }

        return BeaconApiClient.GameSelection.byGameId(textValue(gameId));
    }

    private BeaconApiClient.ClientCapabilities readCapabilities() {
        int refresh = readRequiredInteger(refreshHz);
        return new BeaconApiClient.ClientCapabilities(
            true,
            true,
            true,
            false,
            false,
            refresh,
            true,
            readScreenMode());
    }

    private BeaconApiClient.ClientTelemetry readTelemetry() {
        return new BeaconApiClient.ClientTelemetry(
            readRequiredInteger(rttMs),
            readRequiredDouble(packetLossPercent),
            readRequiredInteger(decoderLoadPercent),
            readRequiredInteger(estimatedBandwidthMbps),
            textValue(wifiBand),
            readRequiredInteger(batteryPercent),
            textValue(thermalState));
    }

    private String readScreenMode() {
        return readRequiredInteger(width) + "x" + readRequiredInteger(height) + "@" + readRequiredInteger(refreshHz);
    }

    private String selectedLocalTheme() {
        Object selected = localTheme.getSelectedItem();
        return selected == null ? "system" : selected.toString();
    }

    private int localThemeIndex(String value) {
        for (int i = 0; i < LOCAL_THEME_VALUES.length; i++) {
            if (LOCAL_THEME_VALUES[i].equalsIgnoreCase(value)) {
                return i;
            }
        }

        return 0;
    }

    private boolean systemDarkTheme() {
        int nightMode = getResources().getConfiguration().uiMode & Configuration.UI_MODE_NIGHT_MASK;
        return nightMode == Configuration.UI_MODE_NIGHT_YES;
    }

    private Integer readInteger(EditText editText) {
        String value = textValue(editText);
        return value.isEmpty() ? null : Integer.parseInt(value);
    }

    private int readRequiredInteger(EditText editText) {
        return Integer.parseInt(textValue(editText));
    }

    private double readRequiredDouble(EditText editText) {
        return Double.parseDouble(textValue(editText));
    }

    private String textValue(EditText editText) {
        return editText.getText().toString().trim();
    }

    private interface BeaconAction {
        void run(BeaconViewModel model) throws IOException;
    }
}
