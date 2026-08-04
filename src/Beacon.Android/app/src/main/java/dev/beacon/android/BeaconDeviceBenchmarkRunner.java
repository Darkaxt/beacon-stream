package dev.beacon.android;

public interface BeaconDeviceBenchmarkRunner {
    Run start(BeaconBenchmarkHardwarePlan plan, Observer observer);

    interface Run {
        void cancel();
    }

    interface Observer {
        void onCompleted(BeaconBenchmarkDeviceEvidence evidence);
        void onFailure(Throwable failure);
    }
}
