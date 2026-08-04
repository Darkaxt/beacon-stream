package dev.beacon.android;

import android.content.Context;
import android.content.pm.PackageInfo;
import android.hardware.display.DisplayManager;
import android.os.Build;
import android.view.Display;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;

final class AndroidSystemBenchmarkHardwareSource
    implements AndroidBenchmarkFingerprintProbe.HardwareSource {
    private static final char[] HEX = "0123456789abcdef".toCharArray();
    private final Context context;
    private final AndroidCodecCatalog codecCatalog;

    AndroidSystemBenchmarkHardwareSource(Context context, AndroidCodecCatalog codecCatalog) {
        if (context == null || codecCatalog == null) {
            throw new IllegalArgumentException("Hardware fingerprint dependencies are required.");
        }
        Context application = context.getApplicationContext();
        this.context = application == null ? context : application;
        this.codecCatalog = codecCatalog;
    }

    @Override
    public AndroidBenchmarkFingerprintProbe.HardwareFacts read(
        BeaconApiClient.ClientCapabilities capabilities) {
        String displayRevision = digest(displayInventory());
        String codecRevision = digest(codecInventory());
        String capabilityRevision = digest(
            capabilities.toJson() + "|" + displayRevision + "|" + codecRevision);
        return new AndroidBenchmarkFingerprintProbe.HardwareFacts(
            capabilityRevision,
            Build.VERSION.SDK_INT + ":" + Build.VERSION.RELEASE,
            apkVersion(),
            displayRevision,
            codecRevision);
    }

    @SuppressWarnings("deprecation")
    private String displayInventory() {
        DisplayManager manager = context.getSystemService(DisplayManager.class);
        List<String> inventory = new ArrayList<>();
        if (manager != null) {
            for (Display display : manager.getDisplays()) {
                for (Display.Mode mode : display.getSupportedModes()) {
                    inventory.add(
                        display.getDisplayId() + ":" + mode.getPhysicalWidth() + "x" +
                            mode.getPhysicalHeight() + "@" + mode.getRefreshRate());
                }
                Display.HdrCapabilities hdr = display.getHdrCapabilities();
                if (hdr != null) {
                    int[] types = hdr.getSupportedHdrTypes().clone();
                    Arrays.sort(types);
                    inventory.add(display.getDisplayId() + ":hdr:" + Arrays.toString(types));
                }
            }
        }
        Collections.sort(inventory);
        return String.join("|", inventory);
    }

    private String codecInventory() {
        List<String> inventory = new ArrayList<>();
        for (AndroidCodecDescriptor codec : codecCatalog.codecs()) {
            String[] types = codec.supportedTypes();
            Arrays.sort(types, String.CASE_INSENSITIVE_ORDER);
            inventory.add(
                codec.encoder() + ":" + Arrays.toString(types) + ":" +
                    codec.lowLatency() + ":" + codec.hdr10());
        }
        Collections.sort(inventory);
        return String.join("|", inventory);
    }

    private String apkVersion() {
        try {
            PackageInfo info = context.getPackageManager().getPackageInfo(
                context.getPackageName(),
                0);
            return info.versionName == null
                ? Long.toString(info.getLongVersionCode())
                : info.versionName + ":" + info.getLongVersionCode();
        } catch (RuntimeException | android.content.pm.PackageManager.NameNotFoundException error) {
            return "unknown";
        }
    }

    private static String digest(String value) {
        try {
            byte[] bytes = MessageDigest.getInstance("SHA-256").digest(
                value.getBytes(StandardCharsets.UTF_8));
            char[] result = new char[bytes.length * 2];
            for (int index = 0; index < bytes.length; index++) {
                int valueByte = bytes[index] & 0xff;
                result[index * 2] = HEX[valueByte >>> 4];
                result[index * 2 + 1] = HEX[valueByte & 0x0f];
            }
            return new String(result);
        } catch (NoSuchAlgorithmException error) {
            throw new IllegalStateException("SHA-256 is unavailable.", error);
        }
    }
}
