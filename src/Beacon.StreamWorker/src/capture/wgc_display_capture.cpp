#include "beacon/worker/capture/wgc_display_capture.h"

#include <algorithm>
#include <ranges>
#include <stdexcept>
#include <utility>

namespace beacon::worker::capture {
namespace {

constexpr std::uint32_t nvidia_vendor_id{0x10de};

}  // namespace

const char* wgc_capture_platform_stage_name(
    WgcCapturePlatformStage stage) noexcept {
  switch (stage) {
    case WgcCapturePlatformStage::none:
      return "none";
    case WgcCapturePlatformStage::request_validation:
      return "request-validation";
    case WgcCapturePlatformStage::winrt_apartment:
      return "winrt-apartment";
    case WgcCapturePlatformStage::adapter_lookup:
      return "adapter-lookup";
    case WgcCapturePlatformStage::d3d_device_creation:
      return "d3d-device-create";
    case WgcCapturePlatformStage::winrt_device_creation:
      return "winrt-device-create";
    case WgcCapturePlatformStage::capture_item_creation:
      return "capture-item-create";
    case WgcCapturePlatformStage::capture_item_display_id_runtime:
      return "capture-item-display-id-runtime";
    case WgcCapturePlatformStage::capture_item_display_id_access:
      return "capture-item-display-id-access";
    case WgcCapturePlatformStage::capture_item_display_id_mapping:
      return "capture-item-display-id-mapping";
    case WgcCapturePlatformStage::capture_item_display_id_creation:
      return "capture-item-display-id-create";
    case WgcCapturePlatformStage::capture_item_stale_monitor:
      return "capture-item-stale-monitor";
    case WgcCapturePlatformStage::capture_item_current_monitor_rejected:
      return "capture-item-current-monitor-rejected";
    case WgcCapturePlatformStage::content_size_read:
      return "content-size-read";
    case WgcCapturePlatformStage::frame_pool_creation:
      return "frame-pool-create";
    case WgcCapturePlatformStage::capture_session_creation:
      return "capture-session-create";
    case WgcCapturePlatformStage::frame_event_registration:
      return "frame-event-register";
    case WgcCapturePlatformStage::capture_start:
      return "capture-start";
    case WgcCapturePlatformStage::frame_acquisition:
      return "frame-acquisition";
    case WgcCapturePlatformStage::frame_surface_access:
      return "frame-surface-access";
    case WgcCapturePlatformStage::frame_texture_access:
      return "frame-texture-access";
    case WgcCapturePlatformStage::frame_metadata:
      return "frame-metadata";
  }
  return "unknown";
}

WgcCapturePlatformFailure detail::resolve_capture_item_failure(
    WgcCapturePlatformFailure display_id_failure,
    std::uint32_t fallback_native_code,
    bool fallback_monitor_is_current) noexcept {
  if (display_id_failure.stage != WgcCapturePlatformStage::none) {
    if (display_id_failure.native_code == 0) {
      display_id_failure.native_code = 0x8000FFFFU;
    }
    return display_id_failure;
  }
  return {
      .stage = fallback_monitor_is_current
                   ? WgcCapturePlatformStage::capture_item_current_monitor_rejected
                   : WgcCapturePlatformStage::capture_item_stale_monitor,
      .native_code = fallback_native_code,
  };
}

WgcDisplayCapture::WgcDisplayCapture(
    std::unique_ptr<IWgcCapturePlatform> platform)
    : platform_(std::move(platform)) {
  if (!platform_) {
    throw std::invalid_argument("A WGC capture platform is required.");
  }
}

WgcDisplayCapture::~WgcDisplayCapture() { stop(); }

bool WgcDisplayCapture::start(const WgcCapturePlan& plan,
                              FrameSink sink,
                              FailureSink failure_sink) {
  if (plan.device_name.empty() || !sink) {
    set_failure(WgcCaptureFailure::invalid_plan);
    return false;
  }
  {
    std::lock_guard lock{mutex_};
    if (active_ || platform_started_ || starting_) {
      failure_ = WgcCaptureFailure::capture_start_failed;
      return false;
    }
    starting_ = true;
    stop_requested_ = false;
    start_thread_ = std::this_thread::get_id();
    failure_ = WgcCaptureFailure::none;
    platform_failure_ = {};
    failure_sink_ = std::move(failure_sink);
    failure_reported_ = false;
    pending_frame_.reset();
    consumer_stopping_ = false;
  }

  try {
    const auto targets = platform_->display_targets();
    const auto target = std::ranges::find_if(
        targets, [&plan](const WgcDisplayTargetSnapshot& value) {
          return value.device_name == plan.device_name;
        });
    if (target == targets.end()) {
      return fail_start(WgcCaptureFailure::display_missing, false);
    }
    if (!target->active || target->monitor == 0) {
      return fail_start(WgcCaptureFailure::display_inactive, false);
    }
    const auto adapters = platform_->graphics_adapters();
    const WgcAdapterSnapshot* adapter = nullptr;
    for (const auto& candidate : adapters) {
      if (candidate.vendor_id == nvidia_vendor_id && !candidate.software &&
          (adapter == nullptr || candidate.dedicated_video_memory >
                                     adapter->dedicated_video_memory)) {
        adapter = &candidate;
      }
    }
    if (adapter == nullptr) {
      return fail_start(WgcCaptureFailure::nvidia_adapter_missing, false);
    }

    bool stopped_before_platform_start = false;
    {
      std::lock_guard lock{mutex_};
      stopped_before_platform_start = stop_requested_;
      if (!stopped_before_platform_start) {
        selected_adapter_description_ = adapter->description;
        pool_width_ = target->width;
        pool_height_ = target->height;
        sink_ = std::move(sink);
        active_ = true;
      }
    }
    if (stopped_before_platform_start) {
      return fail_start(WgcCaptureFailure::capture_start_failed, false);
    }
    consumer_thread_ = std::thread([this] { consume_frames(); });
    const bool platform_started = platform_->start_capture(
        *target, *adapter,
        [this](CapturedD3d11Frame frame) {
          receive_frame(std::move(frame));
        },
        [this](WgcCapturePlatformFailure failure) {
          report_async_failure(WgcCaptureFailure::callback_failed, failure);
        });
    if (!platform_started) {
      {
        std::lock_guard lock{mutex_};
        platform_failure_ = platform_->capture_failure();
      }
      return fail_start(WgcCaptureFailure::capture_start_failed, false);
    }

    bool stop_requested = false;
    bool runtime_failure = false;
    WgcCaptureFailure runtime_failure_kind = WgcCaptureFailure::none;
    {
      std::lock_guard lock{mutex_};
      stop_requested = stop_requested_;
      runtime_failure = failure_reported_;
      runtime_failure_kind = failure_;
      if (!stop_requested && !runtime_failure) {
        platform_started_ = true;
        starting_ = false;
        stop_requested_ = false;
        start_thread_ = {};
      }
    }
    if (stop_requested) {
      return fail_start(WgcCaptureFailure::capture_start_failed, true);
    }
    if (runtime_failure) {
      return fail_start(runtime_failure_kind, true);
    }
    start_finished_.notify_all();
    return true;
  } catch (...) {
    return fail_start(WgcCaptureFailure::capture_start_failed, true);
  }
}

void WgcDisplayCapture::stop() noexcept {
  bool stop_platform = false;
  try {
    {
      std::unique_lock lock{mutex_};
      active_ = false;
      sink_ = {};
      failure_sink_ = {};
      pending_frame_.reset();
      consumer_stopping_ = true;
      if (starting_) {
        stop_requested_ = true;
        frame_available_.notify_all();
        if (start_thread_ == std::this_thread::get_id()) {
          return;
        }
        start_finished_.wait(lock, [this] { return !starting_; });
      }
      stop_platform = std::exchange(platform_started_, false);
    }
    frame_available_.notify_all();
    if (stop_platform) {
      platform_->stop_capture();
    }
    {
      std::unique_lock lock{mutex_};
      callbacks_drained_.wait(lock,
                              [this] { return active_callbacks_ == 0; });
    }
    stop_consumer();
  } catch (...) {
  }
}

bool WgcDisplayCapture::fail_start(WgcCaptureFailure failure,
                                   bool stop_platform) noexcept {
  try {
    {
      std::lock_guard lock{mutex_};
      active_ = false;
      sink_ = {};
      failure_sink_ = {};
      pending_frame_.reset();
      consumer_stopping_ = true;
      platform_started_ = false;
      failure_ = failure;
      selected_adapter_description_.clear();
    }
    frame_available_.notify_all();
    if (stop_platform) {
      platform_->stop_capture();
    }
    {
      std::unique_lock lock{mutex_};
      callbacks_drained_.wait(lock,
                              [this] { return active_callbacks_ == 0; });
    }
    stop_consumer();
  } catch (...) {
  }
  {
    std::lock_guard lock{mutex_};
    starting_ = false;
    stop_requested_ = false;
    start_thread_ = {};
  }
  start_finished_.notify_all();
  return false;
}

WgcCaptureFailure WgcDisplayCapture::failure() const noexcept {
  std::lock_guard lock{mutex_};
  return failure_;
}

WgcCapturePlatformFailure WgcDisplayCapture::platform_failure() const noexcept {
  std::lock_guard lock{mutex_};
  return platform_failure_;
}

std::wstring WgcDisplayCapture::selected_adapter_description() const {
  std::lock_guard lock{mutex_};
  return selected_adapter_description_;
}

void WgcDisplayCapture::receive_frame(CapturedD3d11Frame frame) noexcept {
  std::uint32_t pool_width = 0;
  std::uint32_t pool_height = 0;
  {
    std::lock_guard lock{mutex_};
    if (!active_) {
      return;
    }
    ++active_callbacks_;
    pool_width = pool_width_;
    pool_height = pool_height_;
  }

  try {
    if (!frame.texture || frame.width == 0 || frame.height == 0 ||
        frame.qpc_timestamp <= 0) {
      report_async_failure(WgcCaptureFailure::frame_invalid);
    } else if (frame.width != pool_width || frame.height != pool_height) {
      if (platform_->recreate_frame_pool(frame.width, frame.height)) {
        std::lock_guard lock{mutex_};
        pool_width_ = frame.width;
        pool_height_ = frame.height;
      } else {
        report_async_failure(WgcCaptureFailure::frame_pool_recreate_failed);
      }
    } else {
      std::lock_guard lock{mutex_};
      if (active_) {
        pending_frame_ = std::move(frame);
        frame_available_.notify_one();
      }
    }
  } catch (...) {
    report_async_failure(WgcCaptureFailure::callback_failed);
  }

  {
    std::lock_guard lock{mutex_};
    --active_callbacks_;
  }
  callbacks_drained_.notify_all();
}

void WgcDisplayCapture::consume_frames() noexcept {
  for (;;) {
    CapturedD3d11Frame frame;
    FrameSink sink;
    {
      std::unique_lock lock{mutex_};
      frame_available_.wait(lock, [this] {
        return consumer_stopping_ || pending_frame_.has_value();
      });
      if (consumer_stopping_) {
        return;
      }
      frame = std::move(*pending_frame_);
      pending_frame_.reset();
      sink = sink_;
    }
    if (sink) {
      try {
        sink(std::move(frame));
      } catch (...) {
        report_async_failure(WgcCaptureFailure::callback_failed);
      }
    }
  }
}

void WgcDisplayCapture::report_async_failure(
    WgcCaptureFailure failure,
    WgcCapturePlatformFailure platform_failure) noexcept {
  FailureSink failure_sink;
  {
    std::lock_guard lock{mutex_};
    if (failure_reported_ || !active_) {
      return;
    }
    failure_reported_ = true;
    failure_ = failure;
    platform_failure_ = platform_failure;
    active_ = false;
    pending_frame_.reset();
    consumer_stopping_ = true;
    failure_sink = failure_sink_;
  }
  frame_available_.notify_all();
  try {
    if (failure_sink) {
      failure_sink(failure, platform_failure);
    }
  } catch (...) {
  }
}

void WgcDisplayCapture::stop_consumer() noexcept {
  try {
    if (consumer_thread_.joinable() &&
        consumer_thread_.get_id() != std::this_thread::get_id()) {
      consumer_thread_.join();
    }
  } catch (...) {
  }
}

void WgcDisplayCapture::set_failure(WgcCaptureFailure value) noexcept {
  std::lock_guard lock{mutex_};
  failure_ = value;
}

}  // namespace beacon::worker::capture
