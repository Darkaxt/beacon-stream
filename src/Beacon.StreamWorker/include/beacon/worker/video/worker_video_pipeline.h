#pragma once

#include "beacon/worker/quic_listener.h"
#include "beacon/stream/selected_video_mode.h"

#include <cstdint>
#include <memory>
#include <mutex>
#include <string>

namespace beacon::worker::video {

struct WorkerVideoPlan {
  std::string session_id;
  std::wstring display_device_name;
  std::uint32_t width{};
  std::uint32_t height{};
  std::uint32_t frame_rate_numerator{};
  std::uint32_t frame_rate_denominator{};
  std::uint32_t minimum_bitrate_bps{};
  std::uint32_t initial_bitrate_bps{};
  std::uint32_t maximum_bitrate_bps{};
  stream::v1::VideoCodec codec{stream::v1::VIDEO_CODEC_H264};
  stream::v1::DynamicRange dynamic_range{stream::v1::DYNAMIC_RANGE_SDR};
  stream::v1::VideoProfile profile{stream::v1::VIDEO_PROFILE_H264_HIGH};
  std::uint32_t bit_depth{8};
  stream::v1::ColorPrimaries color_primaries{stream::v1::COLOR_PRIMARIES_BT709};
  stream::v1::TransferFunction transfer_function{stream::v1::TRANSFER_FUNCTION_BT709};
  stream::v1::MatrixCoefficients matrix_coefficients{stream::v1::MATRIX_COEFFICIENTS_BT709};
  stream::v1::ColorRange color_range{stream::v1::COLOR_RANGE_LIMITED};
  std::string hdr_static_info;
  bool hdr_static_info_in_bitstream{};

  bool operator==(const WorkerVideoPlan &) const = default;
};

[[nodiscard]] bool valid_worker_video_plan(const WorkerVideoPlan &plan) noexcept;
[[nodiscard]] bool selected_video_matches_plan(
    const stream::v1::SelectedVideoMode &selected_video,
    const WorkerVideoPlan &plan) noexcept;
[[nodiscard]] stream::v1::SelectedVideoMode
selected_video_from_plan(const WorkerVideoPlan &plan);

class IVideoPipelineGeneration {
public:
  virtual ~IVideoPipelineGeneration() = default;

  [[nodiscard]] virtual bool
  start(std::uint64_t session_generation,
        std::uint16_t maximum_datagram_bytes) = 0;
  virtual void handle_media_event(const QuicMediaEvent &event) = 0;
  [[nodiscard]] virtual bool request_idr() = 0;
  virtual void stop() noexcept = 0;
};

class IVideoPipelineGenerationFactory {
public:
  virtual ~IVideoPipelineGenerationFactory() = default;

  [[nodiscard]] virtual std::shared_ptr<IVideoPipelineGeneration>
  create(const WorkerVideoPlan &plan) = 0;
};

class IWorkerVideoPipeline {
public:
  virtual ~IWorkerVideoPipeline() = default;

  [[nodiscard]] virtual bool prepare(const WorkerVideoPlan &plan) = 0;
  virtual void handle_media_event(const QuicMediaEvent &event) = 0;
  [[nodiscard]] virtual bool request_idr() = 0;
  virtual void reset() noexcept = 0;
};

class WorkerVideoPipeline final : public IWorkerVideoPipeline {
public:
  explicit WorkerVideoPipeline(IVideoPipelineGenerationFactory &factory);
  ~WorkerVideoPipeline() override;

  WorkerVideoPipeline(const WorkerVideoPipeline &) = delete;
  WorkerVideoPipeline &operator=(const WorkerVideoPipeline &) = delete;

  [[nodiscard]] bool prepare(const WorkerVideoPlan &plan) override;
  void handle_media_event(const QuicMediaEvent &event) override;
  [[nodiscard]] bool request_idr() override;
  void reset() noexcept override;

  [[nodiscard]] bool prepared() const noexcept;
  [[nodiscard]] std::uint64_t active_generation() const noexcept;

private:
  [[nodiscard]] std::shared_ptr<IVideoPipelineGeneration>
  create_started_generation(const WorkerVideoPlan &plan,
                            std::uint64_t session_generation,
                            std::uint16_t maximum_datagram_bytes) noexcept;
  void start_generation(
      const stream::ServerSessionProtocolOutput::AcceptedStartSession &start);
  void stop_generation(std::uint64_t session_generation) noexcept;
  void forward_generation_event(const QuicMediaEvent &event,
                                std::uint64_t session_generation);

  IVideoPipelineGenerationFactory &factory_;
  mutable std::mutex mutex_;
  WorkerVideoPlan plan_;
  std::shared_ptr<IVideoPipelineGeneration> active_;
  std::uint64_t starting_generation_{};
  std::uint64_t active_generation_{};
  bool prepared_{};
};

} // namespace beacon::worker::video
