package dev.beacon.android;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

final class AnnexBAccessUnitSplitter {
    private static final String MissingStartCode = "Encoded video bytes are not Annex B start-code delimited.";

    private AnnexBAccessUnitSplitter() {
    }

    static List<byte[]> split(byte[] bytes) {
        if (bytes == null || bytes.length < 5 || !startsWithStartCode(bytes, 0)) {
            throw new IllegalArgumentException(MissingStartCode);
        }

        List<Integer> starts = findStartCodes(bytes);
        if (starts.isEmpty()) {
            throw new IllegalArgumentException(MissingStartCode);
        }

        List<byte[]> samples = new ArrayList<>();
        int sampleStart = starts.get(0);
        boolean currentSampleHasVcl = false;

        for (int startIndex = 0; startIndex < starts.size(); startIndex++) {
            int start = starts.get(startIndex);
            int payload = start + 4;
            boolean vcl = payload < bytes.length && isVclNal(bytes[payload]);
            if (vcl && currentSampleHasVcl) {
                samples.add(Arrays.copyOfRange(bytes, sampleStart, start));
                sampleStart = start;
            }

            if (vcl) {
                currentSampleHasVcl = true;
            }
        }

        if (sampleStart < bytes.length) {
            samples.add(Arrays.copyOfRange(bytes, sampleStart, bytes.length));
        }

        return samples;
    }

    private static List<Integer> findStartCodes(byte[] bytes) {
        List<Integer> starts = new ArrayList<>();
        for (int index = 0; index <= bytes.length - 4; index++) {
            if (startsWithStartCode(bytes, index)) {
                starts.add(index);
                index += 3;
            }
        }

        return starts;
    }

    private static boolean startsWithStartCode(byte[] bytes, int index) {
        return bytes[index] == 0 && bytes[index + 1] == 0 && bytes[index + 2] == 0 && bytes[index + 3] == 1;
    }

    private static boolean isVclNal(byte value) {
        int type = value & 0x1F;
        return type >= 1 && type <= 5;
    }
}
