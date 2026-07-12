package dev.beacon.android;

import android.content.Context;
import android.hardware.display.DisplayManager;
import android.net.ConnectivityManager;
import android.net.LinkAddress;
import android.net.LinkProperties;
import android.net.Network;
import android.net.NetworkCapabilities;
import android.net.wifi.WifiInfo;
import android.net.wifi.WifiManager;
import android.os.Handler;
import android.os.Looper;

import java.net.InetAddress;

final class AndroidBenchmarkChangeMonitor implements AutoCloseable {
    private final ConnectivityManager connectivity;
    private final DisplayManager displays;
    private final Handler handler;
    private final ConnectivityManager.NetworkCallback networkCallback;
    private final DisplayManager.DisplayListener displayListener;
    private Listener listener;
    private Network network;
    private NetworkCapabilities capabilities;
    private LinkProperties linkProperties;
    private AndroidBenchmarkNetworkState current = AndroidBenchmarkNetworkState.disconnected();
    private boolean started;

    AndroidBenchmarkChangeMonitor(Context context) {
        connectivity = context.getSystemService(ConnectivityManager.class);
        displays = context.getSystemService(DisplayManager.class);
        handler = new Handler(Looper.getMainLooper());
        networkCallback = new ConnectivityManager.NetworkCallback() {
            @Override public void onAvailable(Network value) {
                synchronized (AndroidBenchmarkChangeMonitor.this) {
                    if (!value.equals(network)) {
                        network = value;
                        capabilities = null;
                        linkProperties = null;
                    }
                }
            }

            @Override public void onCapabilitiesChanged(
                Network value,
                NetworkCapabilities updated) {
                updateCapabilities(value, updated);
            }

            @Override public void onLinkPropertiesChanged(
                Network value,
                LinkProperties updated) {
                updateLinkProperties(value, updated);
            }

            @Override public void onLost(Network value) {
                Listener target = null;
                synchronized (AndroidBenchmarkChangeMonitor.this) {
                    if (value.equals(network)) {
                        network = null;
                        capabilities = null;
                        linkProperties = null;
                        current = AndroidBenchmarkNetworkState.disconnected();
                        target = listener;
                    }
                }
                if (target != null) target.onChanged(current());
            }
        };
        displayListener = new DisplayManager.DisplayListener() {
            @Override public void onDisplayAdded(int displayId) { publishCurrent(); }
            @Override public void onDisplayRemoved(int displayId) { publishCurrent(); }
            @Override public void onDisplayChanged(int displayId) { publishCurrent(); }
        };
    }

    synchronized void start(Listener value) {
        if (value == null) throw new IllegalArgumentException("Change listener is required.");
        if (started) return;
        if (connectivity == null) {
            throw new IllegalStateException("Android connectivity service is unavailable.");
        }
        listener = value;
        started = true;
        connectivity.registerDefaultNetworkCallback(networkCallback, handler);
        if (displays != null) displays.registerDisplayListener(displayListener, handler);
    }

    synchronized AndroidBenchmarkNetworkState current() {
        return current;
    }

    @Override
    public synchronized void close() {
        if (!started) return;
        started = false;
        listener = null;
        connectivity.unregisterNetworkCallback(networkCallback);
        if (displays != null) displays.unregisterDisplayListener(displayListener);
    }

    private void updateCapabilities(Network value, NetworkCapabilities updated) {
        synchronized (this) {
            if (!value.equals(network)) {
                network = value;
                linkProperties = null;
            }
            capabilities = new NetworkCapabilities(updated);
        }
        publishIfComplete();
    }

    private void updateLinkProperties(Network value, LinkProperties updated) {
        synchronized (this) {
            if (!value.equals(network)) {
                network = value;
                capabilities = null;
            }
            linkProperties = updated;
        }
        publishIfComplete();
    }

    private void publishIfComplete() {
        Listener target;
        AndroidBenchmarkNetworkState snapshot;
        synchronized (this) {
            if (!started || capabilities == null || linkProperties == null) return;
            current = toState(capabilities, linkProperties);
            target = listener;
            snapshot = current;
        }
        if (target != null) target.onChanged(snapshot);
    }

    private void publishCurrent() {
        Listener target;
        AndroidBenchmarkNetworkState snapshot;
        synchronized (this) {
            if (!started) return;
            target = listener;
            snapshot = current;
        }
        if (target != null) target.onChanged(snapshot);
    }

    private static AndroidBenchmarkNetworkState toState(
        NetworkCapabilities capabilities,
        LinkProperties links) {
        String transport = transport(capabilities);
        String wifiBand = null;
        Integer wifiChannel = null;
        int linkSpeedMbps = Math.max(0, capabilities.getLinkDownstreamBandwidthKbps() / 1000);
        String rawSsid = null;
        String rawBssid = null;
        if (capabilities.getTransportInfo() instanceof WifiInfo wifi) {
            int frequency = wifi.getFrequency();
            wifiBand = AndroidBenchmarkFingerprintProbe.wifiBand(frequency);
            wifiChannel = AndroidBenchmarkFingerprintProbe.wifiChannel(frequency);
            linkSpeedMbps = Math.max(linkSpeedMbps, wifi.getLinkSpeed());
            rawSsid = availableSsid(wifi.getSSID());
            rawBssid = availableBssid(wifi.getBSSID());
        }
        return new AndroidBenchmarkNetworkState(
            transport,
            networkPrefix(links),
            wifiBand,
            wifiChannel,
            AndroidBenchmarkFingerprintProbe.linkSpeedBucket(linkSpeedMbps),
            rawSsid,
            rawBssid);
    }

    private static String transport(NetworkCapabilities capabilities) {
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) return "vpn";
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_WIFI)) return "wifi";
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_ETHERNET)) return "ethernet";
        if (capabilities.hasTransport(NetworkCapabilities.TRANSPORT_CELLULAR)) return "cellular";
        return "other";
    }

    private static String networkPrefix(LinkProperties links) {
        for (LinkAddress link : links.getLinkAddresses()) {
            InetAddress address = link.getAddress();
            if (!address.isLoopbackAddress()) {
                byte[] bytes = address.getAddress().clone();
                int prefix = link.getPrefixLength();
                for (int bit = prefix; bit < bytes.length * 8; bit++) {
                    bytes[bit / 8] &= (byte) ~(1 << (7 - bit % 8));
                }
                try {
                    return InetAddress.getByAddress(bytes).getHostAddress() + "/" + prefix;
                } catch (java.net.UnknownHostException ignored) {
                }
            }
        }
        return "0.0.0.0/0";
    }

    private static String availableSsid(String value) {
        if (value == null || WifiManager.UNKNOWN_SSID.equals(value)) return null;
        String normalized = value.trim();
        if (normalized.length() >= 2 && normalized.startsWith("\"") && normalized.endsWith("\"")) {
            normalized = normalized.substring(1, normalized.length() - 1);
        }
        return normalized.isEmpty() ? null : normalized;
    }

    private static String availableBssid(String value) {
        if (value == null || "02:00:00:00:00:00".equalsIgnoreCase(value)) return null;
        return value.trim().isEmpty() ? null : value.trim();
    }

    interface Listener {
        void onChanged(AndroidBenchmarkNetworkState network);
    }
}
