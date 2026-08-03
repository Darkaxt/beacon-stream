#pragma once

#include "beacon/stream/audio_media_packetizer.h"
#include "beacon/worker/media_datagram_transport.h"

#include <cstddef>
#include <cstdint>
#include <mutex>
#include <span>

namespace beacon::worker::audio {

enum class AudioMediaSessionFailure {
  none,
  transport_not_ready,
  packetization_failed,
  transport_closed,
  stale_generation,
};

struct AudioMediaSendResult {
  AudioMediaSessionFailure failure{AudioMediaSessionFailure::none};
  stream::AudioMediaPacketizerFailure packetizer_failure{
      stream::AudioMediaPacketizerFailure::none};
  std::uint64_t sequence{};
  std::size_t attempted_datagrams{};
  std::size_t accepted_datagrams{};
};

class AudioMediaSession final {
public:
  explicit AudioMediaSession(IMediaDatagramTransport &transport);

  [[nodiscard]] bool
  begin_transport_generation(std::uint64_t session_generation,
                             std::uint16_t maximum_datagram_bytes);
  [[nodiscard]] AudioMediaSendResult
  send_packet(std::uint64_t session_generation,
              std::span<const std::uint8_t> encoded_packet,
              std::uint64_t presentation_time_us);

private:
  IMediaDatagramTransport &transport_;
  stream::AudioMediaPacketizer packetizer_;
  std::mutex mutex_;
  std::uint64_t session_generation_{};
  std::uint64_t next_packet_sequence_{1};
  std::uint16_t maximum_datagram_bytes_{};
};

} // namespace beacon::worker::audio
