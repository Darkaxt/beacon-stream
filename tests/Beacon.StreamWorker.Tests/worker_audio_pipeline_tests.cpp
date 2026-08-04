#include "beacon/worker/audio/worker_audio_pipeline.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <cstdint>
#include <memory>
#include <utility>
#include <vector>

namespace {

namespace audio = beacon::worker::audio;
namespace stream_v1 = beacon::stream::v1;

class RecordingGeneration final : public audio::IAudioPipelineGeneration {
public:
  bool start(std::uint64_t session_generation,
             std::uint16_t maximum_datagram_bytes) override {
    ++start_count;
    generation = session_generation;
    datagram_bytes = maximum_datagram_bytes;
    return start_result;
  }

  void stop() noexcept override { ++stop_count; }

  bool start_result{true};
  std::uint64_t generation{};
  std::uint16_t datagram_bytes{};
  std::size_t start_count{};
  std::size_t stop_count{};
};

class RecordingFactory final : public audio::IAudioPipelineGenerationFactory {
public:
  std::shared_ptr<audio::IAudioPipelineGeneration>
  create(const audio::WorkerAudioPlan &plan) override {
    plans.push_back(plan);
    auto generation = std::make_shared<RecordingGeneration>();
    generations.push_back(generation);
    return generation;
  }

  std::vector<audio::WorkerAudioPlan> plans;
  std::vector<std::shared_ptr<RecordingGeneration>> generations;
};

audio::WorkerAudioPlan plan() {
  return {
      .session_id = "session-a",
      .sample_rate_hz = 48'000,
      .channel_count = 2,
      .frame_duration_us = 20'000,
      .bitrate_bps = 96'000,
  };
}

stream_v1::SelectedAudioMode selected_audio() {
  stream_v1::SelectedAudioMode mode;
  mode.set_codec(stream_v1::AUDIO_CODEC_OPUS);
  mode.set_sample_rate_hz(48'000);
  mode.set_channel_count(2);
  mode.set_frame_duration_us(20'000);
  mode.set_bitrate_bps(96'000);
  return mode;
}

beacon::worker::QuicMediaEvent start_event(std::uint64_t generation) {
  beacon::stream::ServerSessionProtocolOutput::AcceptedStartSession start{
      .session_id = "session-a",
      .session_generation = generation,
      .maximum_datagram_bytes = 1232,
  };
  *start.start_session.mutable_selected_audio() = selected_audio();
  return start;
}

void fixed_audio_contract_is_the_only_valid_plan() {
  RecordingFactory factory;
  audio::WorkerAudioPipeline pipeline(factory);

  auto invalid = plan();
  invalid.frame_duration_us = 10'000;
  BEACON_TEST_REQUIRE(!pipeline.prepare(invalid));
  BEACON_TEST_REQUIRE(!pipeline.prepared());

  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));
  BEACON_TEST_REQUIRE(pipeline.prepared());
}

void authenticated_start_requires_the_exact_planned_audio_mode() {
  RecordingFactory factory;
  audio::WorkerAudioPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));

  auto mismatched = std::get<
      beacon::stream::ServerSessionProtocolOutput::AcceptedStartSession>(
      start_event(1));
  mismatched.start_session.mutable_selected_audio()->set_sample_rate_hz(44'100);
  pipeline.handle_media_event(beacon::worker::QuicMediaEvent{mismatched});
  BEACON_TEST_REQUIRE(factory.generations.empty());

  pipeline.handle_media_event(start_event(1));
  BEACON_TEST_REQUIRE(factory.plans == std::vector{plan()});
  BEACON_TEST_REQUIRE(factory.generations.size() == 1);
  BEACON_TEST_REQUIRE(factory.generations[0]->start_count == 1);
  BEACON_TEST_REQUIRE(factory.generations[0]->generation == 1);
  BEACON_TEST_REQUIRE(factory.generations[0]->datagram_bytes == 1232);
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 1);
}

void disconnect_releases_only_its_generation_and_preserves_the_plan() {
  RecordingFactory factory;
  audio::WorkerAudioPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));
  pipeline.handle_media_event(start_event(2));
  auto first = factory.generations[0];

  pipeline.handle_media_event(
      beacon::worker::QuicTransportDisconnected{.session_generation = 2});
  BEACON_TEST_REQUIRE(first->stop_count == 1);
  BEACON_TEST_REQUIRE(pipeline.prepared());
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 0);

  pipeline.handle_media_event(start_event(3));
  BEACON_TEST_REQUIRE(factory.generations.size() == 2);
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 3);

  pipeline.handle_media_event(
      beacon::worker::QuicTransportDisconnected{.session_generation = 2});
  BEACON_TEST_REQUIRE(factory.generations[1]->stop_count == 0);
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 3);
}

void explicit_stop_and_reset_release_capture_exactly_once() {
  RecordingFactory factory;
  audio::WorkerAudioPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));
  pipeline.handle_media_event(start_event(4));
  auto active = factory.generations[0];

  beacon::stream::ServerSessionProtocolOutput::AcceptedStopSession stop{
      .session_generation = 4,
  };
  stop.stop_session.set_reason(stream_v1::SESSION_STOP_REASON_CLIENT_REQUEST);
  pipeline.handle_media_event(beacon::worker::QuicMediaEvent{stop});
  pipeline.handle_media_event(beacon::worker::QuicMediaEvent{stop});
  pipeline.reset();
  pipeline.reset();

  BEACON_TEST_REQUIRE(active->stop_count == 1);
  BEACON_TEST_REQUIRE(!pipeline.prepared());
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 0);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    fixed_audio_contract_is_the_only_valid_plan();
    authenticated_start_requires_the_exact_planned_audio_mode();
    disconnect_releases_only_its_generation_and_preserves_the_plan();
    explicit_stop_and_reset_release_capture_exactly_once();
  });
}
