#pragma once

#include "beacon/stream/transport.h"
#include "beacon/worker/quic_ticket_store.h"
#include "beacon/worker/secure_bytes.h"
#include "stream_control.pb.h"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <variant>
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
  struct AcceptedAuthentication {
    std::string session_id;
    std::uint64_t session_generation{};
    std::uint16_t maximum_datagram_bytes{};
  };

  struct AcceptedStartSession {
    std::string session_id;
    std::uint64_t session_generation{};
    std::uint16_t maximum_datagram_bytes{};
    stream::v1::StartSession start_session;
  };

  struct AcceptedStartBenchmark {
    std::string session_id;
    std::uint64_t session_generation{};
    std::uint16_t maximum_datagram_bytes{};
    stream::v1::StartBenchmark start_benchmark;
  };

  struct AcceptedCancelBenchmark {
    std::uint64_t session_generation{};
    stream::v1::CancelBenchmark cancel_benchmark;
  };

  struct AcceptedStopSession {
    std::uint64_t session_generation{};
    stream::v1::StopSession stop_session;
  };

  struct AcceptedIdrRequest {
    std::uint64_t session_generation{};
    stream::v1::RequestIdr request;
  };

  using AcceptedSessionAction =
      std::variant<AcceptedStartSession, AcceptedStartBenchmark,
                   AcceptedCancelBenchmark, AcceptedStopSession,
                   AcceptedIdrRequest>;

  struct ParsedInput {
    std::uint64_t session_generation{};
    stream::v1::InputStreamEnvelope input;
  };

  struct ParsedFeedback {
    std::uint64_t session_generation{};
    stream::v1::FeedbackStreamEnvelope feedback;
  };

  bool close_connection{};
  bool stale_callback{};
  std::vector<std::vector<std::byte>> session_replies;
  std::vector<stream::TransportPacket> packets;
  std::optional<AcceptedAuthentication> accepted_authentication;
  std::vector<AcceptedSessionAction> accepted_session_actions;
  std::vector<ParsedInput> inputs;
  std::vector<ParsedFeedback> feedback;
};

[[nodiscard]] QuicPeerStreamRole
classify_peer_stream(std::uint64_t stream_id) noexcept;

class QuicSessionProtocol {
public:
  explicit QuicSessionProtocol(
      AuthorizedQuicTicketStore &authorized_tickets,
      SecureClearObserver session_wipe_observer = nullptr,
      void *session_wipe_context = nullptr);
  ~QuicSessionProtocol();

  void set_maximum_datagram_bytes(std::uint16_t value) noexcept;
  void begin_connection(std::uint64_t connection_generation);
  [[nodiscard]] QuicSessionProtocolOutput
  receive(std::uint64_t connection_generation, QuicPeerStreamRole role,
          std::span<const std::byte> bytes, std::uint64_t now_unix_ms);
  [[nodiscard]] QuicSessionProtocolOutput
  receive(QuicPeerStreamRole role, std::span<const std::byte> bytes,
          std::uint64_t now_unix_ms);
  [[nodiscard]] bool authenticated() const noexcept;
  void reset() noexcept;

private:
  void clear_stream_bytes() noexcept;
  void consume_session_prefix(std::size_t bytes) noexcept;

  AuthorizedQuicTicketStore &authorized_tickets_;
  std::vector<std::byte> session_bytes_;
  std::vector<std::byte> input_bytes_;
  std::vector<std::byte> feedback_bytes_;
  std::uint16_t maximum_datagram_bytes_{};
  std::string session_id_;
  std::uint64_t last_session_sequence_{};
  std::uint64_t last_input_sequence_{};
  std::uint64_t last_feedback_sequence_{};
  std::uint64_t current_generation_{};
  std::uint64_t next_generation_{};
  std::uint64_t active_connection_generation_{};
  std::string benchmark_run_id_;
  std::optional<stream::v1::StartBenchmark> authorized_benchmark_plan_;
  SecureClearObserver session_wipe_observer_{};
  void *session_wipe_context_{};
  bool authenticated_{};
  bool started_{};
};

} // namespace beacon::worker
