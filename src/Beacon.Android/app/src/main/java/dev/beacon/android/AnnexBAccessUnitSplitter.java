package dev.beacon.android;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

final class AnnexBAccessUnitSplitter {
    private static final String MissingStartCode = "Encoded video bytes are not Annex B start-code delimited.";

    private AnnexBAccessUnitSplitter() {
    }

    static List<byte[]> split(byte[] bytes) {
        if (bytes == null || bytes.length < 4 || startCodeLength(bytes, 0) == 0) {
            throw new IllegalArgumentException(MissingStartCode);
        }

        List<StartCode> starts = findStartCodes(bytes);
        if (starts.isEmpty()) {
            throw new IllegalArgumentException(MissingStartCode);
        }

        List<byte[]> samples = new ArrayList<>();
        int sampleStart = starts.get(0).index;
        boolean currentSampleHasVcl = false;

        for (int startIndex = 0; startIndex < starts.size(); startIndex++) {
            StartCode start = starts.get(startIndex);
            int payload = start.index + start.length;
            boolean vcl = payload < bytes.length && isVclNal(bytes[payload]);
            if (vcl && currentSampleHasVcl) {
                samples.add(Arrays.copyOfRange(bytes, sampleStart, start.index));
                sampleStart = start.index;
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

    private static List<StartCode> findStartCodes(byte[] bytes) {
        List<StartCode> starts = new ArrayList<>();
        for (int index = 0; index <= bytes.length - 3; index++) {
            int length = startCodeLength(bytes, index);
            if (length > 0) {
                starts.add(new StartCode(index, length));
                index += length - 1;
            }
        }

        return starts;
    }

    private static int startCodeLength(byte[] bytes, int index) {
        if (index <= bytes.length - 4 &&
            bytes[index] == 0 &&
            bytes[index + 1] == 0 &&
            bytes[index + 2] == 0 &&
            bytes[index + 3] == 1) {
            return 4;
        }

        if (index <= bytes.length - 3 &&
            bytes[index] == 0 &&
            bytes[index + 1] == 0 &&
            bytes[index + 2] == 1) {
            return 3;
        }

        return 0;
    }

    private static boolean isVclNal(byte value) {
        int type = value & 0x1F;
        return type >= 1 && type <= 5;
    }

    private static final class StartCode {
        private final int index;
        private final int length;

        StartCode(int index, int length) {
            this.index = index;
            this.length = length;
        }
    }
}
