package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;

public final class GameStreamRtspSdpMetadataTest {
    @Test
    public void extractsH264SpropParameterSetsFromVideoFmtp() {
        GameStreamRtspSdpMetadata metadata = GameStreamRtspSdpMetadata.from(
            "v=0\r\n" +
                "m=audio 0 RTP/AVP 96\r\n" +
                "a=fmtp:96 sprop-parameter-sets=audio-value\r\n" +
                "m=video 0 RTP/AVP 97\r\n" +
                "a=rtpmap:97 H264/90000\r\n" +
                "a=fmtp:97 packetization-mode=1;profile-level-id=42001e;sprop-parameter-sets=Z0IAHg==,aM4G4g==\r\n");

        assertEquals("Z0IAHg==,aM4G4g==", metadata.h264SpropParameterSets());
    }

    @Test
    public void ignoresAudioSectionParameterSets() {
        GameStreamRtspSdpMetadata metadata = GameStreamRtspSdpMetadata.from(
            "v=0\r\n" +
                "m=audio 0 RTP/AVP 96\r\n" +
                "a=rtpmap:96 H264/90000\r\n" +
                "a=fmtp:96 sprop-parameter-sets=audio-value\r\n" +
                "m=video 0 RTP/AVP 97\r\n" +
                "a=rtpmap:97 H264/90000\r\n");

        assertEquals("", metadata.h264SpropParameterSets());
    }

    @Test
    public void acceptsCaseInsensitiveParameterNameAndWhitespace() {
        GameStreamRtspSdpMetadata metadata = GameStreamRtspSdpMetadata.from(
            "v=0\n" +
                "m=video 0 RTP/AVP 97\n" +
                "a=rtpmap:97 h264/90000\n" +
                "a=fmtp:97 packetization-mode=1; SProp-Parameter-Sets = Z0IAHg==,aM4G4g== ; profile-level-id=42001e\n");

        assertEquals("Z0IAHg==,aM4G4g==", metadata.h264SpropParameterSets());
    }

    @Test
    public void fallsBackToVideoFmtpWhenH264RtpmapIsMissing() {
        GameStreamRtspSdpMetadata metadata = GameStreamRtspSdpMetadata.from(
            "v=0\r\n" +
                "m=video 0 RTP/AVP 97\r\n" +
                "a=fmtp:97 packetization-mode=1; sprop-parameter-sets=Z0IAHg==,aM4G4g==\r\n");

        assertEquals("Z0IAHg==,aM4G4g==", metadata.h264SpropParameterSets());
    }

    @Test
    public void returnsEmptyValueWhenVideoH264ParameterSetsAreMissing() {
        GameStreamRtspSdpMetadata metadata = GameStreamRtspSdpMetadata.from(
            "v=0\r\n" +
                "m=video 0 RTP/AVP 97\r\n" +
                "a=rtpmap:97 H264/90000\r\n" +
                "a=fmtp:97 packetization-mode=1; profile-level-id=42001e\r\n");

        assertEquals("", metadata.h264SpropParameterSets());
    }
}
