package dev.beacon.android;

interface DeviceBenchmarkRoundExecutor {
    Run start(BeaconBenchmarkHardwarePlan.DecoderRound round, Observer observer);

    interface Run {
        void cancel();
    }

    interface Observer {
        void onCompleted(BeaconBenchmarkCompletionRequest.DecoderSample sample);
        void onFailure(Throwable failure);
    }
}
