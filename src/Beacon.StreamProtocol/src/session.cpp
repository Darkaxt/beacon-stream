#include "beacon/stream/session.h"

#include <utility>

namespace beacon::stream {

StreamSession::StreamSession(SessionIdentity identity, IStreamTransport& transport)
    : identity_(std::move(identity)), transport_(transport) {}

StreamSession::~StreamSession() { static_cast<void>(shutdown()); }

SessionResult StreamSession::authorize_ticket(TicketGrant grant) {
  if (state_ == SessionState::stopped || state_ == SessionState::shutdown) {
    return SessionResult::invalid_state;
  }
  if (grant.token.empty()) {
    return SessionResult::invalid_ticket;
  }
  if (grant.client_id != identity_.client_id) {
    return SessionResult::client_mismatch;
  }
  if (grant.session_id != identity_.session_id) {
    return SessionResult::session_mismatch;
  }
  if (grant.plan_revision != identity_.plan_revision) {
    return SessionResult::plan_mismatch;
  }
  if (tickets_.contains(grant.token)) {
    return SessionResult::ticket_replayed;
  }

  auto token = grant.token;
  tickets_.emplace(std::move(token),
                   TicketRecord{.grant = std::move(grant), .consumed = false});
  record(SessionEventKind::ticket_authorized, SessionResult::accepted);
  return SessionResult::accepted;
}

SessionResult StreamSession::accept(const SessionHandshake& handshake) {
  if (state_ == SessionState::connected || state_ == SessionState::stopped ||
      state_ == SessionState::shutdown) {
    return reject(SessionResult::invalid_state);
  }

  const auto ticket = tickets_.find(handshake.token);
  if (ticket == tickets_.end()) {
    return reject(handshake.token.empty() ? SessionResult::invalid_ticket
                                          : SessionResult::unknown_ticket);
  }
  if (ticket->second.consumed) {
    return reject(SessionResult::ticket_replayed);
  }

  const auto identity_result = validate_identity(handshake, ticket->second.grant);
  if (identity_result != SessionResult::accepted) {
    return reject(identity_result);
  }

  ticket->second.consumed = true;
  const bool reconnecting = state_ == SessionState::disconnected;
  if (!transport_.open_connection()) {
    state_ = SessionState::disconnected;
    return reject(SessionResult::transport_failed);
  }

  state_ = SessionState::connected;
  ++metrics_.accepted_connections;
  if (reconnecting) {
    ++metrics_.reconnects;
  }
  record(SessionEventKind::connected, SessionResult::accepted);
  return SessionResult::accepted;
}

SessionResult StreamSession::connection_lost() {
  if (state_ != SessionState::connected) {
    return SessionResult::invalid_state;
  }

  transport_.close_connection();
  state_ = SessionState::disconnected;
  ++metrics_.connection_losses;
  record(SessionEventKind::connection_lost, SessionResult::accepted);
  return SessionResult::accepted;
}

SessionResult StreamSession::stop() {
  if (state_ == SessionState::shutdown) {
    return SessionResult::already_shutdown;
  }
  if (state_ == SessionState::stopped) {
    return SessionResult::already_stopped;
  }

  if (state_ == SessionState::connected) {
    transport_.close_connection();
  }
  state_ = SessionState::stopped;
  record(SessionEventKind::stopped, SessionResult::accepted);
  return SessionResult::accepted;
}

SessionResult StreamSession::shutdown() {
  if (state_ == SessionState::shutdown) {
    return SessionResult::already_shutdown;
  }

  if (state_ == SessionState::connected) {
    transport_.close_connection();
  }
  transport_.shutdown();
  state_ = SessionState::shutdown;
  record(SessionEventKind::shutdown, SessionResult::accepted);
  return SessionResult::accepted;
}

SessionState StreamSession::state() const noexcept { return state_; }

const SessionMetrics& StreamSession::metrics() const noexcept { return metrics_; }

std::vector<SessionEvent> StreamSession::take_events() {
  auto result = std::move(events_);
  events_.clear();
  return result;
}

SessionResult StreamSession::validate_identity(const SessionHandshake& handshake,
                                               const TicketGrant& grant) const noexcept {
  if (handshake.client_id != identity_.client_id || handshake.client_id != grant.client_id) {
    return SessionResult::client_mismatch;
  }
  if (handshake.session_id != identity_.session_id || handshake.session_id != grant.session_id) {
    return SessionResult::session_mismatch;
  }
  if (handshake.plan_revision != identity_.plan_revision ||
      handshake.plan_revision != grant.plan_revision) {
    return SessionResult::plan_mismatch;
  }
  return SessionResult::accepted;
}

void StreamSession::record(SessionEventKind kind, SessionResult result) {
  events_.push_back({.kind = kind, .state = state_, .result = result});
}

SessionResult StreamSession::reject(SessionResult result) {
  ++metrics_.rejected_connections;
  record(SessionEventKind::ticket_rejected, result);
  return result;
}

}  // namespace beacon::stream
