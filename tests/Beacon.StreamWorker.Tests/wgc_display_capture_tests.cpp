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
using beacon::worker::capture::WgcCapturePlatformFailure;
using beacon::worker::capture::WgcCapturePlatformStage;
using beacon::worker::capture::WgcCapturePlan;
using beacon::worker::capture::WgcCapturePixelFormat;
using beacon::worker::capture::WgcDisplayCapture;
using beacon::worker::capture::WgcDisplayTargetSnapshot;
using beacon::worker::capture::detail::resolve_capture_item_failure;
using beacon::worker::capture::wgc_capture_platform_stage_name;

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
  WgcCapturePlatformFailure start_failure{};
  bool throw_during_start{};
  bool recreate_result{true};
  int start_count{};
  int recreate_count{};
  int stop_count{};
  std::uint64_t selected_monitor{};
  std::uint64_t selected_adapter{};
  std::uint32_t recreated_width{};
  std::uint32_t recreated_height{};
  WgcCapturePixelFormat selected_format{WgcCapturePixelFormat::bgra8};
  FrameCallback callback;
  FailureCallback failure_callback;
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
      WgcCapturePixelFormat format,
      FrameCallback value,
      FailureCallback failure) override {
    ++start_count;
    selected_monitor = target.monitor;
    selected_adapter = adapter.luid;
    selected_format = format;
    callback = std::move(value);
    failure_callback = std::move(failure);
    if (throw_during_start) {
      throw std::runtime_error{"capture startup failed"};
    }
    if (during_start) {
      during_start();
    }
    return start_result;
  }

  [[nodiscard]] bool recreate_frame_pool(std::uint32_t width,
                                         std::uint32_t height,
                                         WgcCapturePixelFormat format) override {
    ++recreate_count;
    recreated_width = width;
    recreated_height = height;
    selected_format = format;
    return recreate_result;
  }

  [[nodiscard]] WgcCapturePlatformFailure capture_failure() const noexcept override {
    return start_failure;
  }

  void stop_capture() noexcept override {
    ++stop_count;
    callback = {};
    failure_callback = {};
  }

  void emit(std::uint32_t width, std::uint32_t height,
            std::int64_t qpc_timestamp) {
    auto target = callback;
    if (target) {
      target({.texture = std::make_shared<FakeTexture>(),
              .width = width,
              .height = height,
              .qpc_timestamp = qpc_timestamp,
              .pixel_format = selected_format});
    }
  }

  void fail(WgcCapturePlatformFailure failure) {
    auto target = failure_callback;
    if (target) {
      target(failure);
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

void hdr_capture_requests_and_preserves_fp16_scrgb() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  WgcDisplayCapture capture{std::move(platform)};
  CapturedD3d11Frame delivered;
  std::mutex mutex;
  std::condition_variable changed;
  bool received{};
  auto hdr_plan = plan();
  hdr_plan.pixel_format = WgcCapturePixelFormat::rgba16_float;

  BEACON_TEST_REQUIRE(capture.start(
      hdr_plan, [&](CapturedD3d11Frame frame) {
        std::lock_guard lock{mutex};
        delivered = std::move(frame);
        received = true;
        changed.notify_all();
      }));
  BEACON_TEST_REQUIRE(observed->selected_format ==
                      WgcCapturePixelFormat::rgba16_float);
  observed->emit(2560, 1600, 77);
  {
    std::unique_lock lock{mutex};
    changed.wait(lock, [&] { return received; });
  }
  capture.stop();
  BEACON_TEST_REQUIRE(delivered.pixel_format ==
                      WgcCapturePixelFormat::rgba16_float);
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

void platform_start_failure_preserves_stage_and_hresult() {
  auto platform = std::make_unique<FakePlatform>();
  platform->start_result = false;
  platform->start_failure = {
      .stage = WgcCapturePlatformStage::capture_session_creation,
      .native_code = 0x80070005U,
  };
  WgcDisplayCapture capture{std::move(platform)};

  BEACON_TEST_REQUIRE(!capture.start(plan(), [](CapturedD3d11Frame) {}));
  const auto failure = capture.platform_failure();
  BEACON_TEST_REQUIRE(
      failure.stage == WgcCapturePlatformStage::capture_session_creation);
  BEACON_TEST_REQUIRE(failure.native_code == 0x80070005U);
}

void capture_item_failure_stage_names_distinguish_stale_monitor_handles() {
  BEACON_TEST_REQUIRE(std::string{wgc_capture_platform_stage_name(
                          WgcCapturePlatformStage::capture_item_display_id_runtime)} ==
                      "capture-item-display-id-runtime");
  BEACON_TEST_REQUIRE(std::string{wgc_capture_platform_stage_name(
                          WgcCapturePlatformStage::capture_item_display_id_access)} ==
                      "capture-item-display-id-access");
  BEACON_TEST_REQUIRE(std::string{wgc_capture_platform_stage_name(
                          WgcCapturePlatformStage::capture_item_display_id_mapping)} ==
                      "capture-item-display-id-mapping");
  BEACON_TEST_REQUIRE(std::string{wgc_capture_platform_stage_name(
                          WgcCapturePlatformStage::capture_item_display_id_creation)} ==
                      "capture-item-display-id-create");
  BEACON_TEST_REQUIRE(std::string{wgc_capture_platform_stage_name(
                          WgcCapturePlatformStage::capture_item_stale_monitor)} ==
                      "capture-item-stale-monitor");
  BEACON_TEST_REQUIRE(std::string{wgc_capture_platform_stage_name(
                          WgcCapturePlatformStage::capture_item_current_monitor_rejected)} ==
                      "capture-item-current-monitor-rejected");
}

void display_id_failure_is_not_hidden_by_monitor_fallback_failure() {
  const auto display_id_failure = resolve_capture_item_failure(
      {.stage = WgcCapturePlatformStage::capture_item_display_id_creation,
       .native_code = 0x80070057U},
      0x80004005U, true);
  BEACON_TEST_REQUIRE(
      display_id_failure.stage ==
      WgcCapturePlatformStage::capture_item_display_id_creation);
  BEACON_TEST_REQUIRE(display_id_failure.native_code == 0x80070057U);

  const auto missing_result = resolve_capture_item_failure(
      {.stage = WgcCapturePlatformStage::capture_item_display_id_creation,
       .native_code = 0},
      0x80070057U, true);
  BEACON_TEST_REQUIRE(
      missing_result.stage ==
      WgcCapturePlatformStage::capture_item_display_id_creation);
  BEACON_TEST_REQUIRE(missing_result.native_code == 0x8000FFFFU);

  const auto fallback_failure = resolve_capture_item_failure(
      {}, 0x80070057U, false);
  BEACON_TEST_REQUIRE(fallback_failure.stage ==
                      WgcCapturePlatformStage::capture_item_stale_monitor);
  BEACON_TEST_REQUIRE(fallback_failure.native_code == 0x80070057U);
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

void asynchronous_platform_failure_is_reported_once() {
  auto platform = std::make_unique<FakePlatform>();
  auto* observed = platform.get();
  std::vector<std::pair<WgcCaptureFailure, WgcCapturePlatformFailure>> failures;
  WgcDisplayCapture capture{std::move(platform)};
  BEACON_TEST_REQUIRE(capture.start(
      plan(), [](CapturedD3d11Frame) {},
      [&](WgcCaptureFailure failure, WgcCapturePlatformFailure platform_failure) {
        failures.emplace_back(failure, platform_failure);
      }));

  const WgcCapturePlatformFailure platform_failure{
      .stage = WgcCapturePlatformStage::frame_texture_access,
      .native_code = 0x887A0005U,
  };
  observed->fail(platform_failure);
  observed->fail(platform_failure);

  BEACON_TEST_REQUIRE(failures.size() == 1);
  BEACON_TEST_REQUIRE(failures[0].first == WgcCaptureFailure::callback_failed);
  BEACON_TEST_REQUIRE(failures[0].second.stage ==
                      WgcCapturePlatformStage::frame_texture_access);
  BEACON_TEST_REQUIRE(failures[0].second.native_code == 0x887A0005U);
  BEACON_TEST_REQUIRE(capture.failure() == WgcCaptureFailure::callback_failed);
  BEACON_TEST_REQUIRE(capture.platform_failure().native_code == 0x887A0005U);
  capture.stop();
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
    hdr_capture_requests_and_preserves_fp16_scrgb();
    missing_and_inactive_targets_fail_before_capture();
    missing_nvidia_adapter_and_platform_start_failure_are_truthful();
    platform_start_failure_preserves_stage_and_hresult();
    capture_item_failure_stage_names_distinguish_stale_monitor_handles();
    display_id_failure_is_not_hidden_by_monitor_fallback_failure();
    throwing_platform_start_fails_transactionally_and_allows_retry();
    stop_requested_during_platform_start_wins_and_allows_retry();
    frame_callback_preserves_qpc_and_recreates_on_content_size_change();
    frame_sink_never_runs_on_the_platform_callback_thread();
    asynchronous_platform_failure_is_reported_once();
    stop_waits_for_inflight_callback_and_releases_once();
  });
}
