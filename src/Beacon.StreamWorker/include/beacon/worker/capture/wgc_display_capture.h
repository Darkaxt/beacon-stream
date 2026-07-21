#pragma once

#include <condition_variable>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <vector>

namespace beacon::worker::capture {

class D3d11Texture {
 public:
  virtual ~D3d11Texture() = default;
  [[nodiscard]] virtual void* native_texture() const noexcept = 0;
};

struct CapturedD3d11Frame {
  std::shared_ptr<D3d11Texture> texture;
  std::uint32_t width{};
  std::uint32_t height{};
  std::int64_t qpc_timestamp{};
};

struct WgcDisplayTargetSnapshot {
  std::wstring device_name;
  std::uint64_t monitor{};
  bool active{};
  std::uint32_t width{};
  std::uint32_t height{};
};

struct WgcAdapterSnapshot {
  std::uint64_t luid{};
  std::uint32_t vendor_id{};
  bool software{};
  std::uint64_t dedicated_video_memory{};
  std::wstring description;
};

struct WgcCapturePlan {
  std::wstring device_name;
};

enum class WgcCaptureFailure {
  none,
  invalid_plan,
  display_missing,
  display_inactive,
  display_mode_mismatch,
  nvidia_adapter_missing,
  capture_start_failed,
  frame_invalid,
  frame_pool_recreate_failed,
  callback_failed,
};

enum class WgcCapturePlatformStage {
  none,
  request_validation,
  winrt_apartment,
  adapter_lookup,
  d3d_device_creation,
  winrt_device_creation,
  capture_item_creation,
  capture_item_display_id_runtime,
  capture_item_display_id_access,
  capture_item_display_id_mapping,
  capture_item_display_id_creation,
  capture_item_stale_monitor,
  capture_item_current_monitor_rejected,
  content_size_read,
  frame_pool_creation,
  capture_session_creation,
  frame_event_registration,
  capture_start,
  frame_acquisition,
  frame_surface_access,
  frame_texture_access,
  frame_metadata,
};

struct WgcCapturePlatformFailure {
  WgcCapturePlatformStage stage{WgcCapturePlatformStage::none};
  std::uint32_t native_code{};
};

[[nodiscard]] const char* wgc_capture_platform_stage_name(
    WgcCapturePlatformStage stage) noexcept;

namespace detail {

[[nodiscard]] WgcCapturePlatformFailure resolve_capture_item_failure(
    WgcCapturePlatformFailure display_id_failure,
    std::uint32_t fallback_native_code,
    bool fallback_monitor_is_current) noexcept;

}  // namespace detail

class IWgcCapturePlatform {
 public:
  using FrameCallback = std::function<void(CapturedD3d11Frame)>;
  using FailureCallback = std::function<void(WgcCapturePlatformFailure)>;

  virtual ~IWgcCapturePlatform() = default;
  [[nodiscard]] virtual std::vector<WgcDisplayTargetSnapshot>
  display_targets() = 0;
  [[nodiscard]] virtual std::vector<WgcAdapterSnapshot>
  graphics_adapters() = 0;
  [[nodiscard]] virtual bool start_capture(
      const WgcDisplayTargetSnapshot& target,
      const WgcAdapterSnapshot& adapter,
      FrameCallback callback,
      FailureCallback failure_callback) = 0;
  [[nodiscard]] virtual bool recreate_frame_pool(std::uint32_t width,
                                                 std::uint32_t height) = 0;
  [[nodiscard]] virtual WgcCapturePlatformFailure
  capture_failure() const noexcept = 0;
  virtual void stop_capture() noexcept = 0;
};

class WgcDisplayCapture final {
 public:
  using FrameSink = std::function<void(CapturedD3d11Frame)>;
  using FailureSink =
      std::function<void(WgcCaptureFailure, WgcCapturePlatformFailure)>;

  explicit WgcDisplayCapture(std::unique_ptr<IWgcCapturePlatform> platform);
  ~WgcDisplayCapture();

  WgcDisplayCapture(const WgcDisplayCapture&) = delete;
  WgcDisplayCapture& operator=(const WgcDisplayCapture&) = delete;

  [[nodiscard]] bool start(const WgcCapturePlan& plan,
                           FrameSink sink,
                           FailureSink failure_sink = {});
  void stop() noexcept;
  [[nodiscard]] WgcCaptureFailure failure() const noexcept;
  [[nodiscard]] WgcCapturePlatformFailure platform_failure() const noexcept;
  [[nodiscard]] std::wstring selected_adapter_description() const;

 private:
  void receive_frame(CapturedD3d11Frame frame) noexcept;
  void consume_frames() noexcept;
  [[nodiscard]] bool fail_start(WgcCaptureFailure failure,
                                bool stop_platform) noexcept;
  void report_async_failure(
      WgcCaptureFailure failure,
      WgcCapturePlatformFailure platform_failure = {}) noexcept;
  void stop_consumer() noexcept;
  void set_failure(WgcCaptureFailure value) noexcept;

  std::unique_ptr<IWgcCapturePlatform> platform_;
  mutable std::mutex mutex_;
  std::condition_variable callbacks_drained_;
  std::condition_variable frame_available_;
  std::condition_variable start_finished_;
  FrameSink sink_;
  FailureSink failure_sink_;
  std::optional<CapturedD3d11Frame> pending_frame_;
  std::thread consumer_thread_;
  WgcCaptureFailure failure_{WgcCaptureFailure::none};
  WgcCapturePlatformFailure platform_failure_{};
  std::wstring selected_adapter_description_;
  std::uint32_t pool_width_{};
  std::uint32_t pool_height_{};
  std::size_t active_callbacks_{};
  bool active_{};
  bool platform_started_{};
  bool consumer_stopping_{};
  bool failure_reported_{};
  bool starting_{};
  bool stop_requested_{};
  std::thread::id start_thread_{};
};

[[nodiscard]] std::unique_ptr<IWgcCapturePlatform>
create_windows_wgc_capture_platform();

}  // namespace beacon::worker::capture
