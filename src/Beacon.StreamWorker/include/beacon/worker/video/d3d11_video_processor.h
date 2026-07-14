#pragma once

#include "beacon/worker/capture/wgc_display_capture.h"

#include <cstdint>
#include <memory>
#include <optional>
#include <vector>

namespace beacon::worker::video {

enum class VideoPixelFormat {
  bgra8,
  nv12,
};

enum class VideoRange {
  full,
  limited,
};

enum class VideoColorMatrix {
  bt709,
};

struct VideoRectangle {
  std::uint32_t left{};
  std::uint32_t top{};
  std::uint32_t right{};
  std::uint32_t bottom{};

  bool operator==(const VideoRectangle&) const = default;
};

struct D3d11VideoProcessorLayout {
  VideoRectangle source;
  VideoRectangle destination;
};

struct D3d11VideoProcessorPlan {
  std::uint32_t output_width{};
  std::uint32_t output_height{};
  std::uint32_t frame_rate_numerator{};
  std::uint32_t frame_rate_denominator{};
};

struct D3d11VideoProcessorConfiguration {
  std::uint32_t input_width{};
  std::uint32_t input_height{};
  std::uint32_t output_width{};
  std::uint32_t output_height{};
  std::uint32_t frame_rate_numerator{};
  std::uint32_t frame_rate_denominator{};
  VideoPixelFormat input_format{VideoPixelFormat::bgra8};
  VideoPixelFormat output_format{VideoPixelFormat::nv12};
  VideoRange input_range{VideoRange::full};
  VideoRange output_range{VideoRange::limited};
  VideoColorMatrix matrix{VideoColorMatrix::bt709};

  bool operator==(const D3d11VideoProcessorConfiguration&) const = default;
};

enum class D3d11VideoProcessorFailure {
  none,
  invalid_frame,
  invalid_plan,
  device_unavailable,
  unsupported_format,
  unsupported_color_conversion,
  processor_creation_failed,
  output_creation_failed,
  output_pool_exhausted,
  blit_failed,
  device_lost,
};

struct ConvertedD3d11Frame {
  std::shared_ptr<capture::D3d11Texture> texture;
  std::uint32_t width{};
  std::uint32_t height{};
  std::int64_t qpc_timestamp{};
  VideoPixelFormat format{VideoPixelFormat::nv12};
  VideoRange range{VideoRange::limited};
  VideoColorMatrix matrix{VideoColorMatrix::bt709};
};

class ID3d11VideoProcessorPlatform {
 public:
  virtual ~ID3d11VideoProcessorPlatform() = default;

  [[nodiscard]] virtual void* device_identity(
      const capture::D3d11Texture& input) noexcept = 0;
  [[nodiscard]] virtual D3d11VideoProcessorFailure configure(
      const capture::D3d11Texture& input,
      const D3d11VideoProcessorConfiguration& configuration) noexcept = 0;
  [[nodiscard]] virtual std::shared_ptr<capture::D3d11Texture>
  create_output_texture() noexcept = 0;
  [[nodiscard]] virtual D3d11VideoProcessorFailure blit(
      const capture::D3d11Texture& input, capture::D3d11Texture& output,
      const D3d11VideoProcessorLayout& layout) noexcept = 0;
  virtual void reset() noexcept = 0;
};

class D3d11VideoProcessor final {
 public:
  explicit D3d11VideoProcessor(
      std::unique_ptr<ID3d11VideoProcessorPlatform> platform);
  ~D3d11VideoProcessor();

  D3d11VideoProcessor(const D3d11VideoProcessor&) = delete;
  D3d11VideoProcessor& operator=(const D3d11VideoProcessor&) = delete;

  [[nodiscard]] std::optional<ConvertedD3d11Frame> convert(
      const capture::CapturedD3d11Frame& input,
      const D3d11VideoProcessorPlan& plan) noexcept;
  [[nodiscard]] D3d11VideoProcessorFailure failure() const noexcept;

 private:
  struct CachedConfiguration {
    void* device{};
    D3d11VideoProcessorConfiguration configuration;

    bool operator==(const CachedConfiguration&) const = default;
  };

  void discard_gpu_state() noexcept;

  static constexpr std::size_t output_pool_capacity = 3;
  std::unique_ptr<ID3d11VideoProcessorPlatform> platform_;
  std::optional<CachedConfiguration> cached_;
  std::vector<std::shared_ptr<capture::D3d11Texture>> output_pool_;
  D3d11VideoProcessorFailure failure_{D3d11VideoProcessorFailure::none};
};

[[nodiscard]] std::optional<D3d11VideoProcessorLayout>
calculate_video_processor_layout(std::uint32_t input_width,
                                 std::uint32_t input_height,
                                 std::uint32_t output_width,
                                 std::uint32_t output_height) noexcept;

[[nodiscard]] std::unique_ptr<ID3d11VideoProcessorPlatform>
create_windows_d3d11_video_processor_platform();

}  // namespace beacon::worker::video
