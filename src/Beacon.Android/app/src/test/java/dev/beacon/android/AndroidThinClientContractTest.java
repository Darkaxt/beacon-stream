package dev.beacon.android;

import org.junit.Test;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.util.List;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class AndroidThinClientContractTest {
    private static final List<String> ForbiddenPolicyText = List.of(
        "ProfilePatch",
        "patchProfile",
        "Patch Profile",
        "Preferred width",
        "Preferred height",
        "Preferred refresh",
        "HDR preference",
        "Codec preference",
        "Quality mode",
        "Bitrate cap",
        "Audio mode",
        "RTT ms",
        "Packet loss percent",
        "Decoder load percent",
        "Estimated bandwidth Mbps",
        "Wi-Fi band",
        "Battery percent",
        "Thermal state",
        "Report Capabilities",
        "Report Telemetry",
        "Beacon Active",
        "Beacon Inactive");

    @Test
    public void productionJavaContainsNoClientPolicyEditorOrManualPresenceControls() throws Exception {
        File sourceRoot = sourceRoot();
        assertTrue("Android production source should exist at " + sourceRoot.getAbsolutePath(), sourceRoot.isDirectory());

        StringBuilder production = new StringBuilder();
        File[] files = sourceRoot.listFiles((directory, name) -> name.endsWith(".java"));
        if (files != null) {
            for (File file : files) {
                production.append(new String(Files.readAllBytes(file.toPath()), StandardCharsets.UTF_8));
            }
        }

        for (String forbidden : ForbiddenPolicyText) {
            assertFalse("Android production source must not contain " + forbidden, production.toString().contains(forbidden));
        }

        assertTrue("Android must preserve the reconnect command", production.toString().contains("button(\"Reconnect\""));
        assertTrue(
            "Android quit must leave automatic presence",
            production.toString().contains("button(\"Quit\", model -> leaveAndQuit(model))"));
    }

    private static File sourceRoot() {
        File moduleRelative = new File("app/src/main/java/dev/beacon/android");
        if (moduleRelative.isDirectory()) {
            return moduleRelative;
        }

        return new File("src/main/java/dev/beacon/android");
    }
}
