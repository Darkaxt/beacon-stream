#include "beacon/stream/media_datagram.h"
#include "beacon/worker/audio/production_audio_generation.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace {

namespace audio = beacon::worker::audio;
namespace stream = beacon::stream;

struct CaptureTrace {
  std::size_t starts{};
  std::size_t stops{};
  bool start_result{true};
  audio::WasapiLoopbackFailure start_failure{};
};

class FakeAudioCapture final : public audio::IAudioFrameCapture {
public:
  explicit FakeAudioCapture(std::shared_ptr<CaptureTrace> trace)
      : trace_(std::move(trace)) {}

  bool start(audio::AudioFrameSink sink,
             audio::AudioCaptureFailureSink failure_sink) override {
    ++trace_->starts;
    sink_ = std::move(sink);
    failure_sink_ = std::move(failure_sink);
    return trace_->start_result;
  }

  void stop() noexcept override {
    ++trace_->stops;
    sink_ = {};
    failure_sink_ = {};
  }

  bool active() const noexcept override { return static_cast<bool>(sink_); }

  audio::WasapiLoopbackFailure failure() const noexcept override {
    return trace_->start_failure;
  }

  void emit(std::uint64_t timestamp_us,
            std::size_t sample_count = audio::opus_frame_interleaved_samples) {
    BEACON_TEST_REQUIRE(static_cast<bool>(sink_));
    sink_({.interleaved_pcm = std::vector<float>(sample_count, 0.125F),
           .presentation_time_us = timestamp_us});
  }

  void fail(audio::WasapiLoopbackFailure failure) {
    BEACON_TEST_REQUIRE(static_cast<bool>(failure_sink_));
    failure_sink_(failure);
  }

private:
  std::shared_ptr<CaptureTrace> trace_;
  audio::AudioFrameSink sink_;
  audio::AudioCaptureFailureSink failure_sink_;
};

class RecordingTransport final : public beacon::worker::IWorkerMediaTransport {
public:
  bool configure_listener(std::string_view, std::uint16_t) override {
    return true;
  }
  std::uint16_t local_port() const noexcept override { return 50'000; }
  bool open_connection() override { return true; }
  void close_connection() noexcept override {}
  void request_active_disconnect() noexcept override { ++disconnects; }
  void shutdown() noexcept override {}

  stream::TransportSendResult
  send_for_generation(stream::TransportPacket packet,
                      std::uint64_t session_generation) override {
    if (close_transport) {
      return stream::TransportSendResult::connection_closed;
    }
    packets.push_back(std::move(packet));
    generations.push_back(session_generation);
    return stream::TransportSendResult::accepted;
  }

  bool close_transport{};
  std::size_t disconnects{};
  std::vector<stream::TransportPacket> packets;
  std::vector<std::uint64_t> generations;
};

audio::WorkerAudioPlan plan() {
  return {
      .session_id = "session-a",
      .sample_rate_hz = audio::opus_sample_rate_hz,
      .channel_count = audio::opus_channel_count,
      .frame_duration_us = audio::opus_frame_duration_us,
      .bitrate_bps = audio::opus_bitrate_bps,
  };
}

struct Fixture {
  RecordingTransport transport;
  std::shared_ptr<CaptureTrace> capture_trace =
      std::make_shared<CaptureTrace>();
  FakeAudioCapture *capture{};
  std::vector<audio::AudioPipelineFailureEvent> failures;
  std::shared_ptr<audio::ProductionAudioGeneration> generation;

  Fixture() {
    auto capture_owner = std::make_unique<FakeAudioCapture>(capture_trace);
    capture = capture_owner.get();
    generation = std::make_shared<audio::ProductionAudioGeneration>(
        plan(), transport, std::move(capture_owner),
        [this](audio::AudioPipelineFailureEvent failure) {
          failures.push_back(std::move(failure));
        });
  }
};

void one_pcm_frame_becomes_one_generation_bound_opus_datagram() {
  Fixture fixture;
  BEACON_TEST_REQUIRE(fixture.generation->start(7, 1232));
  fixture.capture->emit(1'000'000);

  BEACON_TEST_REQUIRE(fixture.capture_trace->starts == 1);
  BEACON_TEST_REQUIRE(fixture.transport.generations ==
                      std::vector<std::uint64_t>{7});
  BEACON_TEST_REQUIRE(fixture.transport.packets.size() == 1);
  const auto parsed =
      stream::parse_media_datagram(fixture.transport.packets[0].payload);
  BEACON_TEST_REQUIRE(parsed.error == stream::MediaDatagramError::none);
  BEACON_TEST_REQUIRE(parsed.header.media_kind == stream::MediaKind::audio);
  BEACON_TEST_REQUIRE(parsed.header.sequence == 1);
  BEACON_TEST_REQUIRE(parsed.header.presentation_time_us == 1'000'000);
  BEACON_TEST_REQUIRE(!parsed.payload.empty());

  fixture.generation->stop();
  fixture.generation->stop();
  BEACON_TEST_REQUIRE(fixture.capture_trace->stops == 1);
}

void malformed_pcm_disconnects_with_an_encoder_boundary() {
  Fixture fixture;
  BEACON_TEST_REQUIRE(fixture.generation->start(8, 1232));
  fixture.capture->emit(2'000'000, 12);

  BEACON_TEST_REQUIRE(fixture.failures.size() == 1);
  BEACON_TEST_REQUIRE(fixture.failures[0].boundary ==
                      audio::AudioPipelineFailureBoundary::encoder);
  BEACON_TEST_REQUIRE(fixture.failures[0].session_generation == 8);
  BEACON_TEST_REQUIRE(fixture.transport.disconnects == 1);
  BEACON_TEST_REQUIRE(fixture.transport.packets.empty());
  fixture.generation->stop();
}

void capture_failure_preserves_stage_and_native_code() {
  Fixture fixture;
  BEACON_TEST_REQUIRE(fixture.generation->start(9, 1232));
  fixture.capture->fail({
      .stage = audio::WasapiLoopbackStage::packet_acquisition,
      .native_code = 0x88890004U,
  });

  BEACON_TEST_REQUIRE(fixture.failures.size() == 1);
  BEACON_TEST_REQUIRE(fixture.failures[0].boundary ==
                      audio::AudioPipelineFailureBoundary::capture);
  BEACON_TEST_REQUIRE(fixture.failures[0].failure_stage == "packet-acquire");
  BEACON_TEST_REQUIRE(fixture.failures[0].native_code == 0x88890004U);
  BEACON_TEST_REQUIRE(fixture.transport.disconnects == 1);
  fixture.generation->stop();
}

void closed_transport_is_terminal_and_not_retried() {
  Fixture fixture;
  fixture.transport.close_transport = true;
  BEACON_TEST_REQUIRE(fixture.generation->start(10, 1232));
  fixture.capture->emit(3'000'000);

  BEACON_TEST_REQUIRE(fixture.failures.size() == 1);
  BEACON_TEST_REQUIRE(fixture.failures[0].boundary ==
                      audio::AudioPipelineFailureBoundary::transport);
  BEACON_TEST_REQUIRE(fixture.transport.disconnects == 1);
  BEACON_TEST_REQUIRE(fixture.transport.packets.empty());
  fixture.generation->stop();
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    one_pcm_frame_becomes_one_generation_bound_opus_datagram();
    malformed_pcm_disconnects_with_an_encoder_boundary();
    capture_failure_preserves_stage_and_native_code();
    closed_transport_is_terminal_and_not_retried();
  });
}
