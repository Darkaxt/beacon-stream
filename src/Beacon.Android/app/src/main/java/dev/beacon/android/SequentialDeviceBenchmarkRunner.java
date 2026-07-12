package dev.beacon.android;

import java.util.ArrayList;
import java.util.List;

final class SequentialDeviceBenchmarkRunner implements BeaconDeviceBenchmarkRunner {
    private final DeviceBenchmarkRoundExecutor roundExecutor;
    private final DeviceBenchmarkPowerSampler powerSampler;

    SequentialDeviceBenchmarkRunner(
        DeviceBenchmarkRoundExecutor roundExecutor,
        DeviceBenchmarkPowerSampler powerSampler) {
        if (roundExecutor == null || powerSampler == null) {
            throw new IllegalArgumentException("Device benchmark dependencies are required.");
        }
        this.roundExecutor = roundExecutor;
        this.powerSampler = powerSampler;
    }

    @Override
    public Run start(BeaconBenchmarkHardwarePlan plan, Observer observer) {
        if (plan == null || observer == null) {
            throw new IllegalArgumentException("Device benchmark plan and observer are required.");
        }
        if (plan.decoderRounds().isEmpty()) {
            BeaconBenchmarkCompletionRequest.PowerSample power = powerSampler.sample();
            if (power == null) {
                throw new IllegalStateException("Device power sampler returned no sample.");
            }
            observer.onCompleted(new BeaconBenchmarkDeviceEvidence(
                List.of(),
                List.of(power)));
            return () -> { };
        }
        RunState state = new RunState(plan, observer);
        state.startNextRound();
        return state;
    }

    private final class RunState implements Run {
        private final BeaconBenchmarkHardwarePlan plan;
        private final Observer observer;
        private final List<BeaconBenchmarkCompletionRequest.DecoderSample> decoderSamples =
            new ArrayList<>();
        private final List<BeaconBenchmarkCompletionRequest.PowerSample> powerSamples =
            new ArrayList<>();
        private final List<BeaconBenchmarkCompletionRequest.DecoderSample> repetitionSamples =
            new ArrayList<>();
        private int roundIndex;
        private int repetitionIndex;
        private DeviceBenchmarkRoundExecutor.Run activeRound;
        private boolean cancelled;
        private boolean finished;

        RunState(BeaconBenchmarkHardwarePlan plan, Observer observer) {
            this.plan = plan;
            this.observer = observer;
        }

        void startNextRound() {
            int index;
            BeaconBenchmarkHardwarePlan.DecoderRound round;
            try {
                synchronized (this) {
                    if (cancelled || finished) {
                        return;
                    }
                    index = roundIndex;
                    round = plan.decoderRounds().get(index);
                }
                if (repetitionIndex == 0) {
                    samplePower();
                }
                int repetition = repetitionIndex;
                DeviceBenchmarkRoundExecutor.Run started = roundExecutor.start(
                    round.singleRepetition(),
                    new DeviceBenchmarkRoundExecutor.Observer() {
                        @Override
                        public void onCompleted(
                            BeaconBenchmarkCompletionRequest.DecoderSample sample) {
                            completeRepetition(index, repetition, sample);
                        }

                        @Override
                        public void onFailure(Throwable failure) {
                            fail(index, repetition, failure);
                        }
                    });
                attachRound(index, repetition, started);
            } catch (RuntimeException | Error failure) {
                failCurrent(failure);
            }
        }

        private void completeRepetition(
            int completedIndex,
            int completedRepetition,
            BeaconBenchmarkCompletionRequest.DecoderSample sample) {
            BeaconBenchmarkHardwarePlan.DecoderRound round;
            boolean roundComplete;
            synchronized (this) {
                if (cancelled || finished || roundIndex != completedIndex ||
                    repetitionIndex != completedRepetition) {
                    return;
                }
                if (sample == null) {
                    failCurrent(new IllegalArgumentException(
                        "Decoder benchmark sample is required."));
                    return;
                }
                repetitionSamples.add(sample);
                activeRound = null;
                repetitionIndex++;
                round = plan.decoderRounds().get(completedIndex);
                roundComplete = repetitionIndex == round.repetitionCount();
            }
            if (!roundComplete) {
                startNextRound();
                return;
            }

            BeaconBenchmarkCompletionRequest.DecoderSample aggregate;
            try {
                aggregate = BeaconBenchmarkCompletionRequest.DecoderSample
                    .conservativeAggregate(new ArrayList<>(repetitionSamples));
                samplePower();
            } catch (RuntimeException | Error failure) {
                failCurrent(failure);
                return;
            }
            boolean completeRun;
            synchronized (this) {
                if (cancelled || finished || roundIndex != completedIndex ||
                    repetitionIndex != round.repetitionCount()) {
                    return;
                }
                decoderSamples.add(aggregate);
                repetitionSamples.clear();
                repetitionIndex = 0;
                roundIndex++;
                completeRun = roundIndex == plan.decoderRounds().size();
                if (completeRun) {
                    finished = true;
                }
            }
            if (completeRun) {
                observer.onCompleted(new BeaconBenchmarkDeviceEvidence(
                    decoderSamples,
                    powerSamples));
            } else {
                startNextRound();
            }
        }

        private void samplePower() {
            BeaconBenchmarkCompletionRequest.PowerSample sample = powerSampler.sample();
            if (sample == null) {
                throw new IllegalStateException("Device power sampler returned no sample.");
            }
            synchronized (this) {
                if (!cancelled && !finished) {
                    powerSamples.add(sample);
                }
            }
        }

        private void attachRound(
            int index,
            int repetition,
            DeviceBenchmarkRoundExecutor.Run started) {
            if (started == null) {
                throw new IllegalStateException("Decoder benchmark executor returned no active round.");
            }
            boolean retained;
            synchronized (this) {
                retained = !cancelled && !finished && roundIndex == index &&
                    repetitionIndex == repetition;
                if (retained) {
                    activeRound = started;
                }
            }
            if (!retained) {
                started.cancel();
            }
        }

        private void fail(int failedIndex, int failedRepetition, Throwable failure) {
            synchronized (this) {
                if (roundIndex != failedIndex || repetitionIndex != failedRepetition) {
                    return;
                }
            }
            failCurrent(failure);
        }

        private void failCurrent(Throwable failure) {
            DeviceBenchmarkRoundExecutor.Run toCancel;
            synchronized (this) {
                if (cancelled || finished) {
                    return;
                }
                finished = true;
                toCancel = activeRound;
                activeRound = null;
            }
            if (toCancel != null) {
                toCancel.cancel();
            }
            observer.onFailure(failure == null
                ? new IllegalStateException("Decoder benchmark round failed.")
                : failure);
        }

        @Override
        public void cancel() {
            DeviceBenchmarkRoundExecutor.Run toCancel;
            synchronized (this) {
                if (cancelled || finished) {
                    return;
                }
                cancelled = true;
                toCancel = activeRound;
                activeRound = null;
            }
            if (toCancel != null) {
                toCancel.cancel();
            }
        }
    }
}
