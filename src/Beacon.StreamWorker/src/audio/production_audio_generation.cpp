#include "beacon/worker/audio/production_audio_generation.h"

#include <utility>

namespace beacon::worker::audio {

ProductionAudioGeneration::ProductionAudioGeneration(
    WorkerAudioPlan plan, IWorkerMediaTransport &transport,
    std::unique_ptr<IAudioFrameCapture> capture,
    AudioPipelineFailureSink failure_sink)
    : plan_(std::move(plan)), transport_(transport),
      capture_(std::move(capture)), failure_sink_(std::move(failure_sink)),
      media_session_(transport_) {}

ProductionAudioGeneration::~ProductionAudioGeneration() { stop(); }

bool ProductionAudioGeneration::start(std::uint64_t session_generation,
                                      std::uint16_t maximum_datagram_bytes) {
  if (!valid_worker_audio_plan(plan_) || !capture_ || session_generation == 0 ||
      started_.exchange(true, std::memory_order_acq_rel) ||
      stopped_.load(std::memory_order_acquire)) {
    return false;
  }
  session_generation_.store(session_generation, std::memory_order_release);
  if (!encoder_.ready()) {
    fail(AudioPipelineFailureBoundary::encoder,
         static_cast<std::uint32_t>(encoder_.opus_error()),
         "opus-encoder-initialize");
    return false;
  }
  if (!media_session_.begin_transport_generation(session_generation,
                                                 maximum_datagram_bytes)) {
    fail(AudioPipelineFailureBoundary::media_session,
         static_cast<std::uint32_t>(
             AudioMediaSessionFailure::transport_not_ready),
         "audio-media-session-start");
    return false;
  }

  const auto weak = weak_from_this();
  const bool capture_started = capture_->start(
      [weak](CapturedAudioFrame frame) {
        if (const auto generation = weak.lock()) {
          generation->process_frame(std::move(frame));
        }
      },
      [weak](WasapiLoopbackFailure capture_failure) {
        if (const auto generation = weak.lock()) {
          generation->fail(AudioPipelineFailureBoundary::capture,
                           capture_failure.native_code,
                           wasapi_loopback_stage_name(capture_failure.stage));
        }
      });
  if (!capture_started) {
    const auto capture_failure = capture_->failure();
    fail(AudioPipelineFailureBoundary::capture, capture_failure.native_code,
         wasapi_loopback_stage_name(capture_failure.stage));
    return false;
  }
  return !failed_.load(std::memory_order_acquire);
}

void ProductionAudioGeneration::stop() noexcept {
  if (stopped_.exchange(true, std::memory_order_acq_rel)) {
    return;
  }
  if (capture_) {
    capture_->stop();
  }
  session_generation_.store(0, std::memory_order_release);
}

void ProductionAudioGeneration::process_frame(
    CapturedAudioFrame frame) noexcept {
  if (stopped_.load(std::memory_order_acquire) ||
      failed_.load(std::memory_order_acquire)) {
    return;
  }
  const auto generation = session_generation_.load(std::memory_order_acquire);
  if (generation == 0) {
    return;
  }

  const auto encoded = encoder_.encode(frame.interleaved_pcm);
  if (!encoded) {
    fail(AudioPipelineFailureBoundary::encoder,
         static_cast<std::uint32_t>(encoder_.failure()), "opus-encode");
    return;
  }
  const auto sent = media_session_.send_packet(generation, *encoded,
                                               frame.presentation_time_us);
  if (sent.failure != AudioMediaSessionFailure::none) {
    fail(sent.failure == AudioMediaSessionFailure::transport_closed
             ? AudioPipelineFailureBoundary::transport
             : AudioPipelineFailureBoundary::media_session,
         static_cast<std::uint32_t>(sent.failure),
         sent.failure == AudioMediaSessionFailure::transport_closed
             ? "audio-transport-send"
             : "audio-media-session-send");
  }
}

void ProductionAudioGeneration::fail(AudioPipelineFailureBoundary boundary,
                                     std::uint32_t native_code,
                                     std::string failure_stage) noexcept {
  if (failed_.exchange(true, std::memory_order_acq_rel)) {
    return;
  }
  try {
    if (failure_sink_) {
      failure_sink_({
          .session_id = plan_.session_id,
          .session_generation =
              session_generation_.load(std::memory_order_acquire),
          .boundary = boundary,
          .failure_stage = std::move(failure_stage),
          .native_code = native_code,
      });
    }
  } catch (...) {
  }
  transport_.request_active_disconnect();
}

ProductionAudioGenerationFactory::ProductionAudioGenerationFactory(
    IWorkerMediaTransport &transport, AudioPipelineFailureSink failure_sink)
    : transport_(transport), failure_sink_(std::move(failure_sink)) {}

std::shared_ptr<IAudioPipelineGeneration>
ProductionAudioGenerationFactory::create(const WorkerAudioPlan &plan) {
  return std::make_shared<ProductionAudioGeneration>(
      plan, transport_, std::make_unique<WasapiLoopbackCapture>(),
      failure_sink_);
}

} // namespace beacon::worker::audio
