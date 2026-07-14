#pragma once

#include "beacon/worker/capture/wgc_display_capture.h"
#include "beacon/worker/video/d3d11_video_processor.h"
#include "beacon/worker/video/nvenc_h264_encoder.h"
#include "beacon/worker/video/video_media_session.h"
#include "beacon/worker/video/worker_video_pipeline.h"

#include <atomic>
#include <cstdint>
#include <functional>
#include <memory>
#include <string>

namespace beacon::worker::video {

enum class VideoPipelineFailureBoundary {
  capture,
  video_processor,
  encoder,
  media_session,
  transport,
};

struct VideoPipelineFailureEvent {
  std::string session_id;
  std::uint64_t session_generation{};
  VideoPipelineFailureBoundary boundary{VideoPipelineFailureBoundary::capture};
  std::uint32_t native_code{};
};

using VideoPipelineFailureSink =
    std::function<void(VideoPipelineFailureEvent)>;

class ProductionVideoGeneration final
    : public IVideoPipelineGeneration,
      public std::enable_shared_from_this<ProductionVideoGeneration> {
public:
  ProductionVideoGeneration(
      WorkerVideoPlan plan, IWorkerMediaTransport &transport,
      std::unique_ptr<capture::IWgcCapturePlatform> capture_platform,
      std::unique_ptr<ID3d11VideoProcessorPlatform> processor_platform,
      std::unique_ptr<INvencH264Api> encoder_api,
      VideoPipelineFailureSink failure_sink);
  ~ProductionVideoGeneration() override;

  ProductionVideoGeneration(const ProductionVideoGeneration &) = delete;
  ProductionVideoGeneration &
  operator=(const ProductionVideoGeneration &) = delete;

  [[nodiscard]] bool start(std::uint64_t session_generation,
                           std::uint16_t maximum_datagram_bytes) override;
  void handle_media_event(const QuicMediaEvent &event) override;
  [[nodiscard]] bool request_idr() override;
  void stop() noexcept override;

private:
  void process_frame(capture::CapturedD3d11Frame frame) noexcept;
  void fail(VideoPipelineFailureBoundary boundary,
            std::uint32_t native_code) noexcept;

  WorkerVideoPlan plan_;
  IWorkerMediaTransport &transport_;
  VideoPipelineFailureSink failure_sink_;
  capture::WgcDisplayCapture capture_;
  D3d11VideoProcessor processor_;
  NvencH264Encoder encoder_;
  VideoMediaSession media_session_;
  std::atomic_uint64_t session_generation_{};
  std::atomic_uint64_t next_evidence_sequence_{1};
  std::atomic_bool started_{};
  std::atomic_bool stopped_{};
  std::atomic_bool failed_{};
};

class ProductionVideoGenerationFactory final
    : public IVideoPipelineGenerationFactory {
public:
  ProductionVideoGenerationFactory(IWorkerMediaTransport &transport,
                                   VideoPipelineFailureSink failure_sink);

  [[nodiscard]] std::shared_ptr<IVideoPipelineGeneration>
  create(const WorkerVideoPlan &plan) override;

private:
  IWorkerMediaTransport &transport_;
  VideoPipelineFailureSink failure_sink_;
};

} // namespace beacon::worker::video
