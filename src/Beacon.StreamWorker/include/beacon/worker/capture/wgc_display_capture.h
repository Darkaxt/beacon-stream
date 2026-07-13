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
  std::uint32_t width{};
  std::uint32_t height{};
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

class IWgcCapturePlatform {
 public:
  using FrameCallback = std::function<void(CapturedD3d11Frame)>;

  virtual ~IWgcCapturePlatform() = default;
  [[nodiscard]] virtual std::vector<WgcDisplayTargetSnapshot>
  display_targets() = 0;
  [[nodiscard]] virtual std::vector<WgcAdapterSnapshot>
  graphics_adapters() = 0;
  [[nodiscard]] virtual bool start_capture(
      const WgcDisplayTargetSnapshot& target,
      const WgcAdapterSnapshot& adapter,
      FrameCallback callback) = 0;
  [[nodiscard]] virtual bool recreate_frame_pool(std::uint32_t width,
                                                 std::uint32_t height) = 0;
  virtual void stop_capture() noexcept = 0;
};

class WgcDisplayCapture final {
 public:
  using FrameSink = std::function<void(CapturedD3d11Frame)>;

  explicit WgcDisplayCapture(std::unique_ptr<IWgcCapturePlatform> platform);
  ~WgcDisplayCapture();

  WgcDisplayCapture(const WgcDisplayCapture&) = delete;
  WgcDisplayCapture& operator=(const WgcDisplayCapture&) = delete;

  [[nodiscard]] bool start(const WgcCapturePlan& plan, FrameSink sink);
  void stop() noexcept;
  [[nodiscard]] WgcCaptureFailure failure() const noexcept;
  [[nodiscard]] std::wstring selected_adapter_description() const;

 private:
  void receive_frame(CapturedD3d11Frame frame) noexcept;
  void consume_frames() noexcept;
  void stop_consumer() noexcept;
  void set_failure(WgcCaptureFailure value) noexcept;

  std::unique_ptr<IWgcCapturePlatform> platform_;
  mutable std::mutex mutex_;
  std::condition_variable callbacks_drained_;
  std::condition_variable frame_available_;
  FrameSink sink_;
  std::optional<CapturedD3d11Frame> pending_frame_;
  std::thread consumer_thread_;
  WgcCaptureFailure failure_{WgcCaptureFailure::none};
  std::wstring selected_adapter_description_;
  std::uint32_t pool_width_{};
  std::uint32_t pool_height_{};
  std::size_t active_callbacks_{};
  bool active_{};
  bool platform_started_{};
  bool consumer_stopping_{};
};

[[nodiscard]] std::unique_ptr<IWgcCapturePlatform>
create_windows_wgc_capture_platform();

}  // namespace beacon::worker::capture
