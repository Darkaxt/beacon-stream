#include "beacon/worker/video/worker_video_pipeline.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <thread>
#include <utility>
#include <vector>

namespace {

namespace stream_v1 = beacon::stream::v1;
namespace video = beacon::worker::video;

struct StartGate {
  std::mutex mutex;
  std::condition_variable changed;
  bool entered{};
  bool released{};
};

class RecordingGeneration final : public video::IVideoPipelineGeneration {
public:
  explicit RecordingGeneration(StartGate *start_gate = nullptr)
      : start_gate_(start_gate) {}

  bool start(std::uint64_t session_generation,
             std::uint16_t maximum_datagram_bytes) override {
    ++start_count;
    generation = session_generation;
    datagram_bytes = maximum_datagram_bytes;
    if (start_gate_ != nullptr) {
      std::unique_lock lock{start_gate_->mutex};
      start_gate_->entered = true;
      start_gate_->changed.notify_all();
      start_gate_->changed.wait(lock,
                                [this] { return start_gate_->released; });
    }
    return start_result;
  }

  void handle_media_event(
      const beacon::worker::QuicMediaEvent &event) override {
    events.push_back(event);
  }

  bool request_idr() override {
    ++idr_count;
    return idr_result;
  }

  void stop() noexcept override { ++stop_count; }

  bool start_result{true};
  bool idr_result{true};
  std::uint64_t generation{};
  std::uint16_t datagram_bytes{};
  std::size_t start_count{};
  std::size_t idr_count{};
  std::size_t stop_count{};
  std::vector<beacon::worker::QuicMediaEvent> events;

private:
  StartGate *start_gate_{};
};

class RecordingFactory final : public video::IVideoPipelineGenerationFactory {
public:
  explicit RecordingFactory(StartGate *start_gate = nullptr)
      : start_gate_(start_gate) {}

  std::shared_ptr<video::IVideoPipelineGeneration>
  create(const video::WorkerVideoPlan &plan) override {
    plans.push_back(plan);
    auto generation = std::make_shared<RecordingGeneration>(start_gate_);
    generations.push_back(generation);
    return generation;
  }

  std::vector<video::WorkerVideoPlan> plans;
  std::vector<std::shared_ptr<RecordingGeneration>> generations;

private:
  StartGate *start_gate_{};
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
      .codec = stream_v1::VIDEO_CODEC_H264,
      .dynamic_range = stream_v1::DYNAMIC_RANGE_SDR,
      .profile = stream_v1::VIDEO_PROFILE_H264_HIGH,
      .bit_depth = 8,
      .color_primaries = stream_v1::COLOR_PRIMARIES_BT709,
      .transfer_function = stream_v1::TRANSFER_FUNCTION_BT709,
      .matrix_coefficients = stream_v1::MATRIX_COEFFICIENTS_BT709,
      .color_range = stream_v1::COLOR_RANGE_LIMITED,
  };
}

stream_v1::SelectedVideoMode selected_video() {
  stream_v1::SelectedVideoMode mode;
  mode.set_codec(stream_v1::VIDEO_CODEC_H264);
  mode.set_width(2560);
  mode.set_height(1600);
  mode.set_frames_per_second_numerator(120);
  mode.set_frames_per_second_denominator(1);
  mode.set_dynamic_range(stream_v1::DYNAMIC_RANGE_SDR);
  mode.set_profile(stream_v1::VIDEO_PROFILE_H264_HIGH);
  mode.set_bit_depth(8);
  mode.set_color_primaries(stream_v1::COLOR_PRIMARIES_BT709);
  mode.set_transfer_function(stream_v1::TRANSFER_FUNCTION_BT709);
  mode.set_matrix_coefficients(stream_v1::MATRIX_COEFFICIENTS_BT709);
  mode.set_color_range(stream_v1::COLOR_RANGE_LIMITED);
  return mode;
}

beacon::worker::QuicMediaEvent start_event(std::uint64_t generation) {
  beacon::stream::ServerSessionProtocolOutput::AcceptedStartSession start{
      .session_id = "session-a",
      .session_generation = generation,
      .maximum_datagram_bytes = 1232,
  };
  *start.start_session.mutable_selected_video() = selected_video();
  return start;
}

beacon::worker::QuicMediaEvent feedback_event(std::uint64_t generation) {
  beacon::stream::ServerSessionProtocolOutput::ParsedFeedback feedback{
      .session_generation = generation,
  };
  feedback.feedback.set_protocol_version(1);
  feedback.feedback.set_session_id("session-a");
  feedback.feedback.set_sequence(1);
  feedback.feedback.mutable_queue_depth()->set_queued_access_units(2);
  return feedback;
}

void invalid_plans_never_become_prepared() {
  RecordingFactory factory;
  video::WorkerVideoPipeline pipeline(factory);

  auto invalid = plan();
  invalid.minimum_bitrate_bps = invalid.initial_bitrate_bps + 1;

  BEACON_TEST_REQUIRE(!pipeline.prepare(invalid));
  BEACON_TEST_REQUIRE(!pipeline.prepared());
  BEACON_TEST_REQUIRE(factory.generations.empty());
}

void authenticated_start_creates_only_the_exact_planned_generation() {
  RecordingFactory factory;
  video::WorkerVideoPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));

  auto mismatched = std::get<
      beacon::stream::ServerSessionProtocolOutput::AcceptedStartSession>(
      start_event(1));
  mismatched.start_session.mutable_selected_video()->set_height(1440);
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

void stale_events_cannot_control_the_active_generation() {
  RecordingFactory factory;
  video::WorkerVideoPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));
  pipeline.handle_media_event(start_event(3));

  pipeline.handle_media_event(feedback_event(2));
  BEACON_TEST_REQUIRE(factory.generations[0]->events.empty());
  BEACON_TEST_REQUIRE(pipeline.request_idr());
  BEACON_TEST_REQUIRE(factory.generations[0]->idr_count == 1);

  pipeline.handle_media_event(feedback_event(3));
  BEACON_TEST_REQUIRE(factory.generations[0]->events.size() == 1);
}

void disconnect_releases_one_generation_but_preserves_reconnect_plan() {
  RecordingFactory factory;
  video::WorkerVideoPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));
  pipeline.handle_media_event(start_event(1));
  auto first = factory.generations[0];

  pipeline.handle_media_event(
      beacon::worker::QuicTransportDisconnected{.session_generation = 1});

  BEACON_TEST_REQUIRE(first->stop_count == 1);
  BEACON_TEST_REQUIRE(pipeline.prepared());
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 0);

  pipeline.handle_media_event(start_event(2));
  BEACON_TEST_REQUIRE(factory.generations.size() == 2);
  BEACON_TEST_REQUIRE(factory.generations[1] != first);
  BEACON_TEST_REQUIRE(factory.generations[1]->generation == 2);

  pipeline.handle_media_event(
      beacon::worker::QuicTransportDisconnected{.session_generation = 1});
  BEACON_TEST_REQUIRE(factory.generations[1]->stop_count == 0);
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 2);
}

void explicit_stop_and_reset_release_resources_exactly_once() {
  RecordingFactory factory;
  video::WorkerVideoPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));
  pipeline.handle_media_event(start_event(4));
  auto active = factory.generations[0];

  beacon::stream::ServerSessionProtocolOutput::AcceptedStopSession stop{
      .session_generation = 4,
  };
  stop.stop_session.set_reason(stream_v1::SESSION_STOP_REASON_CLIENT_REQUEST);
  pipeline.handle_media_event(beacon::worker::QuicMediaEvent{stop});
  pipeline.handle_media_event(beacon::worker::QuicMediaEvent{stop});

  BEACON_TEST_REQUIRE(active->stop_count == 1);
  BEACON_TEST_REQUIRE(pipeline.prepared());
  pipeline.reset();
  pipeline.reset();
  BEACON_TEST_REQUIRE(active->stop_count == 1);
  BEACON_TEST_REQUIRE(!pipeline.prepared());
  BEACON_TEST_REQUIRE(!pipeline.request_idr());
}

void disconnect_during_start_abandons_generation_without_losing_plan() {
  StartGate gate;
  RecordingFactory factory(&gate);
  video::WorkerVideoPipeline pipeline(factory);
  BEACON_TEST_REQUIRE(pipeline.prepare(plan()));

  std::thread starter([&pipeline] { pipeline.handle_media_event(start_event(5)); });
  {
    std::unique_lock lock{gate.mutex};
    gate.changed.wait(lock, [&gate] { return gate.entered; });
  }

  pipeline.handle_media_event(
      beacon::worker::QuicTransportDisconnected{.session_generation = 5});
  {
    std::lock_guard lock{gate.mutex};
    gate.released = true;
  }
  gate.changed.notify_all();
  starter.join();

  BEACON_TEST_REQUIRE(factory.generations.size() == 1);
  BEACON_TEST_REQUIRE(factory.generations[0]->stop_count == 1);
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 0);
  BEACON_TEST_REQUIRE(pipeline.prepared());

  pipeline.handle_media_event(start_event(6));
  BEACON_TEST_REQUIRE(factory.generations.size() == 2);
  BEACON_TEST_REQUIRE(pipeline.active_generation() == 6);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    invalid_plans_never_become_prepared();
    authenticated_start_creates_only_the_exact_planned_generation();
    stale_events_cannot_control_the_active_generation();
    disconnect_releases_one_generation_but_preserves_reconnect_plan();
    explicit_stop_and_reset_release_resources_exactly_once();
    disconnect_during_start_abandons_generation_without_losing_plan();
  });
}
