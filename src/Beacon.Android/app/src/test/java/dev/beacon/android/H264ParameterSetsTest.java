package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

public final class H264ParameterSetsTest {
    @Test
    public void emptyMetadataReturnsAbsentParameterSets() {
        H264ParameterSets fromNull = H264ParameterSets.fromSpropParameterSets(null);
        H264ParameterSets fromBlank = H264ParameterSets.fromSpropParameterSets("  ");

        assertFalse(fromNull.present());
        assertFalse(fromBlank.present());
        assertArrayEquals(new byte[0], fromNull.annexB());
        assertArrayEquals(new byte[0], fromBlank.annexB());
    }

    @Test
    public void decodesSpropParameterSetsToAnnexB() {
        H264ParameterSets parameterSets = H264ParameterSets.fromSpropParameterSets("Z0IAHg==,aM4G4g==");

        assertTrue(parameterSets.present());
        assertArrayEquals(
            new byte[] {
                0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E,
                0, 0, 0, 1, 0x68, (byte) 0xCE, 0x06, (byte) 0xE2
            },
            parameterSets.annexB());
    }

    @Test
    public void ignoresWhitespaceAroundParameterSets() {
        H264ParameterSets parameterSets = H264ParameterSets.fromSpropParameterSets(" Z0IAHg== , aM4G4g== ");

        assertArrayEquals(
            new byte[] {
                0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E,
                0, 0, 0, 1, 0x68, (byte) 0xCE, 0x06, (byte) 0xE2
            },
            parameterSets.annexB());
    }

    @Test
    public void rejectsInvalidBase64ParameterSets() {
        IllegalArgumentException exception = assertThrows(
            IllegalArgumentException.class,
            () -> H264ParameterSets.fromSpropParameterSets("not base64"));

        assertInvalidMetadata(exception);
    }

    @Test
    public void rejectsEmptyDecodedParameterSetNal() {
        IllegalArgumentException exception = assertThrows(
            IllegalArgumentException.class,
            () -> H264ParameterSets.fromSpropParameterSets("Z0IAHg==,"));

        assertInvalidMetadata(exception);
    }

    @Test
    public void rejectsNonParameterSetNalTypes() {
        IllegalArgumentException exception = assertThrows(
            IllegalArgumentException.class,
            () -> H264ParameterSets.fromSpropParameterSets("ZQ=="));

        assertInvalidMetadata(exception);
    }

    @Test
    public void annexBReturnsDefensiveCopy() {
        H264ParameterSets parameterSets = H264ParameterSets.fromSpropParameterSets("Z0IAHg==,aM4G4g==");

        byte[] first = parameterSets.annexB();
        first[4] = 0x00;

        assertArrayEquals(
            new byte[] {
                0, 0, 0, 1, 0x67, 0x42, 0x00, 0x1E,
                0, 0, 0, 1, 0x68, (byte) 0xCE, 0x06, (byte) 0xE2
            },
            parameterSets.annexB());
    }

    private static void assertInvalidMetadata(IllegalArgumentException exception) {
        org.junit.Assert.assertEquals("H.264 sprop-parameter-sets metadata is invalid.", exception.getMessage());
    }
}
