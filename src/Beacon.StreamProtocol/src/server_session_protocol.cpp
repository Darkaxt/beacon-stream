#include "beacon/stream/server_session_protocol.h"

#include "stream_control.pb.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <utility>

namespace beacon::stream {
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

stream_v1::SessionErrorCode
error_for(StreamTicketAuthorizationResult result) noexcept {
  switch (result) {
  case StreamTicketAuthorizationResult::accepted:
    return stream_v1::SESSION_ERROR_CODE_NONE;
  case StreamTicketAuthorizationResult::replayed:
    return stream_v1::SESSION_ERROR_CODE_TICKET_REPLAYED;
  case StreamTicketAuthorizationResult::plan_mismatch:
    return stream_v1::SESSION_ERROR_CODE_PLAN_MISMATCH;
  case StreamTicketAuthorizationResult::unknown:
  case StreamTicketAuthorizationResult::client_mismatch:
  case StreamTicketAuthorizationResult::session_mismatch:
  case StreamTicketAuthorizationResult::expired:
    return stream_v1::SESSION_ERROR_CODE_AUTHENTICATION_FAILED;
  }
  return stream_v1::SESSION_ERROR_CODE_AUTHENTICATION_FAILED;
}

bool valid_benchmark_round(const stream_v1::BenchmarkRoundPlan &round,
                           std::uint32_t maximum_payload_bytes) noexcept {
  return round.packet_count() != 0 && round.payload_bytes() != 0 &&
         round.payload_bytes() <= maximum_payload_bytes &&
         round.measurement_interval_us() != 0;
}

bool valid_benchmark_start(const stream_v1::StartBenchmark &benchmark,
                           std::uint16_t maximum_datagram_bytes) noexcept {
  return !benchmark.run_id().empty() && benchmark.schema_version() != 0 &&
         benchmark.run_token().size() == 16 &&
         valid_benchmark_round(benchmark.reliable_round(),
                               maximum_stream_message_bytes) &&
         maximum_datagram_bytes != 0 &&
         valid_benchmark_round(benchmark.datagram_round(),
                               maximum_datagram_bytes);
}

bool benchmark_plans_equal(const stream_v1::StartBenchmark &left,
                           const stream_v1::StartBenchmark &right) noexcept {
  const auto rounds_equal = [](const stream_v1::BenchmarkRoundPlan &first,
                               const stream_v1::BenchmarkRoundPlan &second) {
    return first.packet_count() == second.packet_count() &&
           first.payload_bytes() == second.payload_bytes() &&
           first.measurement_interval_us() == second.measurement_interval_us();
  };
  return left.run_id() == right.run_id() &&
         left.schema_version() == right.schema_version() &&
         left.run_token() == right.run_token() &&
         rounds_equal(left.reliable_round(), right.reliable_round()) &&
         rounds_equal(left.datagram_round(), right.datagram_round());
}

bool video_modes_equal(const stream_v1::SelectedVideoMode &left,
                       const stream_v1::SelectedVideoMode &right) noexcept {
  return left.codec() == right.codec() && left.width() == right.width() &&
         left.height() == right.height() &&
         left.frames_per_second_numerator() ==
             right.frames_per_second_numerator() &&
         left.frames_per_second_denominator() ==
             right.frames_per_second_denominator() &&
         left.dynamic_range() == right.dynamic_range();
}

bool audio_modes_equal(const stream_v1::SelectedAudioMode &left,
                       const stream_v1::SelectedAudioMode &right) noexcept {
  return left.codec() == right.codec() &&
         left.sample_rate_hz() == right.sample_rate_hz() &&
         left.channel_count() == right.channel_count() &&
         left.frame_duration_us() == right.frame_duration_us() &&
         left.bitrate_bps() == right.bitrate_bps();
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

ServerSessionProtocol::ServerSessionProtocol(
    IStreamTicketAuthorizer &ticket_authorizer,
    SecureClearObserver session_wipe_observer, void *session_wipe_context)
    : ticket_authorizer_(ticket_authorizer),
      session_wipe_observer_(session_wipe_observer),
      session_wipe_context_(session_wipe_context) {}

ServerSessionProtocol::~ServerSessionProtocol() { reset(); }

void ServerSessionProtocol::set_maximum_datagram_bytes(
    std::uint16_t value) noexcept {
  maximum_datagram_bytes_ = value;
}

void ServerSessionProtocol::begin_connection(
    std::uint64_t connection_generation) {
  reset();
  active_connection_generation_ = connection_generation;
}

ServerSessionProtocolOutput ServerSessionProtocol::receive(
    std::uint64_t connection_generation, QuicPeerStreamRole role,
    std::span<const std::byte> bytes, std::uint64_t now_unix_ms) {
  if (connection_generation == 0 ||
      connection_generation != active_connection_generation_) {
    ServerSessionProtocolOutput output;
    output.stale_callback = true;
    return output;
  }
  return receive(role, bytes, now_unix_ms);
}

ServerSessionProtocolOutput
ServerSessionProtocol::receive(QuicPeerStreamRole role,
                               std::span<const std::byte> bytes,
                               std::uint64_t now_unix_ms) {
  ServerSessionProtocolOutput output;
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
    output.connection_disposition =
        ServerConnectionDisposition::protocol_failure;
    clear_stream_bytes();
    return output;
  }
  constexpr std::size_t maximum_buffered_bytes =
      maximum_stream_message_bytes + 4U;
  if (bytes.size() > maximum_buffered_bytes ||
      buffered->size() > maximum_buffered_bytes - bytes.size()) {
    output.connection_disposition =
        ServerConnectionDisposition::protocol_failure;
    clear_stream_bytes();
    return output;
  }
  buffered->insert(buffered->end(), bytes.begin(), bytes.end());

  while (buffered->size() >= 4) {
    const auto message_bytes =
        read_u32(std::span<const std::byte, 4>{buffered->data(), 4});
    if (message_bytes == 0 || message_bytes > maximum_stream_message_bytes) {
      output.connection_disposition =
          ServerConnectionDisposition::protocol_failure;
      clear_stream_bytes();
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
          StreamTicketAuthorization consumed;
          auto *auth = message.mutable_authenticate_session();
          if (message.protocol_version() == 1) {
            consumed = ticket_authorizer_.authorize(
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
                  ? error_for(consumed.result)
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
              authorized_video_plan_ = std::move(consumed.selected_video);
              authorized_audio_plan_ = std::move(consumed.selected_audio);
              authorized_benchmark_plan_ = std::move(consumed.benchmark_plan);
              session_id_ = message.session_id();
              last_session_sequence_ = message.sequence();
              current_generation_ = ++next_generation_;
              output.accepted_authentication =
                  ServerSessionProtocolOutput::AcceptedAuthentication{
                      .session_id = session_id_,
                      .session_generation = current_generation_,
                      .maximum_datagram_bytes = maximum_datagram_bytes_};
            } else {
              output.connection_disposition =
                  ServerConnectionDisposition::protocol_failure;
            }
          }
        }
      } else if (valid) {
        const auto body = message.body_case();
        valid = message.protocol_version() == 1 &&
                message.session_id() == session_id_ &&
                message.sequence() > last_session_sequence_ &&
                (body == stream_v1::SessionStreamEnvelope::kStartSession ||
                 body == stream_v1::SessionStreamEnvelope::kStartBenchmark ||
                 body == stream_v1::SessionStreamEnvelope::kCancelBenchmark ||
                 body == stream_v1::SessionStreamEnvelope::kStopSession ||
                 body == stream_v1::SessionStreamEnvelope::kRequestIdr);
        if (valid && body == stream_v1::SessionStreamEnvelope::kStartSession) {
          valid = !started_ && authorized_video_plan_.has_value() &&
                  authorized_audio_plan_.has_value() &&
                  !authorized_benchmark_plan_.has_value() &&
                  message.start_session().has_selected_video() &&
                  message.start_session().has_selected_audio() &&
                  video_modes_equal(message.start_session().selected_video(),
                                    *authorized_video_plan_) &&
                  audio_modes_equal(message.start_session().selected_audio(),
                                    *authorized_audio_plan_);
        } else if (valid &&
                   body == stream_v1::SessionStreamEnvelope::kStartBenchmark) {
          valid = !started_ && authorized_benchmark_plan_.has_value() &&
                  valid_benchmark_start(message.start_benchmark(),
                                        maximum_datagram_bytes_) &&
                  benchmark_plans_equal(message.start_benchmark(),
                                        *authorized_benchmark_plan_);
        } else if (valid &&
                   body == stream_v1::SessionStreamEnvelope::kCancelBenchmark) {
          valid = started_ && !benchmark_run_id_.empty() &&
                  message.cancel_benchmark().run_id() == benchmark_run_id_;
        } else if (valid &&
                   body == stream_v1::SessionStreamEnvelope::kStopSession) {
          valid = started_ &&
                  message.stop_session().reason() !=
                      stream_v1::SESSION_STOP_REASON_UNSPECIFIED;
        } else if (valid &&
                   body == stream_v1::SessionStreamEnvelope::kRequestIdr) {
          valid = started_ && benchmark_run_id_.empty() &&
                  message.request_idr().reason() !=
                      stream_v1::IDR_REQUEST_REASON_UNSPECIFIED;
        }
        if (valid) {
          last_session_sequence_ = message.sequence();
          if (body == stream_v1::SessionStreamEnvelope::kStartSession) {
            started_ = true;
            output.accepted_session_actions.emplace_back(
                ServerSessionProtocolOutput::AcceptedStartSession{
                    .session_id = session_id_,
                    .session_generation = current_generation_,
                    .maximum_datagram_bytes = maximum_datagram_bytes_,
                    .start_session = message.start_session()});
          } else if (body ==
                     stream_v1::SessionStreamEnvelope::kStartBenchmark) {
            started_ = true;
            benchmark_run_id_ = message.start_benchmark().run_id();
            output.accepted_session_actions.emplace_back(
                ServerSessionProtocolOutput::AcceptedStartBenchmark{
                    .session_id = session_id_,
                    .session_generation = current_generation_,
                    .maximum_datagram_bytes = maximum_datagram_bytes_,
                    .start_benchmark = message.start_benchmark()});
          } else if (body ==
                     stream_v1::SessionStreamEnvelope::kCancelBenchmark) {
            output.accepted_session_actions.emplace_back(
                ServerSessionProtocolOutput::AcceptedCancelBenchmark{
                    .session_generation = current_generation_,
                    .cancel_benchmark = message.cancel_benchmark()});
            benchmark_run_id_.clear();
            started_ = false;
          } else if (body == stream_v1::SessionStreamEnvelope::kStopSession) {
            output.accepted_session_actions.emplace_back(
                ServerSessionProtocolOutput::AcceptedStopSession{
                    .session_generation = current_generation_,
                    .stop_session = message.stop_session()});
            started_ = false;
            benchmark_run_id_.clear();
            output.connection_disposition =
                ServerConnectionDisposition::session_complete;
          } else if (body == stream_v1::SessionStreamEnvelope::kRequestIdr) {
            output.accepted_session_actions.emplace_back(
                ServerSessionProtocolOutput::AcceptedIdrRequest{
                    .session_generation = current_generation_,
                    .request = message.request_idr()});
          }
          output.packets.push_back({.channel = StreamChannel::session,
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
        output.inputs.push_back(
            {.session_generation = current_generation_, .input = message});
        output.packets.push_back({.channel = StreamChannel::input,
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
        output.feedback.push_back(
            {.session_generation = current_generation_, .feedback = message});
        output.packets.push_back({.channel = StreamChannel::feedback,
                                  .sequence = message.sequence(),
                                  .payload = std::vector<std::byte>(
                                      payload.begin(), payload.end())});
      }
    }
    if (!valid) {
      output.connection_disposition =
          ServerConnectionDisposition::protocol_failure;
      clear_stream_bytes();
      return output;
    }
    if (output.should_close_connection()) {
      clear_stream_bytes();
      return output;
    }
    if (role == QuicPeerStreamRole::session) {
      consume_session_prefix(frame_bytes);
    } else {
      buffered->erase(buffered->begin(), buffered->begin() + frame_bytes);
    }
  }
  return output;
}

bool ServerSessionProtocol::authenticated() const noexcept {
  return authenticated_;
}

void ServerSessionProtocol::clear_stream_bytes() noexcept {
  secure_clear_bytes(session_bytes_, session_wipe_observer_,
                     session_wipe_context_);
  secure_clear_bytes(input_bytes_);
  secure_clear_bytes(feedback_bytes_);
}

void ServerSessionProtocol::consume_session_prefix(
    std::size_t bytes) noexcept {
  const auto remaining = session_bytes_.size() - bytes;
  if (remaining != 0) {
    std::memmove(session_bytes_.data(), session_bytes_.data() + bytes,
                 remaining);
  }
  secure_wipe_bytes(
      std::span<std::byte>{session_bytes_}.subspan(remaining, bytes),
      session_wipe_observer_, session_wipe_context_);
  session_bytes_.resize(remaining);
}

void ServerSessionProtocol::reset() noexcept {
  clear_stream_bytes();
  session_id_.clear();
  benchmark_run_id_.clear();
  authorized_video_plan_.reset();
  authorized_audio_plan_.reset();
  authorized_benchmark_plan_.reset();
  maximum_datagram_bytes_ = 0;
  last_session_sequence_ = 0;
  last_input_sequence_ = 0;
  last_feedback_sequence_ = 0;
  current_generation_ = 0;
  active_connection_generation_ = 0;
  authenticated_ = false;
  started_ = false;
}

} // namespace beacon::stream
