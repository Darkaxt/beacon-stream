#include "beacon/worker/capture/wgc_display_capture.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace {

using beacon::worker::capture::CapturedD3d11Frame;
using beacon::worker::capture::D3d11Texture;
using beacon::worker::capture::IWgcCapturePlatform;
using beacon::worker::capture::WgcAdapterSnapshot;
using beacon::worker::capture::WgcCaptureFailure;
using beacon::worker::capture::WgcCapturePlan;
using beacon::worker::capture::WgcDisplayCapture;
using beacon::worker::capture::WgcDisplayTargetSnapshot;

class FakeTexture final : public D3d11Texture {
 public:
  [[nodiscard]] void* native_texture() const noexcept override { return nullptr; }
};

class FakePlatform final : public IWgcCapturePlatform {
 public:
  std::vector<WgcDisplayTargetSnapshot> targets{
      {.device_name = L"\\\\.\\DISPLAY1",
       .monitor = 1,
       .active = true,
       .width = 2560,
       .height = 1600}};
  std::vector<WgcAdapterSnapshot> adapters{
      {.luid = 1,
       .vendor_id = 0x8086,
       .software = false,
       .description = L"Intel UHD"},
      {.luid = 2,
       .vendor_id = 0x10de,
       .software = false,
       .description = L"NVIDIA RTX"}};
  bool start_result{true};
  bool throw_during_start{};
  bool recreate_result{true};
  int start_count{};
  int recreate_count{};
  int stop_count{};
  std::uint64_t selected_monitor{};
  std::uint64_t selected_adapter{};
  std::uint32_t recreated_width{};
  std::uint32_t recreated_height{};
  FrameCallback callback;
  std::function<void()> during_start;

  [[nodiscard]] std::vector<WgcDisplayTargetSnapshot>
  display_targets() override {
    return targets;
  }

  [[nodiscard]] std::vector<WgcAdapterSnapshot> graphics_adapters() override {
    return adapters;
  }

  [[nodiscard]] bool start_capture(
      const WgcDisplayTargetSnapshot& target,
      const WgcAdapterSnapshot& adapter,
      FrameCallback value) override {
    ++start_count;
    selected_monitor = target.monitor;
    selected_adapter = adapter.luid;
    callback = std::move(value);
    if (throw_during_start) {
      throw std::runtime_error{"capture startup failed"};
    }
    if (during_start) {
      during_start();
    }
    return start_result;
  }

  [[nodiscard]] bool recreate_frame_pool(std::uint32_t width,
                                         std::uint32_t height) override {
    ++recreate_count;
    recreated_width = width;
    recreated_height = height;
    return recreate_result;
  }

  void stop_capture() noexcept override {
    ++stop_count;
    callback = {};
  }

  void emit(std::uint32_t width, std::uint32_t height,
            std::int64_t qpc_timestamp) {
    auto target = callback;
    if (target) {
      target({.texture = std::make_shared<FakeTexture>(),
              .width = width,
              .height = height,
              .qpc_timestamp = qpc_timestamp});
    }
  }
};

WgcCapturePlan plan() {
  return {.device_name = L"\\\\.\\DISPLAY1"};
}

void exact_active_display_and_nvidia_adapter_are_selected() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  WgcDisplayCapture capture{std::move(platform)};

  BEACON_TEST_REQUIRE(capture.start(plan(), [](CapturedD3d11Frame) {}));
  BEACON_TEST_REQUIRE(capture.failure() == WgcCaptureFailure::none);
  BEACON_TEST_REQUIRE(observed->start_count == 1);
  BEACON_TEST_REQUIRE(observed->selected_monitor == 1);
  BEACON_TEST_REQUIRE(observed->selected_adapter == 2);
  BEACON_TEST_REQUIRE(capture.selected_adapter_description() == L"NVIDIA RTX");
}

void missing_and_inactive_targets_fail_before_capture() {
  for (const auto failure : {WgcCaptureFailure::display_missing,
                             WgcCaptureFailure::display_inactive}) {
    auto platform = std::make_unique<FakePlatform>();
    auto* observed = platform.get();
    if (failure == WgcCaptureFailure::display_missing) {
      platform->targets.clear();
    } else if (failure == WgcCaptureFailure::display_inactive) {
      platform->targets[0].active = false;
    }
    WgcDisplayCapture capture{std::move(platform)};

    BEACON_TEST_REQUIRE(!capture.start(plan(), [](CapturedD3d11Frame) {}));
    BEACON_TEST_REQUIRE(capture.failure() == failure);
    BEACON_TEST_REQUIRE(observed->start_count == 0);
  }
}

void missing_nvidia_adapter_and_platform_start_failure_are_truthful() {
  {
    auto platform = std::make_unique<FakePlatform>();
    auto* observed = platform.get();
    platform->adapters.erase(platform->adapters.begin() + 1);
    WgcDisplayCapture capture{std::move(platform)};
    BEACON_TEST_REQUIRE(!capture.start(plan(), [](CapturedD3d11Frame) {}));
    BEACON_TEST_REQUIRE(capture.failure() == WgcCaptureFailure::nvidia_adapter_missing);
    BEACON_TEST_REQUIRE(observed->start_count == 0);
  }
  {
    auto platform = std::make_unique<FakePlatform>();
    platform->start_result = false;
    WgcDisplayCapture capture{std::move(platform)};
    BEACON_TEST_REQUIRE(!capture.start(plan(), [](CapturedD3d11Frame) {}));
    BEACON_TEST_REQUIRE(capture.failure() == WgcCaptureFailure::capture_start_failed);
  }
}

void throwing_platform_start_fails_transactionally_and_allows_retry() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  platform->throw_during_start = true;
  WgcDisplayCapture capture{std::move(platform)};

  bool escaped = false;
  try {
    BEACON_TEST_REQUIRE(!capture.start(plan(), [](CapturedD3d11Frame) {}));
  } catch (...) {
    escaped = true;
  }

  BEACON_TEST_REQUIRE(!escaped);
  BEACON_TEST_REQUIRE(capture.failure() ==
                      WgcCaptureFailure::capture_start_failed);
  observed->throw_during_start = false;
  BEACON_TEST_REQUIRE(capture.start(plan(), [](CapturedD3d11Frame) {}));
  capture.stop();
  BEACON_TEST_REQUIRE(observed->stop_count == 2);
}

void stop_requested_during_platform_start_wins_and_allows_retry() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  WgcDisplayCapture capture{std::move(platform)};
  observed->during_start = [&] { capture.stop(); };

  BEACON_TEST_REQUIRE(!capture.start(plan(), [](CapturedD3d11Frame) {}));
  BEACON_TEST_REQUIRE(capture.failure() ==
                      WgcCaptureFailure::capture_start_failed);
  BEACON_TEST_REQUIRE(observed->stop_count == 1);

  observed->during_start = {};
  BEACON_TEST_REQUIRE(capture.start(plan(), [](CapturedD3d11Frame) {}));
  capture.stop();
  BEACON_TEST_REQUIRE(observed->stop_count == 2);
}

void frame_callback_preserves_qpc_and_recreates_on_content_size_change() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  std::vector<CapturedD3d11Frame> frames;
  std::mutex frames_mutex;
  std::condition_variable frames_changed;
  WgcDisplayCapture capture{std::move(platform)};
  BEACON_TEST_REQUIRE(capture.start(
      plan(), [&](CapturedD3d11Frame frame) {
        std::lock_guard lock{frames_mutex};
        frames.push_back(std::move(frame));
        frames_changed.notify_all();
      }));

  observed->emit(2560, 1600, 42);
  {
    std::unique_lock lock{frames_mutex};
    frames_changed.wait(lock, [&] { return frames.size() == 1; });
  }
  observed->emit(1920, 1200, 43);
  observed->emit(1920, 1200, 44);
  {
    std::unique_lock lock{frames_mutex};
    frames_changed.wait(lock, [&] { return frames.size() == 2; });
  }

  {
    std::lock_guard lock{frames_mutex};
    BEACON_TEST_REQUIRE(frames[0].qpc_timestamp == 42);
    BEACON_TEST_REQUIRE(frames[0].texture != nullptr);
    BEACON_TEST_REQUIRE(frames[1].qpc_timestamp == 44);
  }
  BEACON_TEST_REQUIRE(observed->recreate_count == 1);
  BEACON_TEST_REQUIRE(observed->recreated_width == 1920);
  BEACON_TEST_REQUIRE(observed->recreated_height == 1200);
}

void frame_sink_never_runs_on_the_platform_callback_thread() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  std::mutex mutex;
  std::condition_variable changed;
  std::thread::id producer_thread;
  std::thread::id sink_thread;
  WgcDisplayCapture capture{std::move(platform)};
  BEACON_TEST_REQUIRE(capture.start(plan(), [&](CapturedD3d11Frame) {
    std::lock_guard lock{mutex};
    sink_thread = std::this_thread::get_id();
    changed.notify_all();
  }));

  std::thread producer([&] {
    producer_thread = std::this_thread::get_id();
    observed->emit(2560, 1600, 77);
  });
  producer.join();
  {
    std::unique_lock lock{mutex};
    changed.wait(lock, [&] { return sink_thread != std::thread::id{}; });
  }

  BEACON_TEST_REQUIRE(sink_thread != producer_thread);
}

void stop_waits_for_inflight_callback_and_releases_once() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  std::mutex mutex;
  std::condition_variable changed;
  bool callback_entered = false;
  bool release_callback = false;
  bool stop_returned = false;
  WgcDisplayCapture capture{std::move(platform)};
  BEACON_TEST_REQUIRE(capture.start(plan(), [&](CapturedD3d11Frame) {
    std::unique_lock lock{mutex};
    callback_entered = true;
    changed.notify_all();
    changed.wait(lock, [&] { return release_callback; });
  }));

  std::thread producer([&] { observed->emit(2560, 1600, 55); });
  {
    std::unique_lock lock{mutex};
    changed.wait(lock, [&] { return callback_entered; });
  }
  std::thread stopper([&] {
    capture.stop();
    std::lock_guard lock{mutex};
    stop_returned = true;
    changed.notify_all();
  });
  {
    std::lock_guard lock{mutex};
    BEACON_TEST_REQUIRE(!stop_returned);
    release_callback = true;
    changed.notify_all();
  }
  producer.join();
  stopper.join();

  capture.stop();
  BEACON_TEST_REQUIRE(stop_returned);
  BEACON_TEST_REQUIRE(observed->stop_count == 1);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    exact_active_display_and_nvidia_adapter_are_selected();
    missing_and_inactive_targets_fail_before_capture();
    missing_nvidia_adapter_and_platform_start_failure_are_truthful();
    throwing_platform_start_fails_transactionally_and_allows_retry();
    stop_requested_during_platform_start_wins_and_allows_retry();
    frame_callback_preserves_qpc_and_recreates_on_content_size_change();
    frame_sink_never_runs_on_the_platform_callback_thread();
    stop_waits_for_inflight_callback_and_releases_once();
  });
}
