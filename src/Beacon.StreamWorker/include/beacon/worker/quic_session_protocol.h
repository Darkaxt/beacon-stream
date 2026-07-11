#pragma once

#include "beacon/stream/transport.h"
#include "beacon/worker/quic_listener.h"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

namespace beacon::worker {

inline constexpr std::uint32_t maximum_stream_message_bytes = 1024U * 1024U;

enum class QuicPeerStreamRole {
  session,
  input,
  feedback,
  invalid,
};

struct QuicSessionProtocolOutput {
  bool close_connection{};
  std::vector<std::vector<std::byte>> session_replies;
  std::vector<stream::TransportPacket> packets;
};

[[nodiscard]] QuicPeerStreamRole
classify_peer_stream(std::uint64_t stream_id) noexcept;

class QuicSessionProtocol {
public:
  explicit QuicSessionProtocol(AuthorizedQuicTicketStore &authorized_tickets);

  void set_maximum_datagram_bytes(std::uint16_t value) noexcept;
  [[nodiscard]] QuicSessionProtocolOutput
  receive(QuicPeerStreamRole role, std::span<const std::byte> bytes,
          std::uint64_t now_unix_ms);
  [[nodiscard]] bool authenticated() const noexcept;
  void reset();

private:
  AuthorizedQuicTicketStore &authorized_tickets_;
  std::vector<std::byte> session_bytes_;
  std::vector<std::byte> input_bytes_;
  std::vector<std::byte> feedback_bytes_;
  std::uint16_t maximum_datagram_bytes_{};
  std::string session_id_;
  std::uint64_t last_session_sequence_{};
  std::uint64_t last_input_sequence_{};
  std::uint64_t last_feedback_sequence_{};
  bool authenticated_{};
};

} // namespace beacon::worker
