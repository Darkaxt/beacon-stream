#include "beacon/worker/capture/wgc_display_capture.h"

#include <algorithm>
#include <ranges>
#include <stdexcept>
#include <utility>

namespace beacon::worker::capture {
namespace {

constexpr std::uint32_t nvidia_vendor_id{0x10de};

}  // namespace

WgcDisplayCapture::WgcDisplayCapture(
    std::unique_ptr<IWgcCapturePlatform> platform)
    : platform_(std::move(platform)) {
  if (!platform_) {
    throw std::invalid_argument("A WGC capture platform is required.");
  }
}

WgcDisplayCapture::~WgcDisplayCapture() { stop(); }

bool WgcDisplayCapture::start(const WgcCapturePlan& plan, FrameSink sink) {
  if (plan.device_name.empty() || plan.width == 0 || plan.height == 0 ||
      !sink) {
    set_failure(WgcCaptureFailure::invalid_plan);
    return false;
  }
  {
    std::lock_guard lock{mutex_};
    if (active_ || platform_started_) {
      failure_ = WgcCaptureFailure::capture_start_failed;
      return false;
    }
  }

  const auto targets = platform_->display_targets();
  const auto target = std::ranges::find_if(
      targets, [&plan](const WgcDisplayTargetSnapshot& value) {
        return value.device_name == plan.device_name;
      });
  if (target == targets.end()) {
    set_failure(WgcCaptureFailure::display_missing);
    return false;
  }
  if (!target->active || target->monitor == 0) {
    set_failure(WgcCaptureFailure::display_inactive);
    return false;
  }
  if (target->width != plan.width || target->height != plan.height) {
    set_failure(WgcCaptureFailure::display_mode_mismatch);
    return false;
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
    set_failure(WgcCaptureFailure::nvidia_adapter_missing);
    return false;
  }

  {
    std::lock_guard lock{mutex_};
    failure_ = WgcCaptureFailure::none;
    selected_adapter_description_ = adapter->description;
    pool_width_ = plan.width;
    pool_height_ = plan.height;
    sink_ = std::move(sink);
    active_ = true;
    consumer_stopping_ = false;
  }
  consumer_thread_ = std::thread([this] { consume_frames(); });
  if (!platform_->start_capture(
          *target, *adapter,
          [this](CapturedD3d11Frame frame) {
            receive_frame(std::move(frame));
          })) {
    {
      std::lock_guard lock{mutex_};
      active_ = false;
      sink_ = {};
      failure_ = WgcCaptureFailure::capture_start_failed;
      selected_adapter_description_.clear();
      consumer_stopping_ = true;
    }
    frame_available_.notify_all();
    stop_consumer();
    return false;
  }
  {
    std::lock_guard lock{mutex_};
    platform_started_ = true;
  }
  return true;
}

void WgcDisplayCapture::stop() noexcept {
  bool stop_platform = false;
  try {
    {
      std::lock_guard lock{mutex_};
      active_ = false;
      sink_ = {};
      pending_frame_.reset();
      consumer_stopping_ = true;
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

WgcCaptureFailure WgcDisplayCapture::failure() const noexcept {
  std::lock_guard lock{mutex_};
  return failure_;
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
      set_failure(WgcCaptureFailure::frame_invalid);
    } else if (frame.width != pool_width || frame.height != pool_height) {
      if (platform_->recreate_frame_pool(frame.width, frame.height)) {
        std::lock_guard lock{mutex_};
        pool_width_ = frame.width;
        pool_height_ = frame.height;
      } else {
        set_failure(WgcCaptureFailure::frame_pool_recreate_failed);
      }
    } else {
      std::lock_guard lock{mutex_};
      if (active_) {
        pending_frame_ = std::move(frame);
        frame_available_.notify_one();
      }
    }
  } catch (...) {
    set_failure(WgcCaptureFailure::callback_failed);
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
        set_failure(WgcCaptureFailure::callback_failed);
      }
    }
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
