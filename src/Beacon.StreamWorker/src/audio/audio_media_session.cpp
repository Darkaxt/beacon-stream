#include "beacon/worker/audio/audio_media_session.h"

#include "beacon/stream/media_datagram.h"

#include <utility>

namespace beacon::worker::audio {

AudioMediaSession::AudioMediaSession(IMediaDatagramTransport &transport)
    : transport_(transport) {}

bool AudioMediaSession::begin_transport_generation(
    std::uint64_t session_generation, std::uint16_t maximum_datagram_bytes) {
  if (session_generation == 0 ||
      maximum_datagram_bytes <= stream::media_datagram_header_bytes) {
    return false;
  }
  std::lock_guard lock{mutex_};
  session_generation_ = session_generation;
  maximum_datagram_bytes_ = maximum_datagram_bytes;
  return true;
}

AudioMediaSendResult
AudioMediaSession::send_packet(std::uint64_t session_generation,
                               std::span<const std::uint8_t> encoded_packet,
                               std::uint64_t presentation_time_us) {
  std::lock_guard lock{mutex_};
  if (session_generation_ == 0 || maximum_datagram_bytes_ == 0) {
    return {.failure = AudioMediaSessionFailure::transport_not_ready};
  }
  if (session_generation != session_generation_) {
    return {.failure = AudioMediaSessionFailure::stale_generation};
  }

  const auto sequence = next_packet_sequence_++;
  auto packetized = packetizer_.packetize(
      encoded_packet, sequence, presentation_time_us, maximum_datagram_bytes_);
  if (packetized.failure != stream::AudioMediaPacketizerFailure::none ||
      !packetized.packet.has_value()) {
    return {
        .failure = AudioMediaSessionFailure::packetization_failed,
        .packetizer_failure = packetized.failure,
        .sequence = sequence,
    };
  }

  const auto sent = transport_.send_for_generation(
      std::move(*packetized.packet), session_generation);
  return {
      .failure = sent == stream::TransportSendResult::accepted
                     ? AudioMediaSessionFailure::none
                     : AudioMediaSessionFailure::transport_closed,
      .packetizer_failure = stream::AudioMediaPacketizerFailure::none,
      .sequence = sequence,
      .attempted_datagrams = 1,
      .accepted_datagrams =
          sent == stream::TransportSendResult::accepted ? 1U : 0U,
  };
}

} // namespace beacon::worker::audio
