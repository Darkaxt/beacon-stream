#include "hosted_emulator_endpoint_server.h"

#include "beacon/stream/secure_bytes.h"
#include "beacon/stream/server_session_protocol.h"
#include "beacon/stream/video_media_packetizer.h"

#include <msquic.h>

#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstdio>
#include <cstring>
#include <limits>
#include <mutex>
#include <new>
#include <string>
#include <type_traits>
#include <utility>
#include <variant>

namespace beacon::stream::testing {
namespace {

constexpr std::string_view beacon_alpn{"beacon-stream/1"};
constexpr std::string_view expected_ticket{"beacon-hosted-emulator-ticket-v1"};
constexpr std::string_view expected_client{"hosted-emulator"};
constexpr std::string_view expected_session{"hosted-emulator-stream"};
constexpr std::uint64_t expected_plan_revision{1};
constexpr std::size_t expected_frame_count{30};
constexpr std::uint64_t frame_interval_us{1'000'000U / 30U};

bool ticket_matches(std::span<const std::byte> ticket) noexcept {
  if (ticket.size() != expected_ticket.size()) {
    return false;
  }
  unsigned char difference = 0;
  for (std::size_t index = 0; index < ticket.size(); ++index) {
    difference = static_cast<unsigned char>(
        difference | (std::to_integer<unsigned char>(ticket[index]) ^
                      static_cast<unsigned char>(expected_ticket[index])));
  }
  return difference == 0;
}

v1::SelectedVideoMode fixed_video_mode() {
  v1::SelectedVideoMode mode;
  mode.set_codec(v1::VIDEO_CODEC_H264);
  mode.set_width(640);
  mode.set_height(360);
  mode.set_frames_per_second_numerator(30);
  mode.set_frames_per_second_denominator(1);
  mode.set_dynamic_range(v1::DYNAMIC_RANGE_SDR);
  return mode;
}

StreamTicketAuthorization
authorization_failure(StreamTicketAuthorizationResult result) {
  return {.result = result,
          .selected_video = std::nullopt,
          .benchmark_plan = std::nullopt};
}

HostedEmulatorFrameFlowResult
flow_failure(HostedEmulatorFrameFlowError error) noexcept {
  return {.error = error,
          .next_access_unit_index = std::nullopt,
          .rendering_complete = false};
}

std::uint64_t now_unix_ms() noexcept {
  return static_cast<std::uint64_t>(
      std::chrono::duration_cast<std::chrono::milliseconds>(
          std::chrono::system_clock::now().time_since_epoch())
          .count());
}

} // namespace

StreamTicketAuthorization HostedEmulatorTicketAuthorizer::authorize(
    std::span<const std::byte> ticket, std::string_view client_id,
    std::string_view session_id, std::uint64_t plan_revision,
    std::uint64_t now_unix_ms_value) {
  static_cast<void>(now_unix_ms_value);
  if (!ticket_matches(ticket)) {
    return authorization_failure(StreamTicketAuthorizationResult::unknown);
  }
  if (client_id != expected_client) {
    return authorization_failure(
        StreamTicketAuthorizationResult::client_mismatch);
  }
  if (session_id != expected_session) {
    return authorization_failure(
        StreamTicketAuthorizationResult::session_mismatch);
  }
  if (plan_revision != expected_plan_revision) {
    return authorization_failure(
        StreamTicketAuthorizationResult::plan_mismatch);
  }
  if (consumed_) {
    return authorization_failure(StreamTicketAuthorizationResult::replayed);
  }
  consumed_ = true;
  return {.result = StreamTicketAuthorizationResult::accepted,
          .selected_video = fixed_video_mode(),
          .benchmark_plan = std::nullopt};
}

HostedEmulatorFrameFlow::HostedEmulatorFrameFlow(
    std::size_t frame_count) noexcept
    : frame_count_(frame_count) {}

HostedEmulatorFrameFlowResult HostedEmulatorFrameFlow::start() noexcept {
  if (frame_count_ == 0) {
    return flow_failure(HostedEmulatorFrameFlowError::invalid_frame_count);
  }
  if (started_) {
    return flow_failure(HostedEmulatorFrameFlowError::already_started);
  }
  started_ = true;
  sent_frames_ = 1;
  return {.error = HostedEmulatorFrameFlowError::none,
          .next_access_unit_index = 0,
          .rendering_complete = false};
}

HostedEmulatorFrameFlowResult
HostedEmulatorFrameFlow::rendered(std::uint64_t frame_sequence) noexcept {
  if (!started_) {
    return flow_failure(HostedEmulatorFrameFlowError::not_started);
  }
  const auto expected_sequence = rendered_feedback_ + 1U;
  if (rendered_feedback_ >= sent_frames_ ||
      frame_sequence != expected_sequence) {
    return flow_failure(HostedEmulatorFrameFlowError::unexpected_sequence);
  }

  ++rendered_feedback_;
  if (rendered_feedback_ == frame_count_) {
    return {.error = HostedEmulatorFrameFlowError::none,
            .next_access_unit_index = std::nullopt,
            .rendering_complete = true};
  }
  if (sent_frames_ >= frame_count_) {
    return flow_failure(HostedEmulatorFrameFlowError::unexpected_sequence);
  }
  const auto next_index = sent_frames_;
  ++sent_frames_;
  return {.error = HostedEmulatorFrameFlowError::none,
          .next_access_unit_index = next_index,
          .rendering_complete = false};
}

HostedEmulatorFrameFlowError HostedEmulatorFrameFlow::stop() noexcept {
  if (!started_) {
    return HostedEmulatorFrameFlowError::not_started;
  }
  if (sent_frames_ != frame_count_) {
    return HostedEmulatorFrameFlowError::rendering_incomplete;
  }
  return HostedEmulatorFrameFlowError::none;
}

std::size_t HostedEmulatorFrameFlow::sent_frames() const noexcept {
  return sent_frames_;
}

std::size_t HostedEmulatorFrameFlow::rendered_feedback() const noexcept {
  return rendered_feedback_;
}

class HostedEmulatorEndpointServer::Impl final {
public:
  explicit Impl(HostedEmulatorEndpointConfig config)
      : config_(std::move(config)), flow_(config_.access_units.size()),
        protocol_(authorizer_) {}

  ~Impl() { shutdown_and_release(); }

  int run() {
    bool already_run = false;
    {
      std::lock_guard lock{mutex_};
      already_run = run_called_;
      if (already_run) {
        set_failure_locked(
            HostedEmulatorEndpointFailure::invalid_configuration);
      } else {
        run_called_ = true;
      }
    }
    if (already_run) {
      return report_failure();
    }

    if (!valid_configuration() || !initialize_msquic() || !start_listener()) {
      shutdown_and_release();
      return report_failure();
    }

    std::printf("BEACON_HOSTED_ENDPOINT_READY %u\n",
                static_cast<unsigned int>(local_port_));
    std::fflush(stdout);

    {
      std::unique_lock lock{mutex_};
      changed_.wait(lock, [this] {
        return terminal_ && active_callbacks_ == 0 && open_peer_streams_ == 0 &&
               pending_datagrams_ == 0;
      });
    }

    const bool success = successful_completion();
    shutdown_and_release();
    if (!success) {
      return report_failure();
    }

    std::printf("BEACON_HOSTED_ENDPOINT_AUTHENTICATED 1\n");
    std::printf("BEACON_HOSTED_ENDPOINT_FRAMES %zu\n", flow_.sent_frames());
    std::printf("BEACON_HOSTED_ENDPOINT_RENDERED_FEEDBACK %zu\n",
                flow_.rendered_feedback());
    std::printf("BEACON_HOSTED_ENDPOINT_STOPPED 1\n");
    std::fflush(stdout);
    return 0;
  }

  HostedEmulatorEndpointFailure failure() const noexcept {
    try {
      std::lock_guard lock{mutex_};
      return failure_;
    } catch (...) {
      return HostedEmulatorEndpointFailure::callback_exception;
    }
  }

private:
  struct CallbackGuard {
    explicit CallbackGuard(Impl &owner_value) : owner(owner_value) {
      std::lock_guard lock{owner.mutex_};
      ++owner.active_callbacks_;
    }

    ~CallbackGuard() {
      {
        std::lock_guard lock{owner.mutex_};
        if (owner.active_callbacks_ != 0) {
          --owner.active_callbacks_;
        }
      }
      owner.changed_.notify_all();
    }

    CallbackGuard(const CallbackGuard &) = delete;
    CallbackGuard &operator=(const CallbackGuard &) = delete;

    Impl &owner;
  };

  struct PeerStreamContext {
    Impl *owner{};
    HQUIC connection{};
    HQUIC stream{};
    QuicPeerStreamRole role{QuicPeerStreamRole::invalid};
    std::uint64_t connection_generation{};
  };

  struct StreamSendContext {
    explicit StreamSendContext(std::vector<std::byte> value)
        : bytes(std::move(value)) {
      buffer.Length = static_cast<std::uint32_t>(bytes.size());
      buffer.Buffer = reinterpret_cast<std::uint8_t *>(bytes.data());
    }

    std::vector<std::byte> bytes;
    QUIC_BUFFER buffer{};
  };

  struct DatagramSendContext {
    explicit DatagramSendContext(std::vector<std::byte> value)
        : bytes(std::move(value)) {
      buffer.Length = static_cast<std::uint32_t>(bytes.size());
      buffer.Buffer = reinterpret_cast<std::uint8_t *>(bytes.data());
    }

    std::vector<std::byte> bytes;
    QUIC_BUFFER buffer{};
  };

  bool valid_configuration() {
    std::error_code certificate_error;
    std::error_code key_error;
    const bool certificate_exists = std::filesystem::is_regular_file(
        config_.certificate_path, certificate_error);
    const bool key_exists =
        std::filesystem::is_regular_file(config_.private_key_path, key_error);
    const bool units_valid =
        config_.access_units.size() == expected_frame_count &&
        std::ranges::all_of(config_.access_units,
                            [](const auto &unit) { return !unit.empty(); });
    if (certificate_error || key_error || !certificate_exists || !key_exists ||
        !units_valid) {
      std::lock_guard lock{mutex_};
      set_failure_locked(HostedEmulatorEndpointFailure::invalid_configuration);
      return false;
    }
    return true;
  }

  bool initialize_msquic() {
    const auto open_status = MsQuicOpen2(&api_);
    if (QUIC_FAILED(open_status)) {
      api_ = nullptr;
      set_initialization_failure(HostedEmulatorEndpointFailure::msquic_open);
      return false;
    }

    const QUIC_REGISTRATION_CONFIG registration_config{
        "beacon-hosted-emulator-endpoint", QUIC_EXECUTION_PROFILE_LOW_LATENCY};
    const auto registration_status =
        api_->RegistrationOpen(&registration_config, &registration_);
    if (QUIC_FAILED(registration_status)) {
      set_initialization_failure(
          HostedEmulatorEndpointFailure::registration_open);
      return false;
    }

    QUIC_SETTINGS settings{};
    settings.IdleTimeoutMs = 0;
    settings.IsSet.IdleTimeoutMs = true;
    settings.PeerBidiStreamCount = 1;
    settings.IsSet.PeerBidiStreamCount = true;
    settings.PeerUnidiStreamCount = 2;
    settings.IsSet.PeerUnidiStreamCount = true;
    settings.DatagramReceiveEnabled = true;
    settings.IsSet.DatagramReceiveEnabled = true;
    const QUIC_BUFFER alpn{static_cast<std::uint32_t>(beacon_alpn.size()),
                           reinterpret_cast<std::uint8_t *>(
                               const_cast<char *>(beacon_alpn.data()))};
    const auto configuration_status =
        api_->ConfigurationOpen(registration_, &alpn, 1, &settings,
                                sizeof(settings), nullptr, &configuration_);
    if (QUIC_FAILED(configuration_status)) {
      set_initialization_failure(
          HostedEmulatorEndpointFailure::configuration_open);
      return false;
    }

    private_key_path_ = config_.private_key_path.string();
    certificate_path_ = config_.certificate_path.string();
    QUIC_CERTIFICATE_FILE certificate_file{private_key_path_.c_str(),
                                           certificate_path_.c_str()};
    QUIC_CREDENTIAL_CONFIG credentials{};
    credentials.Type = QUIC_CREDENTIAL_TYPE_CERTIFICATE_FILE;
    credentials.CertificateFile = &certificate_file;
    const auto credential_status =
        api_->ConfigurationLoadCredential(configuration_, &credentials);
    if (QUIC_FAILED(credential_status)) {
      set_initialization_failure(
          HostedEmulatorEndpointFailure::credential_load);
      return false;
    }
    return true;
  }

  bool start_listener() {
    const auto open_status =
        api_->ListenerOpen(registration_, listener_callback, this, &listener_);
    if (QUIC_FAILED(open_status)) {
      listener_ = nullptr;
      set_initialization_failure(HostedEmulatorEndpointFailure::listener_open);
      return false;
    }

    QUIC_ADDR address{};
    if (!QuicAddrFromString("127.0.0.1", 0, &address)) {
      set_initialization_failure(
          HostedEmulatorEndpointFailure::listener_address);
      return false;
    }
    const QUIC_BUFFER alpn{static_cast<std::uint32_t>(beacon_alpn.size()),
                           reinterpret_cast<std::uint8_t *>(
                               const_cast<char *>(beacon_alpn.data()))};
    const auto start_status =
        api_->ListenerStart(listener_, &alpn, 1, &address);
    if (QUIC_FAILED(start_status)) {
      set_initialization_failure(HostedEmulatorEndpointFailure::listener_start);
      return false;
    }

    QUIC_ADDR actual{};
    std::uint32_t actual_size = sizeof(actual);
    if (QUIC_FAILED(api_->GetParam(listener_, QUIC_PARAM_LISTENER_LOCAL_ADDRESS,
                                   &actual_size, &actual))) {
      set_initialization_failure(
          HostedEmulatorEndpointFailure::listener_address);
      return false;
    }
    local_port_ = QuicAddrGetPort(&actual);
    if (local_port_ == 0) {
      set_initialization_failure(
          HostedEmulatorEndpointFailure::listener_address);
      return false;
    }
    return true;
  }

  void set_initialization_failure(HostedEmulatorEndpointFailure failure) {
    std::lock_guard lock{mutex_};
    set_failure_locked(failure);
  }

  void set_failure_locked(HostedEmulatorEndpointFailure failure) noexcept {
    if (failure_ == HostedEmulatorEndpointFailure::none) {
      failure_ = failure;
    }
  }

  void fail_connection(HostedEmulatorEndpointFailure failure, HQUIC connection,
                       std::uint64_t error_code) noexcept {
    bool request_shutdown = false;
    try {
      {
        std::lock_guard lock{mutex_};
        set_failure_locked(failure);
        request_shutdown = connection != nullptr && connection_ == connection &&
                           !shutdown_requested_;
        if (request_shutdown) {
          shutdown_requested_ = true;
        }
      }
      changed_.notify_all();
      if (request_shutdown && api_ != nullptr) {
        api_->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                                 error_code);
      }
    } catch (...) {
    }
  }

  bool send_session_reply(HQUIC stream, std::vector<std::byte> reply,
                          bool shutdown_after_send) {
    auto context = std::make_unique<StreamSendContext>(std::move(reply));
    if (shutdown_after_send) {
      std::lock_guard lock{mutex_};
      close_after_session_send_ = true;
    }
    const auto status = api_->StreamSend(
        stream, &context->buffer, 1,
        shutdown_after_send ? QUIC_SEND_FLAG_FIN : QUIC_SEND_FLAG_NONE,
        context.get());
    if (QUIC_FAILED(status)) {
      if (shutdown_after_send) {
        std::lock_guard lock{mutex_};
        close_after_session_send_ = false;
      }
      return false;
    }
    static_cast<void>(context.release());
    return true;
  }

  bool send_access_unit(std::size_t index, HQUIC connection) {
    std::uint16_t maximum_datagram_bytes = 0;
    {
      std::lock_guard lock{mutex_};
      if (connection_ != connection || !authenticated_ || !started_ ||
          index >= config_.access_units.size()) {
        return false;
      }
      maximum_datagram_bytes = protocol_maximum_datagram_bytes_;
    }

    const auto &unit = config_.access_units[index];
    const EncodedVideoAccessUnitView view{
        .bytes = unit,
        .idr = index == 0,
        .codec_configuration = index == 0,
    };
    auto packetized = packetizer_.packetize(
        view, static_cast<std::uint64_t>(index + 1U),
        static_cast<std::uint64_t>(index) * frame_interval_us,
        maximum_datagram_bytes);
    if (packetized.failure != VideoMediaPacketizerFailure::none ||
        packetized.packets.empty()) {
      fail_connection(HostedEmulatorEndpointFailure::packetization, connection,
                      20);
      return false;
    }

    for (auto &packet : packetized.packets) {
      auto context =
          std::make_unique<DatagramSendContext>(std::move(packet.payload));
      {
        std::lock_guard lock{mutex_};
        if (connection_ != connection || shutdown_requested_) {
          return false;
        }
        ++pending_datagrams_;
      }
      const auto status = api_->DatagramSend(
          connection, &context->buffer, 1, QUIC_SEND_FLAG_NONE, context.get());
      if (QUIC_FAILED(status)) {
        {
          std::lock_guard lock{mutex_};
          if (pending_datagrams_ != 0) {
            --pending_datagrams_;
          }
        }
        changed_.notify_all();
        fail_connection(HostedEmulatorEndpointFailure::datagram_send,
                        connection, 21);
        return false;
      }
      static_cast<void>(context.release());
    }
    return true;
  }

  void process_stream_bytes(HQUIC connection, HQUIC stream,
                            QuicPeerStreamRole role,
                            std::uint64_t connection_generation,
                            std::vector<std::byte> bytes) {
    std::optional<std::size_t> next_access_unit;
    bool stop_accepted = false;
    bool protocol_failed = false;
    ServerSessionProtocolOutput output;
    {
      std::lock_guard lock{mutex_};
      if (connection_ != connection ||
          connection_generation != connection_generation_) {
        if (role == QuicPeerStreamRole::session) {
          secure_clear_bytes(bytes);
        }
        return;
      }
      if (role == QuicPeerStreamRole::session && !protocol_.authenticated() &&
          protocol_maximum_datagram_bytes_ == 0) {
        constexpr std::size_t maximum_pending =
            maximum_stream_message_bytes + 4U;
        if (bytes.size() > maximum_pending ||
            pending_session_bytes_.size() > maximum_pending - bytes.size()) {
          set_failure_locked(HostedEmulatorEndpointFailure::protocol);
          protocol_failed = true;
        } else {
          pending_session_bytes_.insert(pending_session_bytes_.end(),
                                        bytes.begin(), bytes.end());
        }
        secure_clear_bytes(bytes);
        if (!protocol_failed) {
          return;
        }
      }
      if (!protocol_failed) {
        output = protocol_.receive(connection_generation, role, bytes,
                                   now_unix_ms());
        if (role == QuicPeerStreamRole::session) {
          secure_clear_bytes(bytes);
        }
        if (output.accepted_authentication.has_value()) {
          authenticated_ = true;
        }
        for (const auto &action : output.accepted_session_actions) {
          std::visit(
              [&](const auto &accepted) {
                using Action = std::remove_cvref_t<decltype(accepted)>;
                if constexpr (std::is_same_v<Action,
                                             ServerSessionProtocolOutput::
                                                 AcceptedStartSession>) {
                  const auto started = flow_.start();
                  if (started.error != HostedEmulatorFrameFlowError::none) {
                    set_failure_locked(
                        HostedEmulatorEndpointFailure::unexpected_action);
                    protocol_failed = true;
                  } else {
                    started_ = true;
                    next_access_unit = started.next_access_unit_index;
                  }
                } else if constexpr (std::is_same_v<
                                         Action, ServerSessionProtocolOutput::
                                                     AcceptedStopSession>) {
                  if (flow_.stop() != HostedEmulatorFrameFlowError::none) {
                    set_failure_locked(
                        HostedEmulatorEndpointFailure::unexpected_action);
                    protocol_failed = true;
                  } else {
                    stopped_ = true;
                    stop_accepted = flow_.rendered_feedback() ==
                                    config_.access_units.size();
                  }
                } else {
                  set_failure_locked(
                      HostedEmulatorEndpointFailure::unexpected_action);
                  protocol_failed = true;
                }
              },
              action);
        }
        for (const auto &parsed : output.feedback) {
          if (parsed.feedback.body_case() !=
              v1::FeedbackStreamEnvelope::kRenderedFrame) {
            continue;
          }
          const auto rendered =
              flow_.rendered(parsed.feedback.rendered_frame().frame_sequence());
          if (rendered.error != HostedEmulatorFrameFlowError::none) {
            set_failure_locked(
                HostedEmulatorEndpointFailure::unexpected_feedback);
            protocol_failed = true;
          } else if (rendered.next_access_unit_index.has_value()) {
            next_access_unit = rendered.next_access_unit_index;
          }
          if (rendered.rendering_complete && stopped_) {
            stop_accepted = true;
          }
        }
        if (output.connection_disposition ==
            ServerConnectionDisposition::protocol_failure) {
          set_failure_locked(HostedEmulatorEndpointFailure::protocol);
          protocol_failed = true;
        }
      }
    }

    changed_.notify_all();
    bool reply_failed = false;
    for (std::size_t index = 0; index < output.session_replies.size();
         ++index) {
      const bool close_after = output.should_close_connection() &&
                               index + 1U == output.session_replies.size();
      if (!send_session_reply(stream, std::move(output.session_replies[index]),
                              close_after)) {
        reply_failed = true;
        break;
      }
    }
    if (reply_failed) {
      fail_connection(HostedEmulatorEndpointFailure::session_send, connection,
                      22);
      return;
    }
    if (protocol_failed && output.should_close_connection() &&
        !output.session_replies.empty()) {
      return;
    }
    if (protocol_failed) {
      fail_connection(failure(), connection, 23);
      return;
    }
    if (next_access_unit.has_value() &&
        !send_access_unit(*next_access_unit, connection)) {
      if (failure() == HostedEmulatorEndpointFailure::none) {
        fail_connection(HostedEmulatorEndpointFailure::datagram_send,
                        connection, 24);
      }
      return;
    }
    if (stop_accepted) {
      bool request_shutdown = false;
      {
        std::lock_guard lock{mutex_};
        request_shutdown = connection_ == connection && !shutdown_requested_;
        if (request_shutdown) {
          shutdown_requested_ = true;
        }
      }
      if (request_shutdown) {
        api_->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                                 0);
      }
    }
  }

  static QUIC_STATUS QUIC_API
  listener_callback(HQUIC, void *context, QUIC_LISTENER_EVENT *event) noexcept {
    auto &self = *static_cast<Impl *>(context);
    try {
      CallbackGuard guard{self};
      if (event->Type != QUIC_LISTENER_EVENT_NEW_CONNECTION) {
        return QUIC_STATUS_NOT_SUPPORTED;
      }

      {
        std::lock_guard lock{self.mutex_};
        if (self.connection_ != nullptr || self.terminal_) {
          return QUIC_STATUS_CONNECTION_REFUSED;
        }
        self.connection_ = event->NEW_CONNECTION.Connection;
        self.connection_generation_ = ++self.next_connection_generation_;
        self.protocol_.begin_connection(self.connection_generation_);
        self.pending_session_bytes_.clear();
        self.protocol_maximum_datagram_bytes_ = 0;
        self.authenticated_ = false;
        self.started_ = false;
        self.stopped_ = false;
        self.shutdown_requested_ = false;
      }
      self.api_->SetCallbackHandler(
          event->NEW_CONNECTION.Connection,
          reinterpret_cast<void *>(connection_callback), &self);
      const auto status = self.api_->ConnectionSetConfiguration(
          event->NEW_CONNECTION.Connection, self.configuration_);
      if (QUIC_FAILED(status)) {
        {
          std::lock_guard lock{self.mutex_};
          self.set_failure_locked(
              HostedEmulatorEndpointFailure::connection_configuration);
          self.connection_ = nullptr;
          self.terminal_ = true;
        }
        self.changed_.notify_all();
      }
      return status;
    } catch (...) {
      self.fail_connection(HostedEmulatorEndpointFailure::callback_exception,
                           event->Type == QUIC_LISTENER_EVENT_NEW_CONNECTION
                               ? event->NEW_CONNECTION.Connection
                               : nullptr,
                           30);
      return QUIC_STATUS_OUT_OF_MEMORY;
    }
  }

  static QUIC_STATUS QUIC_API connection_callback(
      HQUIC connection, void *context, QUIC_CONNECTION_EVENT *event) noexcept {
    auto &self = *static_cast<Impl *>(context);
    try {
      CallbackGuard guard{self};
      switch (event->Type) {
      case QUIC_CONNECTION_EVENT_CONNECTED: {
        const std::string_view negotiated{
            reinterpret_cast<const char *>(event->CONNECTED.NegotiatedAlpn),
            event->CONNECTED.NegotiatedAlpnLength};
        if (negotiated != beacon_alpn) {
          self.fail_connection(HostedEmulatorEndpointFailure::unsupported_alpn,
                               connection, 31);
        }
        break;
      }
      case QUIC_CONNECTION_EVENT_DATAGRAM_STATE_CHANGED: {
        std::vector<std::byte> pending;
        HQUIC session_stream = nullptr;
        std::uint64_t generation = 0;
        bool disable_after_authentication = false;
        {
          std::lock_guard lock{self.mutex_};
          if (self.connection_ != connection) {
            break;
          }
          self.protocol_maximum_datagram_bytes_ =
              event->DATAGRAM_STATE_CHANGED.SendEnabled != FALSE
                  ? event->DATAGRAM_STATE_CHANGED.MaxSendLength
                  : 0;
          self.protocol_.set_maximum_datagram_bytes(
              self.protocol_maximum_datagram_bytes_);
          disable_after_authentication =
              event->DATAGRAM_STATE_CHANGED.SendEnabled == FALSE &&
              self.authenticated_;
          if (self.protocol_maximum_datagram_bytes_ != 0 &&
              !self.pending_session_bytes_.empty()) {
            pending = std::move(self.pending_session_bytes_);
            self.pending_session_bytes_.clear();
            session_stream = self.session_stream_;
            generation = self.connection_generation_;
          }
        }
        if (disable_after_authentication) {
          self.fail_connection(HostedEmulatorEndpointFailure::transport,
                               connection, 32);
        } else if (!pending.empty() && session_stream != nullptr) {
          self.process_stream_bytes(connection, session_stream,
                                    QuicPeerStreamRole::session, generation,
                                    std::move(pending));
        }
        break;
      }
      case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
        QUIC_UINT62 stream_id = 0;
        std::uint32_t stream_id_size = sizeof(stream_id);
        const auto id_status = self.api_->GetParam(
            event->PEER_STREAM_STARTED.Stream, QUIC_PARAM_STREAM_ID,
            &stream_id_size, &stream_id);
        const auto role = QUIC_SUCCEEDED(id_status)
                              ? classify_peer_stream(stream_id)
                              : QuicPeerStreamRole::invalid;
        auto context_value = std::make_unique<PeerStreamContext>();
        bool accepted = role != QuicPeerStreamRole::invalid;
        {
          std::lock_guard lock{self.mutex_};
          accepted = accepted && self.connection_ == connection;
          if (role == QuicPeerStreamRole::session) {
            accepted = accepted && self.session_stream_ == nullptr;
            if (accepted) {
              self.session_stream_ = event->PEER_STREAM_STARTED.Stream;
            }
          }
          context_value->owner = &self;
          context_value->connection = connection;
          context_value->stream = event->PEER_STREAM_STARTED.Stream;
          context_value->role = role;
          context_value->connection_generation = self.connection_generation_;
          ++self.open_peer_streams_;
        }
        self.api_->SetCallbackHandler(event->PEER_STREAM_STARTED.Stream,
                                      reinterpret_cast<void *>(stream_callback),
                                      context_value.get());
        static_cast<void>(context_value.release());
        if (!accepted) {
          self.api_->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 33);
          self.fail_connection(
              HostedEmulatorEndpointFailure::invalid_peer_stream, connection,
              33);
        }
        break;
      }
      case QUIC_CONNECTION_EVENT_DATAGRAM_SEND_STATE_CHANGED: {
        auto *send = static_cast<DatagramSendContext *>(
            event->DATAGRAM_SEND_STATE_CHANGED.ClientContext);
        if (send != nullptr && QUIC_DATAGRAM_SEND_STATE_IS_FINAL(
                                   event->DATAGRAM_SEND_STATE_CHANGED.State)) {
          std::unique_ptr<DatagramSendContext> final_send{send};
          bool stopped = false;
          const bool failed = event->DATAGRAM_SEND_STATE_CHANGED.State ==
                                  QUIC_DATAGRAM_SEND_LOST_DISCARDED ||
                              event->DATAGRAM_SEND_STATE_CHANGED.State ==
                                  QUIC_DATAGRAM_SEND_CANCELED;
          {
            std::lock_guard lock{self.mutex_};
            if (self.pending_datagrams_ != 0) {
              --self.pending_datagrams_;
            }
            stopped = self.stopped_;
          }
          self.changed_.notify_all();
          if (failed && !stopped) {
            self.fail_connection(HostedEmulatorEndpointFailure::transport,
                                 connection, 34);
          }
        }
        break;
      }
      case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED:
        self.fail_connection(HostedEmulatorEndpointFailure::protocol,
                             connection, 35);
        break;
      case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT: {
        bool stopped = false;
        {
          std::lock_guard lock{self.mutex_};
          stopped = self.stopped_;
        }
        if (!stopped) {
          self.fail_connection(HostedEmulatorEndpointFailure::transport,
                               connection, 36);
        }
        break;
      }
      case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_PEER: {
        bool stopped = false;
        {
          std::lock_guard lock{self.mutex_};
          stopped = self.stopped_;
        }
        if (!stopped) {
          self.fail_connection(HostedEmulatorEndpointFailure::peer_shutdown,
                               connection, 37);
        }
        break;
      }
      case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        {
          std::lock_guard lock{self.mutex_};
          if (self.connection_ == connection) {
            if (!self.stopped_) {
              self.set_failure_locked(
                  HostedEmulatorEndpointFailure::peer_shutdown);
            }
            self.connection_ = nullptr;
            self.session_stream_ = nullptr;
            self.protocol_.reset();
            secure_clear_bytes(self.pending_session_bytes_);
            self.terminal_ = true;
          }
        }
        self.api_->ConnectionClose(connection);
        self.changed_.notify_all();
        break;
      }
      default:
        break;
      }
      return QUIC_STATUS_SUCCESS;
    } catch (...) {
      self.fail_connection(HostedEmulatorEndpointFailure::callback_exception,
                           connection, 38);
      return QUIC_STATUS_SUCCESS;
    }
  }

  static QUIC_STATUS QUIC_API stream_callback(
      HQUIC stream, void *context, QUIC_STREAM_EVENT *event) noexcept {
    auto *stream_context = static_cast<PeerStreamContext *>(context);
    auto &self = *stream_context->owner;
    try {
      CallbackGuard guard{self};
      switch (event->Type) {
      case QUIC_STREAM_EVENT_RECEIVE: {
        if (event->RECEIVE.TotalBufferLength >
            static_cast<std::uint64_t>(maximum_stream_message_bytes + 4U)) {
          self.fail_connection(HostedEmulatorEndpointFailure::protocol,
                               stream_context->connection, 40);
          break;
        }
        std::vector<std::byte> bytes;
        bytes.reserve(
            static_cast<std::size_t>(event->RECEIVE.TotalBufferLength));
        for (std::uint32_t index = 0; index < event->RECEIVE.BufferCount;
             ++index) {
          const auto &buffer = event->RECEIVE.Buffers[index];
          const auto *begin =
              reinterpret_cast<const std::byte *>(buffer.Buffer);
          bytes.insert(bytes.end(), begin, begin + buffer.Length);
        }
        self.process_stream_bytes(
            stream_context->connection, stream, stream_context->role,
            stream_context->connection_generation, std::move(bytes));
        break;
      }
      case QUIC_STREAM_EVENT_SEND_COMPLETE: {
        auto *send = static_cast<StreamSendContext *>(
            event->SEND_COMPLETE.ClientContext);
        delete send;
        break;
      }
      case QUIC_STREAM_EVENT_SEND_SHUTDOWN_COMPLETE: {
        bool shutdown = false;
        {
          std::lock_guard lock{self.mutex_};
          shutdown =
              self.close_after_session_send_ && self.session_stream_ == stream;
          if (shutdown) {
            self.close_after_session_send_ = false;
          }
        }
        if (shutdown && event->SEND_SHUTDOWN_COMPLETE.Graceful != FALSE) {
          self.fail_connection(self.failure(), stream_context->connection, 41);
        }
        break;
      }
      case QUIC_STREAM_EVENT_PEER_SEND_ABORTED: {
        bool stopped = false;
        {
          std::lock_guard lock{self.mutex_};
          stopped = self.stopped_;
        }
        if (!stopped) {
          self.fail_connection(HostedEmulatorEndpointFailure::peer_shutdown,
                               stream_context->connection, 42);
        }
        break;
      }
      case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
        {
          std::lock_guard lock{self.mutex_};
          if (self.session_stream_ == stream) {
            self.session_stream_ = nullptr;
          }
          if (self.open_peer_streams_ != 0) {
            --self.open_peer_streams_;
          }
        }
        self.api_->StreamClose(stream);
        delete stream_context;
        self.changed_.notify_all();
        break;
      }
      default:
        break;
      }
      return QUIC_STATUS_SUCCESS;
    } catch (...) {
      self.fail_connection(HostedEmulatorEndpointFailure::callback_exception,
                           stream_context->connection, 43);
      return QUIC_STATUS_SUCCESS;
    }
  }

  bool successful_completion() const {
    std::lock_guard lock{mutex_};
    return failure_ == HostedEmulatorEndpointFailure::none && authenticated_ &&
           started_ && stopped_ &&
           flow_.sent_frames() == expected_frame_count &&
           flow_.rendered_feedback() == expected_frame_count;
  }

  int report_failure() const {
    const auto current_failure = failure();
    std::fprintf(stderr, "BEACON_HOSTED_ENDPOINT_FAILED %s\n",
                 hosted_emulator_endpoint_failure_name(current_failure));
    std::fflush(stderr);
    return 1;
  }

  void shutdown_and_release() noexcept {
    try {
      HQUIC connection = nullptr;
      HQUIC listener = nullptr;
      {
        std::lock_guard lock{mutex_};
        connection = connection_;
        if (connection != nullptr && !shutdown_requested_) {
          shutdown_requested_ = true;
        } else {
          connection = nullptr;
        }
        listener = std::exchange(listener_, nullptr);
      }
      if (listener != nullptr && api_ != nullptr) {
        api_->ListenerClose(listener);
      }
      if (connection != nullptr && api_ != nullptr) {
        api_->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                                 50);
        std::unique_lock lock{mutex_};
        changed_.wait(lock, [this] {
          return connection_ == nullptr && active_callbacks_ == 0 &&
                 open_peer_streams_ == 0 && pending_datagrams_ == 0;
        });
      }
      if (configuration_ != nullptr && api_ != nullptr) {
        api_->ConfigurationClose(configuration_);
        configuration_ = nullptr;
      }
      if (registration_ != nullptr && api_ != nullptr) {
        api_->RegistrationClose(registration_);
        registration_ = nullptr;
      }
      if (api_ != nullptr) {
        MsQuicClose(api_);
        api_ = nullptr;
      }
    } catch (...) {
    }
  }

  HostedEmulatorEndpointConfig config_;
  HostedEmulatorTicketAuthorizer authorizer_;
  HostedEmulatorFrameFlow flow_;
  ServerSessionProtocol protocol_;
  VideoMediaPacketizer packetizer_;
  mutable std::mutex mutex_;
  std::condition_variable changed_;
  const QUIC_API_TABLE *api_{};
  HQUIC registration_{};
  HQUIC configuration_{};
  HQUIC listener_{};
  HQUIC connection_{};
  HQUIC session_stream_{};
  std::string private_key_path_;
  std::string certificate_path_;
  std::vector<std::byte> pending_session_bytes_;
  std::uint64_t connection_generation_{};
  std::uint64_t next_connection_generation_{};
  std::uint16_t protocol_maximum_datagram_bytes_{};
  std::uint16_t local_port_{};
  std::size_t active_callbacks_{};
  std::size_t open_peer_streams_{};
  std::size_t pending_datagrams_{};
  bool run_called_{};
  bool authenticated_{};
  bool started_{};
  bool stopped_{};
  bool shutdown_requested_{};
  bool close_after_session_send_{};
  bool terminal_{};
  HostedEmulatorEndpointFailure failure_{HostedEmulatorEndpointFailure::none};
};

HostedEmulatorEndpointServer::HostedEmulatorEndpointServer(
    HostedEmulatorEndpointConfig config)
    : impl_(std::make_unique<Impl>(std::move(config))) {}

HostedEmulatorEndpointServer::~HostedEmulatorEndpointServer() = default;

int HostedEmulatorEndpointServer::run() { return impl_->run(); }

HostedEmulatorEndpointFailure
HostedEmulatorEndpointServer::failure() const noexcept {
  return impl_->failure();
}

const char *hosted_emulator_endpoint_failure_name(
    HostedEmulatorEndpointFailure failure) noexcept {
  switch (failure) {
  case HostedEmulatorEndpointFailure::none:
    return "none";
  case HostedEmulatorEndpointFailure::invalid_configuration:
    return "invalid_configuration";
  case HostedEmulatorEndpointFailure::msquic_open:
    return "msquic_open";
  case HostedEmulatorEndpointFailure::registration_open:
    return "registration_open";
  case HostedEmulatorEndpointFailure::configuration_open:
    return "configuration_open";
  case HostedEmulatorEndpointFailure::credential_load:
    return "credential_load";
  case HostedEmulatorEndpointFailure::listener_open:
    return "listener_open";
  case HostedEmulatorEndpointFailure::listener_start:
    return "listener_start";
  case HostedEmulatorEndpointFailure::listener_address:
    return "listener_address";
  case HostedEmulatorEndpointFailure::connection_refused:
    return "connection_refused";
  case HostedEmulatorEndpointFailure::connection_configuration:
    return "connection_configuration";
  case HostedEmulatorEndpointFailure::unsupported_alpn:
    return "unsupported_alpn";
  case HostedEmulatorEndpointFailure::invalid_peer_stream:
    return "invalid_peer_stream";
  case HostedEmulatorEndpointFailure::protocol:
    return "protocol";
  case HostedEmulatorEndpointFailure::packetization:
    return "packetization";
  case HostedEmulatorEndpointFailure::datagram_send:
    return "datagram_send";
  case HostedEmulatorEndpointFailure::session_send:
    return "session_send";
  case HostedEmulatorEndpointFailure::unexpected_action:
    return "unexpected_action";
  case HostedEmulatorEndpointFailure::unexpected_feedback:
    return "unexpected_feedback";
  case HostedEmulatorEndpointFailure::transport:
    return "transport";
  case HostedEmulatorEndpointFailure::peer_shutdown:
    return "peer_shutdown";
  case HostedEmulatorEndpointFailure::callback_exception:
    return "callback_exception";
  }
  return "unknown";
}

} // namespace beacon::stream::testing
