#include "stream_core.h"

#include "beacon/stream/frame_assembler.h"

#include <algorithm>
#include <atomic>
#include <limits>
#include <memory>
#include <utility>

namespace beacon::android::streamcore {

namespace stream_v1 = beacon::stream::v1;

#ifndef NDEBUG
namespace {
std::atomic<CloseFaultHook> close_fault_hook;

void inject_close_fault(CloseFaultPoint point) {
  if (const auto hook = close_fault_hook.load(std::memory_order_acquire)) {
    hook(point);
  }
}
}  // namespace

void set_close_fault_hook_for_test(CloseFaultHook hook) noexcept {
  close_fault_hook.store(hook, std::memory_order_release);
}
#endif

std::uint32_t derive_maximum_frame_bytes(std::uint32_t width,
                                         std::uint32_t height) noexcept {
  const std::uint64_t conservative_bytes =
      static_cast<std::uint64_t>(width) * height * 3U;
  return static_cast<std::uint32_t>(std::clamp<std::uint64_t>(
      conservative_bytes, minimum_planned_frame_bytes,
      maximum_planned_frame_bytes));
}

class StreamCore::FrameAssemblerHolder {
 public:
  explicit FrameAssemblerHolder(std::uint32_t maximum_frame_bytes)
      : value(maximum_frame_bytes) {}
  stream::FrameAssembler value;
};

TicketSecret::TicketSecret(std::vector<std::byte> bytes) : bytes_(std::move(bytes)) {}

TicketSecret::TicketSecret(TicketSecret &&other) noexcept
    : bytes_(std::move(other.bytes_)), consumed_(other.consumed_) {
  other.clear();
}

TicketSecret &TicketSecret::operator=(TicketSecret &&other) noexcept {
  if (this != &other) {
    clear();
    bytes_ = std::move(other.bytes_);
    consumed_ = other.consumed_;
    other.clear();
  }
  return *this;
}

TicketSecret::~TicketSecret() { clear(); }

std::span<const std::byte> TicketSecret::bytes() const noexcept { return bytes_; }

bool TicketSecret::consumed() const noexcept { return consumed_; }

void TicketSecret::clear() noexcept {
  volatile std::byte *current = bytes_.data();
  for (std::size_t index = 0; index < bytes_.size(); ++index) {
    current[index] = std::byte{};
  }
  consumed_ = true;
}

StreamCore::StreamCore(Transport &transport, FrameSink &sink,
                       std::uint32_t maximum_frame_bytes)
    : transport_(transport), sink_(sink),
      assembler_(new FrameAssemblerHolder(
          std::min(maximum_frame_bytes, maximum_planned_frame_bytes))),
      maximum_frame_bytes_(
          std::min(maximum_frame_bytes, maximum_planned_frame_bytes)) {}

StreamCore::~StreamCore() {
  release();
  delete assembler_;
}

bool StreamCore::start(ConnectionGrant grant) {
  if (state_ != State::idle && state_ != State::stopped && state_ != State::failed) {
    return false;
  }
  delete assembler_;
  grant_ = std::move(grant);
  const auto selected_limit = derive_maximum_frame_bytes(
      grant_.video.width, grant_.video.height);
  assembler_ = new FrameAssemblerHolder(
      std::min(maximum_frame_bytes_, selected_limit));
  session_bytes_.clear();
  session_sequence_ = 0;
  input_sequence_ = 0;
  feedback_sequence_ = 0;
  last_complete_sequence_ = 0;
  shutdown_ = false;
  transition(State::connecting);
  if (!transport_.connect(grant_.endpoint)) {
    fail();
    return false;
  }
  return true;
}

bool StreamCore::on_connected() {
  if (state_ != State::connecting ||
      !transport_.open_stream(StreamRole::session) ||
      !transport_.open_stream(StreamRole::input) ||
      !transport_.open_stream(StreamRole::feedback)) {
    fail();
    return false;
  }
  transition(State::authenticating);
  return send_authenticate();
}

bool StreamCore::receive_session(std::span<const std::byte> bytes) {
  if (state_ != State::authenticating) {
    fail();
    return false;
  }
  session_bytes_.insert(session_bytes_.end(), bytes.begin(), bytes.end());
  while (session_bytes_.size() >= 4) {
    const auto length =
        (std::to_integer<std::uint32_t>(session_bytes_[0]) << 24U) |
        (std::to_integer<std::uint32_t>(session_bytes_[1]) << 16U) |
        (std::to_integer<std::uint32_t>(session_bytes_[2]) << 8U) |
        std::to_integer<std::uint32_t>(session_bytes_[3]);
    if (length > 1024U * 1024U) {
      fail();
      return false;
    }
    if (session_bytes_.size() < static_cast<std::size_t>(length) + 4U) {
      return true;
    }
    stream_v1::SessionStreamEnvelope reply;
    const bool parsed = length <= static_cast<std::uint32_t>(std::numeric_limits<int>::max()) &&
                        reply.ParseFromArray(session_bytes_.data() + 4,
                                             static_cast<int>(length));
    session_bytes_.erase(session_bytes_.begin(),
                         session_bytes_.begin() + 4U + length);
    if (state_ != State::authenticating || !parsed ||
        reply.protocol_version() != 1 ||
        reply.session_id() != grant_.session_id ||
        reply.sequence() != 1 ||
        reply.body_case() != stream_v1::SessionStreamEnvelope::kSessionAuthenticated ||
        !reply.session_authenticated().accepted()) {
      fail();
      return false;
    }
    if (!send_start()) {
      return false;
    }
  }
  return true;
}

bool StreamCore::receive_datagram(std::span<const std::byte> bytes) {
  if (state_ != State::streaming) {
    return false;
  }
  auto result = assembler_->value.push(bytes);
  if (!drain_assembler_events()) return false;
  if (!result.frame.has_value()) {
    return result.status == stream::FramePushStatus::accepted_incomplete;
  }
  auto &frame = *result.frame;
  last_complete_sequence_ = frame.sequence;
  const bool idr = (static_cast<std::uint16_t>(frame.flags) &
                    static_cast<std::uint16_t>(stream::MediaDatagramFlags::idr)) != 0;
  sink_.frame({.bytes = std::move(frame.bytes),
               .presentation_time_us = frame.presentation_time_us,
               .sequence = frame.sequence,
               .idr = idr});
  return true;
}

bool StreamCore::drain_assembler_events() {
  const auto events = assembler_->value.take_events();
  const bool idr_requested = std::ranges::any_of(events, [](const auto &event) {
    return event.kind == stream::FrameAssemblerEventKind::idr_requested;
  });
  const bool idr_recovered = std::ranges::any_of(events, [](const auto &event) {
    return event.kind == stream::FrameAssemblerEventKind::idr_recovered;
  });
  if (idr_requested && !idr_recovered && !send_request_idr()) {
    return false;
  }
  return true;
}

bool StreamCore::send_request_idr() {
  stream_v1::SessionStreamEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_session_id(grant_.session_id);
  envelope.set_sequence(++session_sequence_);
  auto *request = envelope.mutable_request_idr();
  request->set_reason(stream_v1::IDR_REQUEST_REASON_FRAME_EVICTED);
  request->set_last_complete_sequence(last_complete_sequence_);
  if (!transport_.send(StreamRole::session, frame_message(envelope))) {
    fail();
    return false;
  }
  return true;
}

bool StreamCore::send_input(const stream_v1::InputBatch &input) {
  if (state_ != State::streaming || input.events().empty()) {
    return false;
  }
  stream_v1::InputStreamEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_session_id(grant_.session_id);
  envelope.set_sequence(++input_sequence_);
  envelope.mutable_input_batch()->CopyFrom(input);
  return transport_.send(StreamRole::input, frame_message(envelope));
}

bool StreamCore::send_feedback(const stream_v1::QueueDepthFeedback &feedback) {
  if (state_ != State::streaming) {
    return false;
  }
  stream_v1::FeedbackStreamEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_session_id(grant_.session_id);
  envelope.set_sequence(++feedback_sequence_);
  envelope.mutable_queue_depth()->CopyFrom(feedback);
  return transport_.send(StreamRole::feedback, frame_message(envelope));
}

void StreamCore::on_connection_lost() {
  if (state_ != State::released && state_ != State::stopped) {
    fail();
  }
}

void StreamCore::stop() noexcept {
  if (state_ == State::released || state_ == State::stopped) {
    return;
  }
  if (state_ == State::streaming) {
    try {
#ifndef NDEBUG
      inject_close_fault(CloseFaultPoint::stop_envelope_allocation);
#endif
      stream_v1::SessionStreamEnvelope envelope;
      envelope.set_protocol_version(1);
      envelope.set_session_id(grant_.session_id);
      envelope.set_sequence(++session_sequence_);
      envelope.mutable_stop_session()->set_reason(
          stream_v1::SESSION_STOP_REASON_CLIENT_REQUEST);
#ifndef NDEBUG
      inject_close_fault(CloseFaultPoint::stop_serialization);
#endif
      transport_.send(StreamRole::session, frame_message(envelope));
    } catch (...) {
    }
  }
  if (!shutdown_) {
    shutdown_ = true;
    try {
      transport_.shutdown();
    } catch (...) {
    }
  }
  state_ = State::stopped;
  try {
    sink_.state_changed(State::stopped);
  } catch (...) {
  }
}

void StreamCore::release() noexcept {
  if (released_) {
    return;
  }
  stop();
  released_ = true;
  try {
    transport_.release();
  } catch (...) {
  }
  state_ = State::released;
  try {
    sink_.state_changed(State::released);
  } catch (...) {
  }
}

State StreamCore::state() const noexcept { return state_; }

bool StreamCore::ticket_consumed() const noexcept { return grant_.ticket.consumed(); }

template <typename Message>
std::vector<std::byte> StreamCore::frame_message(const Message &message) {
  const auto length = static_cast<std::uint32_t>(message.ByteSizeLong());
  std::vector<std::byte> result(4U + length);
  result[0] = static_cast<std::byte>(length >> 24U);
  result[1] = static_cast<std::byte>(length >> 16U);
  result[2] = static_cast<std::byte>(length >> 8U);
  result[3] = static_cast<std::byte>(length);
  message.SerializeToArray(result.data() + 4, static_cast<int>(length));
  return result;
}

bool StreamCore::send_authenticate() {
  stream_v1::SessionStreamEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_session_id(grant_.session_id);
  envelope.set_sequence(++session_sequence_);
  auto *auth = envelope.mutable_authenticate_session();
  auth->set_client_id(grant_.client_id);
  auth->set_plan_revision(grant_.plan_revision);
  const auto ticket = grant_.ticket.bytes();
  auth->set_stream_ticket(ticket.data(), ticket.size());
  const bool sent = transport_.send(StreamRole::session, frame_message(envelope));
  std::fill(auth->mutable_stream_ticket()->begin(),
            auth->mutable_stream_ticket()->end(), '\0');
  grant_.ticket.clear();
  if (!sent) {
    fail();
  }
  return sent;
}

bool StreamCore::send_start() {
  stream_v1::SessionStreamEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_session_id(grant_.session_id);
  envelope.set_sequence(++session_sequence_);
  auto *video = envelope.mutable_start_session()->mutable_selected_video();
  video->set_codec(grant_.video.codec);
  video->set_width(grant_.video.width);
  video->set_height(grant_.video.height);
  video->set_frames_per_second_numerator(grant_.video.fps_numerator);
  video->set_frames_per_second_denominator(grant_.video.fps_denominator);
  video->set_dynamic_range(grant_.video.dynamic_range);
  if (!transport_.send(StreamRole::session, frame_message(envelope))) {
    fail();
    return false;
  }
  transition(State::streaming);
  return true;
}

void StreamCore::transition(State state) {
  state_ = state;
  sink_.state_changed(state);
}

void StreamCore::fail() {
  if (!shutdown_) {
    shutdown_ = true;
    transport_.shutdown();
  }
  transition(State::failed);
}

}  // namespace beacon::android::streamcore
