package dev.beacon.android;

import java.io.ByteArrayOutputStream;
import java.util.Arrays;
import java.util.Base64;

final class H264ParameterSets {
    private static final byte[] AnnexBStartCode = new byte[] {0, 0, 0, 1};
    private static final H264ParameterSets Empty = new H264ParameterSets(new byte[0]);
    private static final String InvalidMetadata = "H.264 sprop-parameter-sets metadata is invalid.";

    private final byte[] annexB;

    private H264ParameterSets(byte[] annexB) {
        this.annexB = annexB == null ? new byte[0] : Arrays.copyOf(annexB, annexB.length);
    }

    static H264ParameterSets empty() {
        return Empty;
    }

    static H264ParameterSets fromSpropParameterSets(String value) {
        if (value == null || value.trim().isEmpty()) {
            return empty();
        }

        ByteArrayOutputStream output = new ByteArrayOutputStream();
        String[] encodedNals = value.split(",", -1);
        for (String encodedNal : encodedNals) {
            byte[] nal = decodeNal(encodedNal);
            validateParameterSetNal(nal);
            output.write(AnnexBStartCode, 0, AnnexBStartCode.length);
            output.write(nal, 0, nal.length);
        }

        return new H264ParameterSets(output.toByteArray());
    }

    boolean present() {
        return annexB.length > 0;
    }

    byte[] annexB() {
        return Arrays.copyOf(annexB, annexB.length);
    }

    private static byte[] decodeNal(String encodedNal) {
        String trimmed = encodedNal == null ? "" : encodedNal.trim();
        if (trimmed.isEmpty()) {
            throw invalidMetadata();
        }

        try {
            return Base64.getDecoder().decode(trimmed);
        } catch (IllegalArgumentException ex) {
            throw invalidMetadata();
        }
    }

    private static void validateParameterSetNal(byte[] nal) {
        if (nal.length == 0) {
            throw invalidMetadata();
        }

        int nalType = nal[0] & 0x1F;
        if (nalType != 7 && nalType != 8) {
            throw invalidMetadata();
        }
    }

    private static IllegalArgumentException invalidMetadata() {
        return new IllegalArgumentException(InvalidMetadata);
    }
}
