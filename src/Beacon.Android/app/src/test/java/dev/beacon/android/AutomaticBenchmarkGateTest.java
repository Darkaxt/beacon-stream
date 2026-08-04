package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class AutomaticBenchmarkGateTest {
    @Test
    public void duplicateCallbacksDoNotRepeatInFlightOrCompletedFingerprints() {
        AutomaticBenchmarkGate gate = new AutomaticBenchmarkGate();
        gate.enable();

        assertTrue(gate.begin("network-a"));
        assertFalse(gate.begin("network-a"));
        gate.finish("network-a", true);
        assertFalse(gate.begin("network-a"));
    }

    @Test
    public void changedFingerprintSupersedesInFlightRunAndStaleCompletionCannotWin() {
        AutomaticBenchmarkGate gate = new AutomaticBenchmarkGate();
        gate.enable();

        assertTrue(gate.begin("network-a"));
        assertTrue(gate.begin("network-b"));
        gate.finish("network-a", true);
        assertFalse(gate.begin("network-b"));
        gate.finish("network-b", true);
        assertFalse(gate.begin("network-b"));
    }

    @Test
    public void failedFingerprintCanBeRetried() {
        AutomaticBenchmarkGate gate = new AutomaticBenchmarkGate();
        gate.enable();

        assertTrue(gate.begin("network-a"));
        gate.finish("network-a", false);
        assertTrue(gate.begin("network-a"));
    }

    @Test
    public void departureBlocksChangedNetworkUntilNextArrival() {
        AutomaticBenchmarkGate gate = new AutomaticBenchmarkGate();
        gate.enable();
        assertTrue(gate.begin("network-a"));
        gate.finish("network-a", true);

        gate.disable();
        assertFalse(gate.begin("network-b"));

        gate.enable();
        assertTrue(gate.begin("network-b"));
    }
}
