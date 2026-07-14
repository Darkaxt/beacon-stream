#include "beacon/stream/media_datagram.h"
#include "beacon/worker/video/production_video_generation.h"
#include "beacon/worker/video/production_video_capabilities.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <utility>
#include <vector>

namespace {

namespace capture = beacon::worker::capture;
namespace stream = beacon::stream;
namespace video = beacon::worker::video;

class FakeTexture final : public capture::D3d11Texture {
public:
  explicit FakeTexture(void *identity) : identity_(identity) {}
  [[nodiscard]] void *native_texture() const noexcept override {
    return identity_;
  }

private:
  void *identity_{};
};

struct CaptureTrace {
  std::size_t starts{};
  std::size_t stops{};
  std::wstring device_name;
};

class FakeCapturePlatform final : public capture::IWgcCapturePlatform {
public:
  explicit FakeCapturePlatform(std::shared_ptr<CaptureTrace> trace)
      : trace_(std::move(trace)) {}

  [[nodiscard]] std::vector<capture::WgcDisplayTargetSnapshot>
  display_targets() override {
    return {{.device_name = L"\\\\.\\DISPLAY7",
             .monitor = 7,
             .active = true,
             .width = 2560,
             .height = 1600}};
  }

  [[nodiscard]] std::vector<capture::WgcAdapterSnapshot>
  graphics_adapters() override {
    return {{.luid = 9,
             .vendor_id = 0x10de,
             .software = false,
             .dedicated_video_memory = 8'000'000'000,
             .description = L"NVIDIA Test Adapter"}};
  }

  [[nodiscard]] bool start_capture(
      const capture::WgcDisplayTargetSnapshot &target,
      const capture::WgcAdapterSnapshot &,
      FrameCallback callback) override {
    ++trace_->starts;
    trace_->device_name = target.device_name;
    callback_ = std::move(callback);
    return true;
  }

  [[nodiscard]] bool recreate_frame_pool(std::uint32_t,
                                         std::uint32_t) override {
    return true;
  }

  void stop_capture() noexcept override {
    ++trace_->stops;
    callback_ = {};
  }

  void emit(std::int64_t timestamp) {
    auto callback = callback_;
    BEACON_TEST_REQUIRE(static_cast<bool>(callback));
    callback({
        .texture = std::make_shared<FakeTexture>(
            reinterpret_cast<void *>(0x1000)),
        .width = 2560,
        .height = 1600,
        .qpc_timestamp = timestamp,
    });
  }

private:
  std::shared_ptr<CaptureTrace> trace_;
  FrameCallback callback_;
};

struct ProcessorTrace {
  std::size_t blits{};
  video::D3d11VideoProcessorFailure blit_result{
      video::D3d11VideoProcessorFailure::none};
};

class FakeProcessorPlatform final : public video::ID3d11VideoProcessorPlatform {
public:
  explicit FakeProcessorPlatform(std::shared_ptr<ProcessorTrace> trace)
      : trace_(std::move(trace)) {}

  [[nodiscard]] void *
  device_identity(const capture::D3d11Texture &) noexcept override {
    return reinterpret_cast<void *>(0x2000);
  }

  [[nodiscard]] video::D3d11VideoProcessorFailure configure(
      const capture::D3d11Texture &,
      const video::D3d11VideoProcessorConfiguration &) noexcept override {
    return video::D3d11VideoProcessorFailure::none;
  }

  [[nodiscard]] video::D3d11VideoProcessorTextureResult
  create_output_texture() noexcept override {
    return {.texture = std::make_shared<FakeTexture>(
                reinterpret_cast<void *>(0x3000))};
  }

  [[nodiscard]] video::D3d11VideoProcessorFailure blit(
      const capture::D3d11Texture &, capture::D3d11Texture &,
      const video::D3d11VideoProcessorLayout &) noexcept override {
    ++trace_->blits;
    return trace_->blit_result;
  }

  void reset() noexcept override {}

private:
  std::shared_ptr<ProcessorTrace> trace_;
};

struct EncoderTrace {
  std::vector<video::NvencH264Submit> submits;
  std::vector<std::uint32_t> bitrates;
  std::size_t destroy_sessions{};
  std::size_t unloads{};
};

class FakeNvencApi final : public video::INvencH264Api {
public:
  explicit FakeNvencApi(std::shared_ptr<EncoderTrace> trace)
      : trace_(std::move(trace)) {}

  [[nodiscard]] void *
  device_identity(const capture::D3d11Texture &) noexcept override {
    return reinterpret_cast<void *>(0x2000);
  }

  [[nodiscard]] video::NvencH264Failure open(
      const capture::D3d11Texture &,
      const video::NvencH264Configuration &) noexcept override {
    return video::NvencH264Failure::none;
  }

  [[nodiscard]] video::NvencH264ApiHandleResult
  create_bitstream() noexcept override {
    return {.handle = 10};
  }

  [[nodiscard]] video::NvencH264ApiHandleResult register_input(
      const capture::D3d11Texture &, std::uint32_t,
      std::uint32_t) noexcept override {
    return {.handle = 20};
  }

  [[nodiscard]] video::NvencH264ApiHandleResult
  map_input(std::uintptr_t registered) noexcept override {
    return {.handle = registered + 100};
  }

  [[nodiscard]] video::NvencH264Failure
  submit(const video::NvencH264Submit &submit) noexcept override {
    trace_->submits.push_back(submit);
    return video::NvencH264Failure::none;
  }

  [[nodiscard]] video::NvencH264ApiLockResult
  lock_bitstream(std::uintptr_t) noexcept override {
    const auto &submit = trace_->submits.back();
    if (submit.force_idr) {
      bytes_ = {0, 0, 0, 1, 0x67, 0x64, 0, 0, 0, 1,
                0x68, 0xee, 0, 0, 1, 0x65, 0xaa};
    } else {
      bytes_ = {0, 0, 1, 0x61, 0xbb};
    }
    return {.bitstream = {.data = bytes_.data(),
                          .size = bytes_.size(),
                          .qpc_timestamp = submit.qpc_timestamp}};
  }

  [[nodiscard]] video::NvencH264Failure
  unlock_bitstream(std::uintptr_t) noexcept override {
    return video::NvencH264Failure::none;
  }

  [[nodiscard]] video::NvencH264Failure
  unmap_input(std::uintptr_t) noexcept override {
    return video::NvencH264Failure::none;
  }

  [[nodiscard]] video::NvencH264Failure
  unregister_input(std::uintptr_t) noexcept override {
    return video::NvencH264Failure::none;
  }

  [[nodiscard]] video::NvencH264Failure
  reconfigure_bitrate(std::uint32_t bitrate_bps) noexcept override {
    trace_->bitrates.push_back(bitrate_bps);
    return video::NvencH264Failure::none;
  }

  [[nodiscard]] video::NvencH264Failure
  destroy_bitstream(std::uintptr_t) noexcept override {
    return video::NvencH264Failure::none;
  }

  [[nodiscard]] video::NvencH264Failure destroy_session() noexcept override {
    ++trace_->destroy_sessions;
    return video::NvencH264Failure::none;
  }

  void poison_session(const capture::D3d11Texture *) noexcept override {}
  void unload() noexcept override { ++trace_->unloads; }

private:
  std::shared_ptr<EncoderTrace> trace_;
  std::vector<std::uint8_t> bytes_;
};

class RecordingTransport final : public beacon::worker::IWorkerMediaTransport {
public:
  [[nodiscard]] bool configure_listener(std::string_view,
                                        std::uint16_t) override {
    return true;
  }
  [[nodiscard]] std::uint16_t local_port() const noexcept override {
    return 50000;
  }
  [[nodiscard]] bool open_connection() override { return true; }
  void close_connection() noexcept override {}
  void request_active_disconnect() noexcept override {
    {
      std::lock_guard lock{mutex_};
      ++disconnects;
    }
    changed_.notify_all();
  }
  void shutdown() noexcept override {}

  [[nodiscard]] stream::TransportSendResult send_for_generation(
      stream::TransportPacket packet,
      std::uint64_t session_generation) override {
    {
      std::lock_guard lock{mutex_};
      packets.push_back(std::move(packet));
      generations.push_back(session_generation);
    }
    changed_.notify_all();
    return stream::TransportSendResult::accepted;
  }

  void wait_for_packets(std::size_t count) {
    std::unique_lock lock{mutex_};
    changed_.wait(lock, [this, count] { return packets.size() >= count; });
  }

  void wait_for_disconnect() {
    std::unique_lock lock{mutex_};
    changed_.wait(lock, [this] { return disconnects != 0; });
  }

  std::mutex mutex_;
  std::condition_variable changed_;
  std::vector<stream::TransportPacket> packets;
  std::vector<std::uint64_t> generations;
  std::size_t disconnects{};
};

video::WorkerVideoPlan plan() {
  return {
      .session_id = "session-a",
      .display_device_name = L"\\\\.\\DISPLAY7",
      .width = 2560,
      .height = 1600,
      .frame_rate_numerator = 120,
      .frame_rate_denominator = 1,
      .minimum_bitrate_bps = 8'000'000,
      .initial_bitrate_bps = 24'000'000,
      .maximum_bitrate_bps = 40'000'000,
  };
}

struct Fixture {
  RecordingTransport transport;
  std::shared_ptr<CaptureTrace> capture_trace =
      std::make_shared<CaptureTrace>();
  std::shared_ptr<ProcessorTrace> processor_trace =
      std::make_shared<ProcessorTrace>();
  std::shared_ptr<EncoderTrace> encoder_trace =
      std::make_shared<EncoderTrace>();
  std::vector<video::VideoPipelineFailureEvent> failures;
  std::mutex failure_mutex;
  std::condition_variable failure_changed;
  FakeCapturePlatform *capture_platform{};
  std::shared_ptr<video::ProductionVideoGeneration> generation;

  Fixture() {
    auto capture = std::make_unique<FakeCapturePlatform>(capture_trace);
    capture_platform = capture.get();
    generation = std::make_shared<video::ProductionVideoGeneration>(
        plan(), transport, std::move(capture),
        std::make_unique<FakeProcessorPlatform>(processor_trace),
        std::make_unique<FakeNvencApi>(encoder_trace),
        [this](video::VideoPipelineFailureEvent failure) {
          {
            std::lock_guard lock{failure_mutex};
            failures.push_back(std::move(failure));
          }
          failure_changed.notify_all();
        });
  }

  void wait_for_failure() {
    std::unique_lock lock{failure_mutex};
    failure_changed.wait(lock, [this] { return !failures.empty(); });
  }
};

void one_captured_frame_reaches_the_generation_bound_transport() {
  Fixture fixture;
  BEACON_TEST_REQUIRE(fixture.generation->start(7, 1232));
  fixture.capture_platform->emit(1'000'000);
  fixture.transport.wait_for_packets(1);

  BEACON_TEST_REQUIRE(fixture.capture_trace->starts == 1);
  BEACON_TEST_REQUIRE(fixture.capture_trace->device_name ==
                      L"\\\\.\\DISPLAY7");
  BEACON_TEST_REQUIRE(fixture.processor_trace->blits == 1);
  BEACON_TEST_REQUIRE(fixture.encoder_trace->submits.size() == 1);
  BEACON_TEST_REQUIRE(fixture.encoder_trace->submits[0].force_idr);
  BEACON_TEST_REQUIRE(fixture.transport.generations ==
                      std::vector<std::uint64_t>{7});

  const auto parsed =
      stream::parse_media_datagram(fixture.transport.packets[0].payload);
  BEACON_TEST_REQUIRE(parsed.error == stream::MediaDatagramError::none);
  BEACON_TEST_REQUIRE(parsed.header.sequence == 1);
  BEACON_TEST_REQUIRE(parsed.header.presentation_time_us == 100'000);
  const auto flags = static_cast<std::uint16_t>(parsed.header.flags);
  BEACON_TEST_REQUIRE(
      (flags & static_cast<std::uint16_t>(stream::MediaDatagramFlags::idr)) !=
      0);
  BEACON_TEST_REQUIRE(
      (flags & static_cast<std::uint16_t>(
                   stream::MediaDatagramFlags::codec_configuration)) != 0);

  fixture.generation->stop();
  fixture.generation->stop();
  BEACON_TEST_REQUIRE(fixture.capture_trace->stops == 1);
}

void feedback_applies_server_bounded_bitrate_and_forces_the_next_idr() {
  Fixture fixture;
  BEACON_TEST_REQUIRE(fixture.generation->start(8, 1232));
  fixture.capture_platform->emit(1'000'000);
  fixture.transport.wait_for_packets(1);

  fixture.generation->handle_media_event(beacon::worker::QuicDatagramOutcome{
      .kind = beacon::worker::QuicDatagramOutcomeKind::lost,
      .session_generation = 8,
      .access_unit_sequence = 1,
      .smoothed_rtt_us = 3'000,
      .congestion_window_bytes = 80'000,
  });
  fixture.capture_platform->emit(1'000'010);
  fixture.transport.wait_for_packets(2);

  BEACON_TEST_REQUIRE(fixture.encoder_trace->bitrates ==
                      std::vector<std::uint32_t>{21'000'000});
  BEACON_TEST_REQUIRE(fixture.encoder_trace->submits.size() == 2);
  BEACON_TEST_REQUIRE(fixture.encoder_trace->submits[1].force_idr);
  fixture.generation->stop();
}

void conversion_failure_is_typed_and_disconnects_only_the_active_client() {
  Fixture fixture;
  fixture.processor_trace->blit_result =
      video::D3d11VideoProcessorFailure::device_lost;
  BEACON_TEST_REQUIRE(fixture.generation->start(9, 1232));

  fixture.capture_platform->emit(1'000'000);
  fixture.wait_for_failure();
  fixture.transport.wait_for_disconnect();

  BEACON_TEST_REQUIRE(fixture.failures.size() == 1);
  BEACON_TEST_REQUIRE(fixture.failures[0].session_id == "session-a");
  BEACON_TEST_REQUIRE(fixture.failures[0].session_generation == 9);
  BEACON_TEST_REQUIRE(
      fixture.failures[0].boundary ==
      video::VideoPipelineFailureBoundary::video_processor);
  BEACON_TEST_REQUIRE(
      fixture.failures[0].native_code ==
      static_cast<std::uint32_t>(
          video::D3d11VideoProcessorFailure::device_lost));
  BEACON_TEST_REQUIRE(fixture.transport.disconnects == 1);
  fixture.generation->stop();
}

void production_capability_failures_are_boundary_specific() {
  const auto missing_adapter = video::classify_production_video_capabilities(
      false, video::NvencH264Failure::none);
  BEACON_TEST_REQUIRE(!missing_adapter.available);
  BEACON_TEST_REQUIRE(
      missing_adapter.unavailable_boundary ==
      video::ProductionVideoCapabilityBoundary::capture);
  BEACON_TEST_REQUIRE(
      missing_adapter.unavailable_code ==
      static_cast<std::uint32_t>(
          capture::WgcCaptureFailure::nvidia_adapter_missing));

  const auto missing_runtime = video::classify_production_video_capabilities(
      true, video::NvencH264Failure::runtime_unavailable);
  BEACON_TEST_REQUIRE(!missing_runtime.available);
  BEACON_TEST_REQUIRE(
      missing_runtime.unavailable_boundary ==
      video::ProductionVideoCapabilityBoundary::encoder);
  BEACON_TEST_REQUIRE(
      missing_runtime.unavailable_code ==
      static_cast<std::uint32_t>(video::NvencH264Failure::runtime_unavailable));

  const auto available = video::classify_production_video_capabilities(
      true, video::NvencH264Failure::none);
  BEACON_TEST_REQUIRE(available.available);
  BEACON_TEST_REQUIRE(available.unavailable_code == 0);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    one_captured_frame_reaches_the_generation_bound_transport();
    feedback_applies_server_bounded_bitrate_and_forces_the_next_idr();
    conversion_failure_is_typed_and_disconnects_only_the_active_client();
    production_capability_failures_are_boundary_specific();
  });
}
