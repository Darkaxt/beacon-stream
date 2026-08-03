#pragma once

#include "beacon/worker/audio/audio_media_session.h"
#include "beacon/worker/audio/audio_pipeline_failure.h"
#include "beacon/worker/audio/opus_audio_encoder.h"
#include "beacon/worker/audio/wasapi_loopback_capture.h"
#include "beacon/worker/audio/worker_audio_pipeline.h"

#include <atomic>
#include <cstdint>
#include <memory>

namespace beacon::worker::audio {

class ProductionAudioGeneration final
    : public IAudioPipelineGeneration,
      public std::enable_shared_from_this<ProductionAudioGeneration> {
public:
  ProductionAudioGeneration(WorkerAudioPlan plan,
                            IWorkerMediaTransport &transport,
                            std::unique_ptr<IAudioFrameCapture> capture,
                            AudioPipelineFailureSink failure_sink);
  ~ProductionAudioGeneration() override;

  ProductionAudioGeneration(const ProductionAudioGeneration &) = delete;
  ProductionAudioGeneration &
  operator=(const ProductionAudioGeneration &) = delete;

  [[nodiscard]] bool start(std::uint64_t session_generation,
                           std::uint16_t maximum_datagram_bytes) override;
  void stop() noexcept override;

private:
  void process_frame(CapturedAudioFrame frame) noexcept;
  void fail(AudioPipelineFailureBoundary boundary, std::uint32_t native_code,
            std::string failure_stage = {}) noexcept;

  WorkerAudioPlan plan_;
  IWorkerMediaTransport &transport_;
  std::unique_ptr<IAudioFrameCapture> capture_;
  AudioPipelineFailureSink failure_sink_;
  OpusAudioEncoder encoder_;
  AudioMediaSession media_session_;
  std::atomic_uint64_t session_generation_{};
  std::atomic_bool started_{};
  std::atomic_bool stopped_{};
  std::atomic_bool failed_{};
};

class ProductionAudioGenerationFactory final
    : public IAudioPipelineGenerationFactory {
public:
  ProductionAudioGenerationFactory(IWorkerMediaTransport &transport,
                                   AudioPipelineFailureSink failure_sink);

  [[nodiscard]] std::shared_ptr<IAudioPipelineGeneration>
  create(const WorkerAudioPlan &plan) override;

private:
  IWorkerMediaTransport &transport_;
  AudioPipelineFailureSink failure_sink_;
};

} // namespace beacon::worker::audio
