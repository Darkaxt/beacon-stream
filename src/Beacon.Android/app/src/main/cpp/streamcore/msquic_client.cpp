#include "msquic_client.h"

#include "certificate_pin.h"
#include "deferred_action_queue.h"

#include <algorithm>
#include <cstring>
#include <new>
#include <string_view>
#include <utility>

#if defined(__ANDROID__)
#include <android/log.h>
#endif

namespace beacon::android::streamcore {
namespace {

constexpr std::string_view beacon_alpn{"beacon-stream/1"};
constexpr std::uint32_t transport_heartbeat_interval_ms{1000};

void log_transport_stage(std::string_view stage, std::uint64_t generation,
                         std::uint64_t value) noexcept {
#if defined(__ANDROID__)
  __android_log_print(
      ANDROID_LOG_INFO, "BeaconStreamCore",
      "BEACON_STREAMCORE_TRANSPORT %.*s generation=%llu value=%llu",
      static_cast<int>(stage.size()), stage.data(),
      static_cast<unsigned long long>(generation),
      static_cast<unsigned long long>(value));
#else
  static_cast<void>(stage);
  static_cast<void>(generation);
  static_cast<void>(value);
#endif
}

}  // namespace

QUIC_SETTINGS make_msquic_client_settings() noexcept {
  QUIC_SETTINGS settings{};
  settings.IdleTimeoutMs = 0;
  settings.IsSet.IdleTimeoutMs = TRUE;
  settings.KeepAliveIntervalMs = transport_heartbeat_interval_ms;
  settings.IsSet.KeepAliveIntervalMs = TRUE;
  settings.DatagramReceiveEnabled = TRUE;
  settings.IsSet.DatagramReceiveEnabled = TRUE;
  return settings;
}

void secure_clear_send_bytes(std::vector<std::byte> &bytes) noexcept {
  volatile std::byte *current = bytes.data();
  for (std::size_t index = 0; index < bytes.size(); ++index) {
    current[index] = std::byte{};
  }
}

std::uint64_t expected_stream_id(StreamRole role) noexcept {
  switch (role) {
    case StreamRole::session: return 0;
    case StreamRole::input: return 2;
    case StreamRole::feedback: return 6;
  }
  return 0;
}

bool stream_id_matches(StreamRole role, std::uint64_t id) noexcept {
  return id == expected_stream_id(role);
}

StreamStartValidation validate_stream_start(StreamRole role, QUIC_STATUS status,
                                            std::uint64_t id) noexcept {
  if (QUIC_FAILED(status)) return StreamStartValidation::failed_status;
  return stream_id_matches(role, id) ? StreamStartValidation::accepted
                                     : StreamStartValidation::unexpected_id;
}

ShutdownCleanupAction select_shutdown_cleanup_action(
    bool release_requested, std::optional<Endpoint> pending_endpoint) {
  return {.notify_closed = release_requested,
          .reconnect = release_requested ? std::nullopt
                                         : std::move(pending_endpoint)};
}

MsQuicClient::MsQuicClient(MsQuicClientCallbacks &callbacks) : callbacks_(callbacks) {}

MsQuicClient::~MsQuicClient() { close_api_handles(); }

bool MsQuicClient::connect(const Endpoint &endpoint) {
  std::lock_guard lock(mutex_);
  if (endpoint.host.empty() || endpoint.port == 0 || endpoint.generation == 0 ||
      release_requested_) {
    return false;
  }
  if (connection_ != nullptr) {
    if (shutdown_started_) {
      pending_endpoint_ = endpoint;
      return true;
    }
    return false;
  }
  if (api_ != nullptr) {
    return false;
  }
  api_closed_ = false;
  shutdown_started_ = false;
  certificate_validated_ = false;
  cleanup_scheduled_ = false;
  loss_reported_ = false;
  expected_pin_ = endpoint.spki_pin;
  connection_generation_ = endpoint.generation;
#ifndef NDEBUG
  if (test_connect_hook_) return test_connect_hook_(endpoint);
#endif
  if (QUIC_FAILED(MsQuicOpen2(&api_))) {
    api_ = nullptr;
    return false;
  }
  const QUIC_REGISTRATION_CONFIG registration_config{
      "beacon-android-streamcore", QUIC_EXECUTION_PROFILE_LOW_LATENCY};
  if (QUIC_FAILED(api_->RegistrationOpen(&registration_config, &registration_))) {
    close_api_handles();
    return false;
  }
  QUIC_SETTINGS settings = make_msquic_client_settings();
  const QUIC_BUFFER alpn{
      static_cast<std::uint32_t>(beacon_alpn.size()),
      reinterpret_cast<std::uint8_t *>(const_cast<char *>(beacon_alpn.data()))};
  if (QUIC_FAILED(api_->ConfigurationOpen(
          registration_, &alpn, 1, &settings, sizeof(settings), nullptr,
          &configuration_))) {
    close_api_handles();
    return false;
  }
  QUIC_CREDENTIAL_CONFIG credentials{};
  credentials.Type = QUIC_CREDENTIAL_TYPE_NONE;
  credentials.Flags = static_cast<QUIC_CREDENTIAL_FLAGS>(
      QUIC_CREDENTIAL_FLAG_CLIENT |
      QUIC_CREDENTIAL_FLAG_NO_CERTIFICATE_VALIDATION |
      QUIC_CREDENTIAL_FLAG_INDICATE_CERTIFICATE_RECEIVED |
      QUIC_CREDENTIAL_FLAG_USE_PORTABLE_CERTIFICATES);
  if (QUIC_FAILED(api_->ConfigurationLoadCredential(configuration_, &credentials)) ||
      QUIC_FAILED(api_->ConnectionOpen(registration_, connection_callback, this,
                                       &connection_))) {
    close_api_handles();
    return false;
  }
  const std::string host(endpoint.host);
  if (QUIC_FAILED(api_->ConnectionStart(connection_, configuration_,
                                        QUIC_ADDRESS_FAMILY_UNSPEC,
                                        host.c_str(), endpoint.port))) {
    api_->ConnectionClose(connection_);
    connection_ = nullptr;
    close_api_handles();
    return false;
  }
  return true;
}

bool MsQuicClient::open_stream(StreamRole role) {
  std::lock_guard lock(mutex_);
  if (connection_ == nullptr || shutdown_started_) {
    log_transport_stage("stream_open_rejected", connection_generation_,
                        static_cast<std::uint64_t>(role));
    return false;
  }
  HQUIC *target = nullptr;
  StreamContext *context = nullptr;
  QUIC_STREAM_OPEN_FLAGS flags = QUIC_STREAM_OPEN_FLAG_NONE;
  switch (role) {
    case StreamRole::session:
      target = &session_stream_;
      context = &session_context_;
      break;
    case StreamRole::input:
      target = &input_stream_;
      context = &input_context_;
      flags = QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL;
      break;
    case StreamRole::feedback:
      target = &feedback_stream_;
      context = &feedback_context_;
      flags = QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL;
      break;
  }
  context->generation = connection_generation_;
  const auto open_status = *target == nullptr
                               ? api_->StreamOpen(connection_, flags,
                                                  stream_callback, context,
                                                  target)
                               : QUIC_STATUS_INVALID_STATE;
  if (QUIC_FAILED(open_status)) {
    log_transport_stage("stream_open_failed", connection_generation_,
                        static_cast<std::uint32_t>(open_status));
    return false;
  }
  const auto start_status =
      api_->StreamStart(*target, QUIC_STREAM_START_FLAG_IMMEDIATE);
  if (QUIC_FAILED(start_status)) {
    log_transport_stage("stream_start_failed", connection_generation_,
                        static_cast<std::uint32_t>(start_status));
    api_->StreamClose(*target);
    *target = nullptr;
    return false;
  }
  log_transport_stage("stream_start_queued", connection_generation_,
                      static_cast<std::uint64_t>(role));
  return true;
}

bool MsQuicClient::send(StreamRole role, std::vector<std::byte> bytes) {
  std::lock_guard lock(mutex_);
  HQUIC stream = stream_for(role);
  if (stream == nullptr || shutdown_started_ || bytes.empty()) {
    return false;
  }
  auto *context = new SendContext{};
  context->bytes = std::move(bytes);
  context->buffer.Length = static_cast<std::uint32_t>(context->bytes.size());
  context->buffer.Buffer = reinterpret_cast<std::uint8_t *>(context->bytes.data());
  if (QUIC_FAILED(api_->StreamSend(stream, &context->buffer, 1,
                                   QUIC_SEND_FLAG_NONE, context))) {
    context->clear();
    delete context;
    return false;
  }
  return true;
}

bool MsQuicClient::send_final(StreamRole role, std::vector<std::byte> bytes) {
  std::lock_guard lock(mutex_);
  HQUIC stream = stream_for(role);
  if (role != StreamRole::session || stream == nullptr || shutdown_started_ ||
      bytes.empty()) {
    return false;
  }
  auto *context = new SendContext{};
  context->bytes = std::move(bytes);
  context->buffer.Length = static_cast<std::uint32_t>(context->bytes.size());
  context->buffer.Buffer =
      reinterpret_cast<std::uint8_t *>(context->bytes.data());
  shutdown_started_ = true;
  if (QUIC_FAILED(api_->StreamSend(stream, &context->buffer, 1,
                                   QUIC_SEND_FLAG_FIN, context))) {
    shutdown_started_ = false;
    context->clear();
    delete context;
    return false;
  }
  return true;
}

void MsQuicClient::shutdown() {
  std::lock_guard lock(mutex_);
  if (!shutdown_started_) {
    shutdown_started_ = true;
    if (connection_ != nullptr) {
      api_->ConnectionShutdown(connection_, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
    }
  }
}

void MsQuicClient::release() {
  bool closed_immediately = false;
  std::uint64_t closed_generation = 0;
  {
    std::lock_guard lock(mutex_);
    release_requested_ = true;
    pending_endpoint_.reset();
    if (!shutdown_started_) {
      shutdown_started_ = true;
      if (connection_ != nullptr) {
        api_->ConnectionShutdown(connection_, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
      }
    }
    if (connection_ == nullptr) {
      close_api_handles();
      closed_immediately = true;
      closed_generation = connection_generation_;
    }
  }
  if (closed_immediately) {
    callbacks_.transport_closed(closed_generation);
  }
}

void MsQuicClient::report_local_failure(std::uint64_t generation) noexcept {
  bool report_loss = false;
  try {
    {
      std::lock_guard lock(mutex_);
      if (generation != connection_generation_ || connection_ == nullptr ||
          release_requested_) {
        return;
      }
      if (!shutdown_started_) {
        shutdown_started_ = true;
        if (api_ != nullptr) {
          api_->ConnectionShutdown(connection_, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                                   1);
        }
      }
      if (!loss_reported_) {
        loss_reported_ = true;
        report_loss = true;
      }
    }
    if (report_loss) callbacks_.connection_lost(generation);
  } catch (...) {
  }
}

QUIC_STATUS QUIC_API MsQuicClient::connection_callback(
    HQUIC connection, void *context, QUIC_CONNECTION_EVENT *event) {
  if (context == nullptr || event == nullptr) return QUIC_STATUS_INVALID_PARAMETER;
  try {
    auto &self = *static_cast<MsQuicClient *>(context);
    auto callback_scope = self.callback_barrier_->enter();
    switch (event->Type) {
      case QUIC_CONNECTION_EVENT_PEER_CERTIFICATE_RECEIVED: {
        const auto *portable = reinterpret_cast<const QUIC_BUFFER *>(
            event->PEER_CERTIFICATE_RECEIVED.Certificate);
        std::vector<std::byte> der;
        if (portable != nullptr && portable->Buffer != nullptr &&
            portable->Length > 0) {
          const auto *begin =
              reinterpret_cast<const std::byte *>(portable->Buffer);
          der.assign(begin, begin + portable->Length);
        }
        const bool valid = validate_der_spki_pin(der, self.expected_pin_);
        {
          std::lock_guard lock(self.mutex_);
          self.certificate_validated_ = valid;
        }
        log_transport_stage(valid ? "certificate_accepted"
                                  : "certificate_rejected",
                            self.connection_generation_, der.size());
        return valid ? QUIC_STATUS_SUCCESS : QUIC_STATUS_BAD_CERTIFICATE;
      }
      case QUIC_CONNECTION_EVENT_CONNECTED: {
        bool valid;
        std::uint64_t generation;
        {
          std::lock_guard lock(self.mutex_);
          if (connection != nullptr && connection != self.connection_) break;
          valid = self.certificate_validated_;
          generation = self.connection_generation_;
        }
        log_transport_stage("connected", generation, valid ? 1 : 0);
        if (!valid) return QUIC_STATUS_BAD_CERTIFICATE;
        self.callbacks_.transport_connected(generation);
        break;
      }
      case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
        if (event->DATAGRAM_RECEIVED.Buffer == nullptr ||
            (event->DATAGRAM_RECEIVED.Buffer->Length != 0 &&
             event->DATAGRAM_RECEIVED.Buffer->Buffer == nullptr)) {
          return QUIC_STATUS_INVALID_PARAMETER;
        }
        const auto &buffer = *event->DATAGRAM_RECEIVED.Buffer;
        std::vector<std::byte> bytes;
        if (buffer.Length != 0) {
          const auto *begin =
              reinterpret_cast<const std::byte *>(buffer.Buffer);
          bytes.assign(begin, begin + buffer.Length);
        }
        std::uint64_t generation;
        {
          std::lock_guard lock(self.mutex_);
          if (connection != nullptr && connection != self.connection_) break;
          generation = self.connection_generation_;
        }
        self.callbacks_.media_datagram(generation, std::move(bytes));
        break;
      }
      case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT:
      case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_PEER: {
        bool report_loss = false;
        std::uint64_t generation = 0;
        {
          std::lock_guard lock(self.mutex_);
          if (connection != nullptr && connection != self.connection_) break;
          self.shutdown_started_ = true;
          generation = self.connection_generation_;
          if (!self.loss_reported_) {
            self.loss_reported_ = true;
            report_loss = true;
          }
        }
        const auto status =
            event->Type == QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT
                ? static_cast<std::uint64_t>(static_cast<std::uint32_t>(
                      event->SHUTDOWN_INITIATED_BY_TRANSPORT.Status))
                : event->SHUTDOWN_INITIATED_BY_PEER.ErrorCode;
        log_transport_stage("shutdown", generation, status);
        if (report_loss) self.callbacks_.connection_lost(generation);
        break;
      }
      case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
        bool schedule_cleanup = false;
        {
          std::lock_guard lock(self.mutex_);
          if (connection != nullptr && connection != self.connection_) break;
          if (!self.cleanup_scheduled_) {
            self.cleanup_scheduled_ = true;
            schedule_cleanup = true;
          }
        }
        if (schedule_cleanup) {
          DeferredActionQueue::instance().enqueue(
              self.callback_barrier_, [&self] { self.complete_shutdown(); });
        }
        break;
      }
      default:
        break;
    }
    return QUIC_STATUS_SUCCESS;
  } catch (const std::bad_alloc &) {
    return QUIC_STATUS_OUT_OF_MEMORY;
  } catch (...) {
    return QUIC_STATUS_INTERNAL_ERROR;
  }
}

QUIC_STATUS QUIC_API MsQuicClient::stream_callback(
    HQUIC, void *context, QUIC_STREAM_EVENT *event) {
  if (context == nullptr || event == nullptr) return QUIC_STATUS_INVALID_PARAMETER;
  try {
    auto &stream_context = *static_cast<StreamContext *>(context);
    if (stream_context.owner == nullptr) return QUIC_STATUS_INVALID_PARAMETER;
    auto &self = *stream_context.owner;
    auto callback_scope = self.callback_barrier_->enter();
    switch (event->Type) {
      case QUIC_STREAM_EVENT_START_COMPLETE: {
        const auto validation = validate_stream_start(
            stream_context.role, event->START_COMPLETE.Status,
            event->START_COMPLETE.ID);
        if (validation == StreamStartValidation::accepted) {
          log_transport_stage("stream_started", stream_context.generation,
                              event->START_COMPLETE.ID);
          break;
        }
        log_transport_stage(
            validation == StreamStartValidation::failed_status
                ? "stream_start_complete_failed"
                : "stream_id_rejected",
            stream_context.generation,
            validation == StreamStartValidation::failed_status
                ? static_cast<std::uint32_t>(event->START_COMPLETE.Status)
                : static_cast<std::uint64_t>(event->START_COMPLETE.ID));
        const auto generation = stream_context.generation;
        DeferredActionQueue::instance().enqueue(
            self.callback_barrier_,
            [&self, generation] { self.fail_stream_start(generation); });
        break;
      }
      case QUIC_STREAM_EVENT_RECEIVE: {
        if (event->RECEIVE.BufferCount != 0 &&
            event->RECEIVE.Buffers == nullptr) {
          return QUIC_STATUS_INVALID_PARAMETER;
        }
        std::vector<std::byte> bytes;
        bytes.reserve(
            static_cast<std::size_t>(event->RECEIVE.TotalBufferLength));
        for (std::uint32_t index = 0; index < event->RECEIVE.BufferCount;
             ++index) {
          const auto &buffer = event->RECEIVE.Buffers[index];
          if (buffer.Length != 0 && buffer.Buffer == nullptr) {
            return QUIC_STATUS_INVALID_PARAMETER;
          }
          if (buffer.Length != 0) {
            const auto *begin =
                reinterpret_cast<const std::byte *>(buffer.Buffer);
            bytes.insert(bytes.end(), begin, begin + buffer.Length);
          }
        }
        if (stream_context.role == StreamRole::session) {
          self.callbacks_.session_bytes(stream_context.generation,
                                        std::move(bytes));
        }
        break;
      }
      case QUIC_STREAM_EVENT_SEND_COMPLETE: {
        auto *send = static_cast<SendContext *>(
            event->SEND_COMPLETE.ClientContext);
        if (send != nullptr) send->clear();
        delete send;
        break;
      }
      default:
        break;
    }
    return QUIC_STATUS_SUCCESS;
  } catch (const std::bad_alloc &) {
    return QUIC_STATUS_OUT_OF_MEMORY;
  } catch (...) {
    return QUIC_STATUS_INTERNAL_ERROR;
  }
}

HQUIC MsQuicClient::stream_for(StreamRole role) const noexcept {
  switch (role) {
    case StreamRole::session: return session_stream_;
    case StreamRole::input: return input_stream_;
    case StreamRole::feedback: return feedback_stream_;
  }
  return nullptr;
}

void MsQuicClient::fail_stream_start(std::uint64_t generation) noexcept {
  report_local_failure(generation);
}

void MsQuicClient::close_api_handles() {
  if (api_closed_) {
    return;
  }
  api_closed_ = true;
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
}

void MsQuicClient::complete_shutdown() {
  ShutdownCleanupAction action;
  std::uint64_t closed_generation = 0;
  {
    std::lock_guard lock(mutex_);
#ifndef NDEBUG
    const bool skip_handle_cleanup = test_skip_msquic_handle_cleanup_;
#else
    constexpr bool skip_handle_cleanup = false;
#endif
    if (!skip_handle_cleanup && api_ != nullptr) {
      if (session_stream_ != nullptr) api_->StreamClose(session_stream_);
      if (input_stream_ != nullptr) api_->StreamClose(input_stream_);
      if (feedback_stream_ != nullptr) api_->StreamClose(feedback_stream_);
    }
    session_stream_ = nullptr;
    input_stream_ = nullptr;
    feedback_stream_ = nullptr;
    if (!skip_handle_cleanup && connection_ != nullptr && api_ != nullptr) {
      api_->ConnectionClose(connection_);
    }
    connection_ = nullptr;
    closed_generation = connection_generation_;
    action = select_shutdown_cleanup_action(
        release_requested_, std::move(pending_endpoint_));
    pending_endpoint_.reset();
    close_api_handles();
  }
  if (action.reconnect.has_value()) {
    if (!connect(*action.reconnect)) {
      callbacks_.connection_lost(action.reconnect->generation);
    }
  } else if (action.notify_closed) {
    callbacks_.transport_closed(closed_generation);
  }
}

}  // namespace beacon::android::streamcore
