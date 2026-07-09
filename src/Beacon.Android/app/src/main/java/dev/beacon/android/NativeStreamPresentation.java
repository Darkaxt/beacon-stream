package dev.beacon.android;

public final class NativeStreamPresentation {
    private static final NativeStreamPresentation NONE = new NativeStreamPresentation(false, "", "", "");

    private final boolean active;
    private final String kind;
    private final String endpointUri;
    private final String label;

    private NativeStreamPresentation(boolean active, String kind, String endpointUri, String label) {
        this.active = active;
        this.kind = kind == null ? "" : kind;
        this.endpointUri = endpointUri == null ? "" : endpointUri;
        this.label = label == null ? "" : label;
    }

    public static NativeStreamPresentation none() {
        return NONE;
    }

    public static NativeStreamPresentation colorBars(String endpointUri) {
        return new NativeStreamPresentation(true, "color-bars", endpointUri, "Beacon test stream");
    }

    public boolean active() {
        return active;
    }

    public String kind() {
        return kind;
    }

    public String endpointUri() {
        return endpointUri;
    }

    public String label() {
        return label;
    }
}
