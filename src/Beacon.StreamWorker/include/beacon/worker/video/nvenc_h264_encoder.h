#pragma once

#include "beacon/worker/video/d3d11_video_processor.h"

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <vector>

namespace beacon::worker::video {

enum class NvencH264Failure {
  none,
  invalid_plan,
  invalid_frame,
  timestamp_not_monotonic,
  device_unavailable,
  device_lost,
  runtime_unavailable,
  api_unavailable,
  api_incompatible,
  session_unavailable,
  session_open_failed,
  h264_unsupported,
  nv12_unsupported,
  dimensions_unsupported,
  bitrate_reconfiguration_unsupported,
  preset_unavailable,
  initialization_failed,
  bitstream_creation_failed,
  input_registration_failed,
  input_mapping_failed,
  encode_failed,
  bitstream_lock_failed,
  bitstream_unlock_failed,
  input_unmapping_failed,
  input_unregistration_failed,
  reconfigure_failed,
  invalid_bitstream,
};

struct NvencH264Plan {
  std::uint32_t width{};
  std::uint32_t height{};
  std::uint32_t frame_rate_numerator{};
  std::uint32_t frame_rate_denominator{};
  std::uint32_t bitrate_bps{};
};

struct NvencH264Configuration {
  std::uint32_t width{};
  std::uint32_t height{};
  std::uint32_t frame_rate_numerator{};
  std::uint32_t frame_rate_denominator{};
  std::uint32_t bitrate_bps{};
  VideoPixelFormat input_format{VideoPixelFormat::nv12};
  VideoRange input_range{VideoRange::limited};
  VideoColorMatrix matrix{VideoColorMatrix::bt709};
  bool low_latency{true};
  std::uint32_t b_frame_count{};
  bool repeat_parameter_sets{true};
};

struct NvencH264NativeContract {
  std::uint32_t api_version{};
  std::uint32_t device_type{};
  std::uint32_t buffer_format{};
  std::uint32_t tuning_info{};
  std::uint32_t rate_control_mode{};
  std::int32_t frame_interval_p{};
  std::uint32_t gop_length{};
  bool repeat_sps_pps{};
  bool enable_encode_async{};
  bool enable_picture_type_decision{};
  std::uint32_t first_frame_flags{};
};

struct NvencH264ApiCapabilities {
  bool h264{};
  bool nv12{};
  bool dynamic_bitrate{};
  std::uint32_t max_width{};
  std::uint32_t max_height{};
};

struct NvencH264ApiHandleResult {
  std::uintptr_t handle{};
  NvencH264Failure failure{NvencH264Failure::none};
};

struct NvencH264LockedBitstream {
  const std::uint8_t* data{};
  std::size_t size{};
  std::int64_t qpc_timestamp{};
};

struct NvencH264ApiLockResult {
  NvencH264LockedBitstream bitstream;
  NvencH264Failure failure{NvencH264Failure::none};
};

struct NvencH264Submit {
  std::uintptr_t mapped_input{};
  std::uintptr_t output_bitstream{};
  std::int64_t qpc_timestamp{};
  bool force_idr{};
  bool output_parameter_sets{};
};

struct EncodedH264AccessUnit {
  std::vector<std::uint8_t> annex_b;
  std::int64_t qpc_timestamp{};
  bool idr{};
  bool has_sps{};
  bool has_pps{};
};

class INvencH264Api {
 public:
  virtual ~INvencH264Api() = default;

  [[nodiscard]] virtual void* device_identity(
      const capture::D3d11Texture& texture) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure open(
      const capture::D3d11Texture& texture,
      const NvencH264Configuration& configuration) noexcept = 0;
  [[nodiscard]] virtual NvencH264ApiHandleResult
  create_bitstream() noexcept = 0;
  [[nodiscard]] virtual NvencH264ApiHandleResult register_input(
      const capture::D3d11Texture& texture, std::uint32_t width,
      std::uint32_t height) noexcept = 0;
  [[nodiscard]] virtual NvencH264ApiHandleResult map_input(
      std::uintptr_t registered) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure submit(
      const NvencH264Submit& submit) noexcept = 0;
  [[nodiscard]] virtual NvencH264ApiLockResult lock_bitstream(
      std::uintptr_t output_bitstream) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure unlock_bitstream(
      std::uintptr_t output_bitstream) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure unmap_input(
      std::uintptr_t mapped) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure unregister_input(
      std::uintptr_t registered) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure reconfigure_bitrate(
      std::uint32_t bitrate_bps) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure destroy_bitstream(
      std::uintptr_t output_bitstream) noexcept = 0;
  [[nodiscard]] virtual NvencH264Failure destroy_session() noexcept = 0;
  virtual void unload() noexcept = 0;
};

class NvencH264Encoder final {
 public:
  NvencH264Encoder(std::unique_ptr<INvencH264Api> api,
                   NvencH264Plan plan);
  ~NvencH264Encoder();

  NvencH264Encoder(const NvencH264Encoder&) = delete;
  NvencH264Encoder& operator=(const NvencH264Encoder&) = delete;

  [[nodiscard]] std::optional<EncodedH264AccessUnit> encode(
      const ConvertedD3d11Frame& frame, bool force_idr = false) noexcept;
  [[nodiscard]] bool reconfigure_bitrate(
      std::uint32_t bitrate_bps) noexcept;
  [[nodiscard]] NvencH264Failure failure() const noexcept;

 private:
  [[nodiscard]] bool valid_plan() const noexcept;
  [[nodiscard]] bool valid_frame(
      const ConvertedD3d11Frame& frame) const noexcept;
  [[nodiscard]] bool ensure_session(
      const ConvertedD3d11Frame& frame, void* device) noexcept;
  [[nodiscard]] std::optional<std::uintptr_t> registered_input(
      const ConvertedD3d11Frame& frame) noexcept;
  [[nodiscard]] NvencH264Failure release_input(
      std::uintptr_t mapped, std::uintptr_t registered) noexcept;
  void shutdown_session() noexcept;

  std::unique_ptr<INvencH264Api> api_;
  NvencH264Plan plan_;
  void* device_{};
  std::uintptr_t output_bitstream_{};
  std::optional<std::int64_t> last_timestamp_;
  bool session_open_{};
  bool first_frame_{true};
  NvencH264Failure failure_{NvencH264Failure::none};
};

[[nodiscard]] NvencH264NativeContract
nvenc_h264_native_contract() noexcept;

[[nodiscard]] NvencH264Failure classify_nvenc_runtime_preflight(
    bool runtime_loaded, bool entry_points_available,
    std::uint32_t max_supported_api_version) noexcept;

[[nodiscard]] NvencH264Failure validate_nvenc_h264_capabilities(
    const NvencH264ApiCapabilities& capabilities,
    const NvencH264Plan& plan) noexcept;

[[nodiscard]] std::unique_ptr<INvencH264Api>
create_windows_nvenc_h264_api();

}  // namespace beacon::worker::video
