#include "beacon/worker/video/production_video_generation.h"

#include "beacon/worker/video/quic_media_rate_adapter.h"

#include <utility>

namespace beacon::worker::video {

ProductionVideoGeneration::ProductionVideoGeneration(
    WorkerVideoPlan plan, IWorkerMediaTransport &transport,
    std::unique_ptr<capture::IWgcCapturePlatform> capture_platform,
    std::unique_ptr<ID3d11VideoProcessorPlatform> processor_platform,
    std::unique_ptr<INvencH264Api> encoder_api,
    VideoPipelineFailureSink failure_sink)
    : plan_(std::move(plan)), transport_(transport),
      failure_sink_(std::move(failure_sink)),
      capture_(std::move(capture_platform)),
      processor_(std::move(processor_platform)),
      encoder_(std::move(encoder_api),
               {.width = plan_.width,
                .height = plan_.height,
                .frame_rate_numerator = plan_.frame_rate_numerator,
                .frame_rate_denominator = plan_.frame_rate_denominator,
                .bitrate_bps = plan_.initial_bitrate_bps}),
      media_session_(
          transport_, encoder_,
          {.minimum_bitrate_bps = plan_.minimum_bitrate_bps,
           .initial_bitrate_bps = plan_.initial_bitrate_bps,
           .maximum_bitrate_bps = plan_.maximum_bitrate_bps}) {}

ProductionVideoGeneration::~ProductionVideoGeneration() { stop(); }

bool ProductionVideoGeneration::start(
    std::uint64_t session_generation,
    std::uint16_t maximum_datagram_bytes) {
  if (!valid_worker_video_plan(plan_) || session_generation == 0 ||
      started_.exchange(true, std::memory_order_acq_rel) ||
      stopped_.load(std::memory_order_acquire)) {
    return false;
  }
  session_generation_.store(session_generation, std::memory_order_release);
  if (!media_session_.begin_transport_generation(session_generation,
                                                 maximum_datagram_bytes)) {
    fail(VideoPipelineFailureBoundary::media_session,
         static_cast<std::uint32_t>(media_session_.failure()));
    return false;
  }

  const auto weak = weak_from_this();
  const bool capture_started = capture_.start(
      {.device_name = plan_.display_device_name},
      [weak](capture::CapturedD3d11Frame frame) {
        if (const auto generation = weak.lock()) {
          generation->process_frame(std::move(frame));
        }
      });
  if (!capture_started) {
    fail(VideoPipelineFailureBoundary::capture,
         static_cast<std::uint32_t>(capture_.failure()));
    return false;
  }
  return true;
}

void ProductionVideoGeneration::handle_media_event(
    const QuicMediaEvent &event) {
  if (stopped_.load(std::memory_order_acquire) ||
      failed_.load(std::memory_order_acquire)) {
    return;
  }
  const auto evidence_sequence =
      next_evidence_sequence_.fetch_add(1, std::memory_order_relaxed);
  if (const auto evidence =
          media_rate_evidence_from(event, evidence_sequence)) {
    static_cast<void>(media_session_.observe(*evidence));
  }
}

bool ProductionVideoGeneration::request_idr() {
  const auto generation =
      session_generation_.load(std::memory_order_acquire);
  if (generation == 0 || stopped_.load(std::memory_order_acquire) ||
      failed_.load(std::memory_order_acquire)) {
    return false;
  }
  const auto evidence_sequence =
      next_evidence_sequence_.fetch_add(1, std::memory_order_relaxed);
  const auto decision = media_session_.observe({
      .kind = MediaRateEvidenceKind::reliable_idr_request,
      .session_generation = generation,
      .evidence_sequence = evidence_sequence,
  });
  return decision.force_idr;
}

void ProductionVideoGeneration::stop() noexcept {
  if (stopped_.exchange(true, std::memory_order_acq_rel)) {
    return;
  }
  capture_.stop();
  session_generation_.store(0, std::memory_order_release);
}

void ProductionVideoGeneration::process_frame(
    capture::CapturedD3d11Frame frame) noexcept {
  if (stopped_.load(std::memory_order_acquire) ||
      failed_.load(std::memory_order_acquire)) {
    return;
  }
  const auto generation =
      session_generation_.load(std::memory_order_acquire);
  if (generation == 0) {
    return;
  }

  const auto converted = processor_.convert(
      frame,
      {.output_width = plan_.width,
       .output_height = plan_.height,
       .frame_rate_numerator = plan_.frame_rate_numerator,
       .frame_rate_denominator = plan_.frame_rate_denominator});
  if (!converted) {
    fail(VideoPipelineFailureBoundary::video_processor,
         static_cast<std::uint32_t>(processor_.failure()));
    return;
  }

  const auto control = media_session_.apply_pending_encoder_control();
  if (!control.ready) {
    fail(VideoPipelineFailureBoundary::media_session,
         static_cast<std::uint32_t>(control.failure));
    return;
  }
  const auto encoded = encoder_.encode(*converted, control.force_idr);
  if (!encoded) {
    fail(VideoPipelineFailureBoundary::encoder,
         static_cast<std::uint32_t>(encoder_.failure()));
    return;
  }

  const auto presentation_time_us =
      static_cast<std::uint64_t>(encoded->qpc_timestamp) / 10U;
  const auto sent = media_session_.send_access_unit(
      generation, *encoded, presentation_time_us);
  if (sent.failure != VideoMediaSessionFailure::none) {
    fail(sent.failure == VideoMediaSessionFailure::transport_closed
             ? VideoPipelineFailureBoundary::transport
             : VideoPipelineFailureBoundary::media_session,
         static_cast<std::uint32_t>(sent.failure));
  }
}

void ProductionVideoGeneration::fail(VideoPipelineFailureBoundary boundary,
                                     std::uint32_t native_code) noexcept {
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
          .native_code = native_code,
      });
    }
  } catch (...) {
  }
  transport_.request_active_disconnect();
}

ProductionVideoGenerationFactory::ProductionVideoGenerationFactory(
    IWorkerMediaTransport &transport, VideoPipelineFailureSink failure_sink)
    : transport_(transport), failure_sink_(std::move(failure_sink)) {}

std::shared_ptr<IVideoPipelineGeneration>
ProductionVideoGenerationFactory::create(const WorkerVideoPlan &plan) {
  return std::make_shared<ProductionVideoGeneration>(
      plan, transport_, capture::create_windows_wgc_capture_platform(),
      create_windows_d3d11_video_processor_platform(),
      create_windows_nvenc_h264_api(), failure_sink_);
}

} // namespace beacon::worker::video
