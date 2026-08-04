#include "hosted_video_generation.h"

#include "beacon/stream/media_datagram.h"
#include "test_failure.h"

#include <cstddef>
#include <cstdint>
#include <string_view>
#include <utility>
#include <vector>

namespace {

class RecordingTransport final : public beacon::worker::IWorkerMediaTransport {
public:
  bool configure_listener(std::string_view, std::uint16_t) override {
    return true;
  }
  std::uint16_t local_port() const noexcept override { return 1; }
  bool open_connection() override { return true; }
  void close_connection() noexcept override {}
  void request_active_disconnect() noexcept override {}
  void shutdown() noexcept override {}
  beacon::stream::TransportSendResult
  send_for_generation(beacon::stream::TransportPacket packet,
                      std::uint64_t generation) override {
    generations.push_back(generation);
    packets.push_back(std::move(packet));
    return beacon::stream::TransportSendResult::accepted;
  }

  std::vector<std::uint64_t> generations;
  std::vector<beacon::stream::TransportPacket> packets;
};

beacon::testing::HostedAccessUnits vectors(std::uint8_t suffix) {
  return {
      {0, 0, 0, 1, 0x65, suffix},
      {0, 0, 0, 1, 0x41, static_cast<std::uint8_t>(suffix + 1)},
      {0, 0, 0, 1, 0x41, static_cast<std::uint8_t>(suffix + 2)},
  };
}

beacon::worker::video::WorkerVideoPlan plan(std::uint32_t width,
                                            std::uint32_t height,
                                            std::uint32_t fps) {
  return {
      .session_id = "hosted-session",
      .display_device_name = L"fake-hosted-display",
      .width = width,
      .height = height,
      .frame_rate_numerator = fps,
      .frame_rate_denominator = 1,
      .minimum_bitrate_bps = 5'000'000,
      .initial_bitrate_bps = 8'000'000,
      .maximum_bitrate_bps = 12'000'000,
  };
}

std::uint64_t last_frame_sequence(const RecordingTransport &transport) {
  BEACON_TEST_REQUIRE(!transport.packets.empty());
  const auto parsed = beacon::stream::parse_media_datagram(
      transport.packets.back().payload);
  BEACON_TEST_REQUIRE(parsed.error == beacon::stream::MediaDatagramError::none);
  return parsed.header.sequence;
}

void feedback_paces_frames_and_new_generation_restarts_vector() {
  RecordingTransport transport;
  beacon::testing::HostedVideoGenerationFactory factory(
      transport, vectors(10), vectors(20));
  BEACON_TEST_REQUIRE(factory.valid());
  auto first = factory.create(plan(640, 360, 30));
  BEACON_TEST_REQUIRE(first != nullptr);
  BEACON_TEST_REQUIRE(first->start(7, 1200));
  BEACON_TEST_REQUIRE(last_frame_sequence(transport) == 1);

  beacon::stream::ServerSessionProtocolOutput::ParsedFeedback feedback{
      .session_generation = 7,
      .feedback = {},
  };
  feedback.feedback.mutable_rendered_frame()->set_frame_sequence(1);
  first->handle_media_event(feedback);
  BEACON_TEST_REQUIRE(last_frame_sequence(transport) == 2);
  BEACON_TEST_REQUIRE(transport.generations.back() == 7);
  first->stop();

  auto reconnected = factory.create(plan(640, 360, 30));
  BEACON_TEST_REQUIRE(reconnected != nullptr);
  BEACON_TEST_REQUIRE(reconnected->start(8, 1200));
  BEACON_TEST_REQUIRE(last_frame_sequence(transport) == 1);
  BEACON_TEST_REQUIRE(transport.generations.back() == 8);
}

void factory_rejects_unhosted_video_modes() {
  RecordingTransport transport;
  beacon::testing::HostedVideoGenerationFactory factory(
      transport, vectors(10), vectors(20));
  BEACON_TEST_REQUIRE(factory.create(plan(1920, 1080, 60)) == nullptr);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    feedback_paces_frames_and_new_generation_restarts_vector();
    factory_rejects_unhosted_video_modes();
  });
}
