#include "beacon/worker/quic_session_protocol.h"

#include "stream_control.pb.h"

#include <algorithm>
#include <limits>
#include <utility>

namespace beacon::worker {
namespace {

namespace stream_v1 = beacon::stream::v1;

std::uint32_t read_u32(std::span<const std::byte, 4> bytes) noexcept {
  std::uint32_t value = 0;
  for (const auto byte : bytes) {
    value = (value << 8U) | std::to_integer<std::uint32_t>(byte);
  }
  return value;
}

void write_u32(std::span<std::byte, 4> bytes, std::uint32_t value) noexcept {
  for (std::size_t index = 0; index < bytes.size(); ++index) {
    bytes[index] =
        static_cast<std::byte>((value >> ((3U - index) * 8U)) & 0xffU);
  }
}

template <typename Message>
std::vector<std::byte> frame(const Message &message) {
  const auto size = message.ByteSizeLong();
  if (size > maximum_stream_message_bytes ||
      size > static_cast<std::size_t>(std::numeric_limits<int>::max())) {
    return {};
  }
  std::vector<std::byte> result(4 + size);
  write_u32(std::span<std::byte, 4>{result.data(), 4},
            static_cast<std::uint32_t>(size));
  if (!message.SerializeToArray(result.data() + 4, static_cast<int>(size))) {
    return {};
  }
  return result;
}

stream_v1::SessionErrorCode error_for(QuicTicketConsumeResult result) noexcept {
  switch (result) {
  case QuicTicketConsumeResult::accepted:
    return stream_v1::SESSION_ERROR_CODE_NONE;
  case QuicTicketConsumeResult::replayed:
    return stream_v1::SESSION_ERROR_CODE_TICKET_REPLAYED;
  case QuicTicketConsumeResult::plan_mismatch:
    return stream_v1::SESSION_ERROR_CODE_PLAN_MISMATCH;
  case QuicTicketConsumeResult::unknown:
  case QuicTicketConsumeResult::client_mismatch:
  case QuicTicketConsumeResult::session_mismatch:
  case QuicTicketConsumeResult::expired:
    return stream_v1::SESSION_ERROR_CODE_AUTHENTICATION_FAILED;
  }
  return stream_v1::SESSION_ERROR_CODE_AUTHENTICATION_FAILED;
}

} // namespace

QuicPeerStreamRole classify_peer_stream(std::uint64_t stream_id) noexcept {
  switch (stream_id) {
  case 0:
    return QuicPeerStreamRole::session;
  case 2:
    return QuicPeerStreamRole::input;
  case 6:
    return QuicPeerStreamRole::feedback;
  default:
    return QuicPeerStreamRole::invalid;
  }
}

QuicSessionProtocol::QuicSessionProtocol(
    AuthorizedQuicTicketStore &authorized_tickets)
    : authorized_tickets_(authorized_tickets) {}

void QuicSessionProtocol::set_maximum_datagram_bytes(
    std::uint16_t value) noexcept {
  maximum_datagram_bytes_ = value;
}

QuicSessionProtocolOutput
QuicSessionProtocol::receive(QuicPeerStreamRole role,
                             std::span<const std::byte> bytes,
                             std::uint64_t now_unix_ms) {
  QuicSessionProtocolOutput output;
  std::vector<std::byte> *buffered = nullptr;
  switch (role) {
  case QuicPeerStreamRole::session:
    buffered = &session_bytes_;
    break;
  case QuicPeerStreamRole::input:
    buffered = &input_bytes_;
    break;
  case QuicPeerStreamRole::feedback:
    buffered = &feedback_bytes_;
    break;
  case QuicPeerStreamRole::invalid:
    output.close_connection = true;
    return output;
  }
  constexpr std::size_t maximum_buffered_bytes =
      maximum_stream_message_bytes + 4U;
  if (bytes.size() > maximum_buffered_bytes ||
      buffered->size() > maximum_buffered_bytes - bytes.size()) {
    output.close_connection = true;
    buffered->clear();
    return output;
  }
  buffered->insert(buffered->end(), bytes.begin(), bytes.end());

  while (buffered->size() >= 4) {
    const auto message_bytes =
        read_u32(std::span<const std::byte, 4>{buffered->data(), 4});
    if (message_bytes == 0 || message_bytes > maximum_stream_message_bytes) {
      output.close_connection = true;
      buffered->clear();
      return output;
    }
    const auto frame_bytes = 4U + static_cast<std::size_t>(message_bytes);
    if (buffered->size() < frame_bytes) {
      return output;
    }
    const auto payload =
        std::span<const std::byte>{buffered->data() + 4, message_bytes};

    bool valid = false;
    if (role == QuicPeerStreamRole::session) {
      stream_v1::SessionStreamEnvelope message;
      valid = message.ParseFromArray(payload.data(),
                                     static_cast<int>(payload.size()));
      if (valid && !authenticated_) {
        const bool has_auth =
            message.body_case() ==
            stream_v1::SessionStreamEnvelope::kAuthenticateSession;
        if (!has_auth || message.session_id().empty() ||
            message.sequence() == 0) {
          valid = false;
        } else {
          auto reply = stream_v1::SessionStreamEnvelope{};
          reply.set_protocol_version(1);
          reply.set_session_id(message.session_id());
          reply.set_sequence(message.sequence());
          auto *result = reply.mutable_session_authenticated();
          QuicTicketConsumeResult consumed = QuicTicketConsumeResult::unknown;
          auto *auth = message.mutable_authenticate_session();
          if (message.protocol_version() == 1) {
            consumed = authorized_tickets_.consume(
                {reinterpret_cast<const std::byte *>(
                     auth->stream_ticket().data()),
                 auth->stream_ticket().size()},
                auth->client_id(), message.session_id(), auth->plan_revision(),
                now_unix_ms);
          }
          std::fill(auth->mutable_stream_ticket()->begin(),
                    auth->mutable_stream_ticket()->end(), '\0');
          const auto error =
              message.protocol_version() == 1
                  ? error_for(consumed)
                  : stream_v1::SESSION_ERROR_CODE_UNSUPPORTED_VERSION;
          result->set_accepted(error == stream_v1::SESSION_ERROR_CODE_NONE);
          result->set_error_code(error);
          result->set_maximum_datagram_bytes(maximum_datagram_bytes_);
          auto reply_frame = frame(reply);
          if (reply_frame.empty()) {
            valid = false;
          } else {
            output.session_replies.push_back(std::move(reply_frame));
            authenticated_ = result->accepted();
            if (authenticated_) {
              session_id_ = message.session_id();
              last_session_sequence_ = message.sequence();
            } else {
              output.close_connection = true;
            }
          }
        }
        std::fill(buffered->begin(), buffered->begin() + frame_bytes,
                  std::byte{});
      } else if (valid) {
        const auto body = message.body_case();
        valid = message.protocol_version() == 1 &&
                message.session_id() == session_id_ &&
                message.sequence() > last_session_sequence_ &&
                (body == stream_v1::SessionStreamEnvelope::kStartSession ||
                 body == stream_v1::SessionStreamEnvelope::kStopSession ||
                 body == stream_v1::SessionStreamEnvelope::kRequestIdr);
        if (valid) {
          last_session_sequence_ = message.sequence();
          output.packets.push_back({.channel = stream::StreamChannel::session,
                                    .sequence = message.sequence(),
                                    .payload = std::vector<std::byte>(
                                        payload.begin(), payload.end())});
        }
      }
    } else if (role == QuicPeerStreamRole::input) {
      stream_v1::InputStreamEnvelope message;
      valid = authenticated_ &&
              message.ParseFromArray(payload.data(),
                                     static_cast<int>(payload.size())) &&
              message.protocol_version() == 1 &&
              message.session_id() == session_id_ &&
              message.sequence() > last_input_sequence_ &&
              message.has_input_batch();
      if (valid) {
        last_input_sequence_ = message.sequence();
        output.packets.push_back({.channel = stream::StreamChannel::input,
                                  .sequence = message.sequence(),
                                  .payload = std::vector<std::byte>(
                                      payload.begin(), payload.end())});
      }
    } else {
      stream_v1::FeedbackStreamEnvelope message;
      valid = authenticated_ &&
              message.ParseFromArray(payload.data(),
                                     static_cast<int>(payload.size())) &&
              message.protocol_version() == 1 &&
              message.session_id() == session_id_ &&
              message.sequence() > last_feedback_sequence_ &&
              message.body_case() !=
                  stream_v1::FeedbackStreamEnvelope::BODY_NOT_SET;
      if (valid) {
        last_feedback_sequence_ = message.sequence();
        output.packets.push_back({.channel = stream::StreamChannel::feedback,
                                  .sequence = message.sequence(),
                                  .payload = std::vector<std::byte>(
                                      payload.begin(), payload.end())});
      }
    }
    buffered->erase(buffered->begin(), buffered->begin() + frame_bytes);
    if (!valid) {
      output.close_connection = true;
      buffered->clear();
      return output;
    }
    if (output.close_connection) {
      buffered->clear();
      return output;
    }
  }
  return output;
}

bool QuicSessionProtocol::authenticated() const noexcept {
  return authenticated_;
}

void QuicSessionProtocol::reset() {
  std::fill(session_bytes_.begin(), session_bytes_.end(), std::byte{});
  session_bytes_.clear();
  input_bytes_.clear();
  feedback_bytes_.clear();
  session_id_.clear();
  maximum_datagram_bytes_ = 0;
  last_session_sequence_ = 0;
  last_input_sequence_ = 0;
  last_feedback_sequence_ = 0;
  authenticated_ = false;
}

} // namespace beacon::worker
