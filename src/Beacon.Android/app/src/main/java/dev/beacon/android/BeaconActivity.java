package dev.beacon.android;

import android.app.Activity;
import android.graphics.Color;
import android.os.Bundle;
import android.view.Gravity;
import android.view.View;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.io.IOException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

public final class BeaconActivity extends Activity {
    private final ExecutorService executor = Executors.newSingleThreadExecutor();

    private EditText serverUrl;
    private EditText clientId;
    private EditText width;
    private EditText height;
    private EditText refreshHz;
    private EditText hdrPreference;
    private EditText codecPreference;
    private EditText qualityMode;
    private EditText bitrateCap;
    private EditText audioMode;
    private EditText gameId;
    private EditText rttMs;
    private EditText packetLossPercent;
    private EditText decoderLoadPercent;
    private EditText estimatedBandwidthMbps;
    private EditText wifiBand;
    private EditText batteryPercent;
    private EditText thermalState;
    private TextView status;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
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
        root.setOrientation(LinearLayout.VERTICAL);
        root.setPadding(28, 28, 28, 28);
        root.setBackgroundColor(Color.rgb(16, 20, 24));
        scrollView.addView(root);

        TextView title = text("Beacon", 28, true);
        root.addView(title);

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
        root.addView(rttMs);
        root.addView(packetLossPercent);
        root.addView(decoderLoadPercent);
        root.addView(estimatedBandwidthMbps);
        root.addView(wifiBand);
        root.addView(batteryPercent);
        root.addView(thermalState);

        root.addView(button("Hello / Refresh", model -> model.refresh()));
        root.addView(button("Load Games", model -> model.loadGames()));
        root.addView(button("Patch Profile", model -> model.patchProfile(readPatch())));
        root.addView(button("Report Capabilities", model -> model.reportCapabilities(readCapabilities())));
        root.addView(button("Report Telemetry", model -> model.reportTelemetry(readTelemetry())));
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
        root.addView(button("Stop Stream", model -> model.stopStream()));
        root.addView(button("Disconnect", model -> model.disconnect()));
        root.addView(button("Quit", model -> model.quit(new BeaconApiClient.QuitState(false))));
        root.addView(button("Emergency Restore", model -> model.emergencyRestore()));

        status = text("Idle", 14, false);
        status.setGravity(Gravity.START);
        root.addView(status);

        return scrollView;
    }

    private EditText input(String hint, String value) {
        EditText editText = new EditText(this);
        editText.setHint(hint);
        editText.setText(value);
        editText.setSingleLine(true);
        editText.setTextColor(Color.WHITE);
        editText.setHintTextColor(Color.rgb(160, 170, 180));
        return editText;
    }

    private TextView text(String value, int sizeSp, boolean title) {
        TextView textView = new TextView(this);
        textView.setText(value);
        textView.setTextSize(sizeSp);
        textView.setTextColor(Color.WHITE);
        textView.setPadding(0, title ? 0 : 12, 0, 12);
        return textView;
    }

    private Button button(String label, BeaconAction action) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        button.setOnClickListener(view -> runAction(label, action));
        return button;
    }

    private void runAction(String label, BeaconAction action) {
        status.setText(label + "...");
        BeaconViewModel model = createModel();
        executor.execute(() -> {
            try {
                action.run(model);
                setStatus(model.status() + "\nGames: " + model.latestGames() + "\nPlan: " + model.latestPlan() + "\nStream: " + model.latestStream());
            } catch (IOException | RuntimeException ex) {
                setStatus(label + " failed: " + ex.getMessage());
            }
        });
    }

    private void setStatus(String value) {
        runOnUiThread(() -> status.setText(value));
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
