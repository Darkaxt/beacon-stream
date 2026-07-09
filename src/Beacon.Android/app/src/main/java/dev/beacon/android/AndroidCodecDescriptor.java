package dev.beacon.android;

public final class AndroidCodecDescriptor {
    private final boolean encoder;
    private final String[] supportedTypes;
    private final boolean lowLatency;
    private final boolean hdr10;

    public AndroidCodecDescriptor(boolean encoder, String[] supportedTypes, boolean lowLatency, boolean hdr10) {
        this.encoder = encoder;
        this.supportedTypes = supportedTypes == null ? new String[0] : supportedTypes.clone();
        this.lowLatency = lowLatency;
        this.hdr10 = hdr10;
    }

    public boolean encoder() {
        return encoder;
    }

    public String[] supportedTypes() {
        return supportedTypes.clone();
    }

    public boolean lowLatency() {
        return lowLatency;
    }

    public boolean hdr10() {
        return hdr10;
    }
}
