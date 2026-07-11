#pragma once

#include "beacon/stream/transport.h"

#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

namespace beacon::stream {

struct SessionIdentity {
  std::string client_id;
  std::string session_id;
  std::uint64_t plan_revision{};
};

struct TicketGrant {
  std::string token;
  std::string client_id;
  std::string session_id;
  std::uint64_t plan_revision{};
};

struct SessionHandshake {
  std::string token;
  std::string client_id;
  std::string session_id;
  std::uint64_t plan_revision{};
};

enum class SessionState {
  waiting_for_connection,
  connected,
  disconnected,
  stopped,
  shutdown,
};

enum class SessionResult {
  accepted,
  invalid_state,
  invalid_ticket,
  unknown_ticket,
  ticket_replayed,
  client_mismatch,
  session_mismatch,
  plan_mismatch,
  transport_failed,
  already_stopped,
  already_shutdown,
};

enum class SessionEventKind {
  ticket_authorized,
  ticket_rejected,
  connected,
  connection_lost,
  stopped,
  shutdown,
};

struct SessionEvent {
  SessionEventKind kind{};
  SessionState state{};
  SessionResult result{};
};

struct SessionMetrics {
  std::uint64_t accepted_connections{};
  std::uint64_t rejected_connections{};
  std::uint64_t reconnects{};
  std::uint64_t connection_losses{};
};

class StreamSession {
 public:
  StreamSession(SessionIdentity identity, IStreamTransport& transport);
  ~StreamSession();

  StreamSession(const StreamSession&) = delete;
  StreamSession& operator=(const StreamSession&) = delete;

  [[nodiscard]] SessionResult authorize_ticket(TicketGrant grant);
  [[nodiscard]] SessionResult accept(const SessionHandshake& handshake);
  [[nodiscard]] SessionResult connection_lost();
  [[nodiscard]] SessionResult stop();
  [[nodiscard]] SessionResult shutdown();

  [[nodiscard]] SessionState state() const noexcept;
  [[nodiscard]] const SessionMetrics& metrics() const noexcept;
  [[nodiscard]] std::vector<SessionEvent> take_events();

 private:
  struct TicketRecord {
    TicketGrant grant;
    bool consumed{};
  };

  [[nodiscard]] SessionResult validate_identity(const SessionHandshake& handshake,
                                                const TicketGrant& grant) const noexcept;
  void record(SessionEventKind kind, SessionResult result);
  [[nodiscard]] SessionResult reject(SessionResult result);

  SessionIdentity identity_;
  IStreamTransport& transport_;
  SessionState state_{SessionState::waiting_for_connection};
  SessionMetrics metrics_{};
  std::unordered_map<std::string, TicketRecord> tickets_;
  std::vector<SessionEvent> events_;
};

}  // namespace beacon::stream
