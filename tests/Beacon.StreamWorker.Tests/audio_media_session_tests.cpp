#include "beacon/stream/media_datagram.h"
#include "beacon/worker/audio/audio_media_session.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <utility>
#include <vector>

namespace {

namespace audio = beacon::worker::audio;
namespace stream = beacon::stream;

class RecordingTransport final
    : public beacon::worker::IMediaDatagramTransport {
public:
  stream::TransportSendResult
  send_for_generation(stream::TransportPacket packet,
                      std::uint64_t session_generation) override {
    ++send_calls;
    if (close_on_call == send_calls) {
      return stream::TransportSendResult::connection_closed;
    }
    generations.push_back(session_generation);
    packets.push_back(std::move(packet));
    return stream::TransportSendResult::accepted;
  }

  std::optional<std::size_t> close_on_call;
  std::size_t send_calls{};
  std::vector<std::uint64_t> generations;
  std::vector<stream::TransportPacket> packets;
};

std::vector<std::uint8_t> opus_packet(std::size_t size = 240) {
  return std::vector<std::uint8_t>(size, 0x5a);
}

void packets_use_the_active_generation_and_independent_sequence() {
  RecordingTransport transport;
  audio::AudioMediaSession session(transport);
  BEACON_TEST_REQUIRE(session.begin_transport_generation(4, 1232));

  const auto first = session.send_packet(4, opus_packet(), 20'000);
  const auto second = session.send_packet(4, opus_packet(), 40'000);

  BEACON_TEST_REQUIRE(first.failure == audio::AudioMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(second.failure == audio::AudioMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(first.sequence == 1);
  BEACON_TEST_REQUIRE(second.sequence == 2);
  BEACON_TEST_REQUIRE(transport.generations ==
                      std::vector<std::uint64_t>({4, 4}));
  BEACON_TEST_REQUIRE(stream::parse_media_datagram(transport.packets[0].payload)
                          .header.media_kind == stream::MediaKind::audio);
}

void stale_generation_and_closed_transport_are_not_retried() {
  RecordingTransport transport;
  audio::AudioMediaSession session(transport);
  BEACON_TEST_REQUIRE(session.begin_transport_generation(4, 1232));

  const auto stale = session.send_packet(3, opus_packet(), 20'000);
  BEACON_TEST_REQUIRE(stale.failure ==
                      audio::AudioMediaSessionFailure::stale_generation);
  BEACON_TEST_REQUIRE(transport.send_calls == 0);

  transport.close_on_call = 1;
  const auto closed = session.send_packet(4, opus_packet(), 20'000);
  BEACON_TEST_REQUIRE(closed.failure ==
                      audio::AudioMediaSessionFailure::transport_closed);
  BEACON_TEST_REQUIRE(transport.send_calls == 1);
  BEACON_TEST_REQUIRE(transport.packets.empty());
}

void each_transport_generation_restarts_readiness_not_sequence_identity() {
  RecordingTransport transport;
  audio::AudioMediaSession session(transport);
  BEACON_TEST_REQUIRE(session.begin_transport_generation(4, 1232));
  BEACON_TEST_REQUIRE(session.send_packet(4, opus_packet(), 20'000).sequence ==
                      1);
  BEACON_TEST_REQUIRE(session.begin_transport_generation(5, 1232));
  BEACON_TEST_REQUIRE(session.send_packet(5, opus_packet(), 20'000).sequence ==
                      2);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    packets_use_the_active_generation_and_independent_sequence();
    stale_generation_and_closed_transport_are_not_retried();
    each_transport_generation_restarts_readiness_not_sequence_identity();
  });
}
