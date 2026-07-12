#include "beacon/worker/named_pipe_channel.h"
#include "beacon/worker/quic_listener.h"
#include "beacon/worker/synthetic_media_source.h"

#include "beacon/stream/media_datagram.h"

#include "stream_control.pb.h"
#include "worker_ipc.pb.h"

#include <Windows.h>
#include <msquic.h>
#include <wincrypt.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <limits>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

constexpr std::string_view kAlpn{"beacon-stream/1"};
constexpr std::string_view kRawTicket{"loopback-ticket"};
namespace stream_v1 = beacon::stream::v1;
namespace worker_v1 = beacon::worker::v1;

class UniqueHandle {
public:
  UniqueHandle() = default;
  explicit UniqueHandle(HANDLE value) noexcept : value_(value) {}
  ~UniqueHandle() { reset(); }

  UniqueHandle(const UniqueHandle &) = delete;
  UniqueHandle &operator=(const UniqueHandle &) = delete;

  UniqueHandle(UniqueHandle &&other) noexcept
      : value_(std::exchange(other.value_, nullptr)) {}
  UniqueHandle &operator=(UniqueHandle &&other) noexcept {
    if (this != &other) {
      reset();
      value_ = std::exchange(other.value_, nullptr);
    }
    return *this;
  }

  [[nodiscard]] HANDLE get() const noexcept { return value_; }
  [[nodiscard]] HANDLE release() noexcept {
    return std::exchange(value_, nullptr);
  }
  void reset(HANDLE value = nullptr) noexcept {
    if (value_ != nullptr && value_ != INVALID_HANDLE_VALUE) {
      CloseHandle(value_);
    }
    value_ = value;
  }

private:
  HANDLE value_{};
};

class WorkerProcess {
public:
  WorkerProcess() = default;
  ~WorkerProcess() {
    if (process_.get() != nullptr && !reaped_) {
      DWORD exit_code = 0;
      if (GetExitCodeProcess(process_.get(), &exit_code) != FALSE &&
          exit_code == STILL_ACTIVE) {
        TerminateProcess(process_.get(), 127);
      }
      WaitForSingleObject(process_.get(), INFINITE);
    }
  }

  WorkerProcess(const WorkerProcess &) = delete;
  WorkerProcess &operator=(const WorkerProcess &) = delete;

  [[nodiscard]] bool start(const std::wstring &worker_path,
                           const std::wstring &pipe_name,
                           const std::wstring &identity_path) {
    std::wstring command_line = L"\"" + worker_path + L"\" --pipe \"" +
                                pipe_name + L"\" --identity \"" +
                                identity_path + L"\"";
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (CreateProcessW(worker_path.c_str(), command_line.data(), nullptr,
                       nullptr, FALSE, CREATE_NO_WINDOW, nullptr, nullptr,
                       &startup, &process) == FALSE) {
      return false;
    }
    process_.reset(process.hProcess);
    thread_.reset(process.hThread);
    return true;
  }

  [[nodiscard]] std::optional<DWORD> wait() {
    if (process_.get() == nullptr ||
        WaitForSingleObject(process_.get(), INFINITE) != WAIT_OBJECT_0) {
      return std::nullopt;
    }
    DWORD exit_code = 0;
    if (GetExitCodeProcess(process_.get(), &exit_code) == FALSE) {
      return std::nullopt;
    }
    reaped_ = true;
    return exit_code;
  }

  [[nodiscard]] HANDLE handle() const noexcept { return process_.get(); }

  [[nodiscard]] std::optional<DWORD> exit_code() const noexcept {
    DWORD value = 0;
    if (process_.get() == nullptr ||
        GetExitCodeProcess(process_.get(), &value) == FALSE ||
        value == STILL_ACTIVE) {
      return std::nullopt;
    }
    return value;
  }

private:
  UniqueHandle process_;
  UniqueHandle thread_;
  bool reaped_{};
};

bool decode_fingerprint(std::string_view text,
                        beacon::worker::TicketHash &output) {
  if (text.size() != output.size() * 2U)
    return false;
  const auto hex = [](char value) -> int {
    if (value >= '0' && value <= '9')
      return value - '0';
    if (value >= 'A' && value <= 'F')
      return value - 'A' + 10;
    if (value >= 'a' && value <= 'f')
      return value - 'a' + 10;
    return -1;
  };
  for (std::size_t index = 0; index < output.size(); ++index) {
    const int high = hex(text[index * 2]);
    const int low = hex(text[index * 2 + 1]);
    if (high < 0 || low < 0)
      return false;
    output[index] = static_cast<std::byte>((high << 4) | low);
  }
  return true;
}

bool certificate_matches(QUIC_CERTIFICATE *certificate,
                         const beacon::worker::TicketHash &expected) {
  const auto *context = reinterpret_cast<PCCERT_CONTEXT>(certificate);
  BYTE *encoded = nullptr;
  DWORD encoded_size = 0;
  if (context == nullptr ||
      !CryptEncodeObjectEx(X509_ASN_ENCODING, X509_PUBLIC_KEY_INFO,
                           &context->pCertInfo->SubjectPublicKeyInfo,
                           CRYPT_ENCODE_ALLOC_FLAG, nullptr, &encoded,
                           &encoded_size)) {
    return false;
  }
  const auto actual = beacon::worker::hash_stream_ticket(
      {reinterpret_cast<const std::byte *>(encoded), encoded_size});
  LocalFree(encoded);
  return std::ranges::equal(actual, expected);
}

template <typename Message>
std::vector<std::byte> frame(const Message &message) {
  const auto size = message.ByteSizeLong();
  std::vector<std::byte> result(4 + size);
  result[0] = static_cast<std::byte>((size >> 24U) & 0xffU);
  result[1] = static_cast<std::byte>((size >> 16U) & 0xffU);
  result[2] = static_cast<std::byte>((size >> 8U) & 0xffU);
  result[3] = static_cast<std::byte>(size & 0xffU);
  if (!message.SerializeToArray(result.data() + 4, static_cast<int>(size))) {
    return {};
  }
  return result;
}

struct ClientState;

struct StreamContext {
  ClientState *owner{};
  bool session{};
};

struct SendContext {
  explicit SendContext(std::vector<std::byte> value) : bytes(std::move(value)) {
    buffer.Length = static_cast<std::uint32_t>(bytes.size());
    buffer.Buffer = reinterpret_cast<std::uint8_t *>(bytes.data());
  }
  std::vector<std::byte> bytes;
  QUIC_BUFFER buffer{};
};

struct ClientState {
  const QUIC_API_TABLE *api{};
  HQUIC registration{};
  HQUIC configuration{};
  HQUIC connection{};
  HQUIC session_stream{};
  HQUIC input_stream{};
  HQUIC feedback_stream{};
  StreamContext session_context{this, true};
  StreamContext input_context{this, false};
  StreamContext feedback_context{this, false};
  std::mutex mutex;
  std::condition_variable changed;
  std::vector<std::byte> session_bytes;
  bool authenticated{};
  bool datagram_received{};
  bool connection_closed{};
  bool failed{};
  bool certificate_seen{};
  bool send_data_after_auth{true};
  std::uint64_t expected_marker_sequence{1};
  stream_v1::SessionErrorCode authentication_error{
      stream_v1::SESSION_ERROR_CODE_UNSPECIFIED};
  std::string raw_ticket{kRawTicket};
  std::uint32_t protocol_version{1};
  beacon::worker::TicketHash expected_fingerprint{};

  bool send(HQUIC stream, std::vector<std::byte> bytes, QUIC_SEND_FLAGS flags) {
    auto *context = new SendContext(std::move(bytes));
    const auto status =
        api->StreamSend(stream, &context->buffer, 1, flags, context);
    if (QUIC_FAILED(status)) {
      delete context;
      return false;
    }
    return true;
  }

  bool send_post_auth_messages() {
    stream_v1::SessionStreamEnvelope control;
    control.set_protocol_version(1);
    control.set_session_id("session-a");
    control.set_sequence(2);
    auto *video = control.mutable_start_session()->mutable_selected_video();
    video->set_codec(stream_v1::VIDEO_CODEC_H264);
    video->set_width(2560);
    video->set_height(1600);
    video->set_frames_per_second_numerator(120);
    video->set_frames_per_second_denominator(1);
    video->set_dynamic_range(stream_v1::DYNAMIC_RANGE_SDR);

    stream_v1::InputStreamEnvelope input;
    input.set_protocol_version(1);
    input.set_session_id("session-a");
    input.set_sequence(1);
    auto *key = input.mutable_input_batch()->add_events()->mutable_keyboard();
    key->set_scan_code(30);
    key->set_pressed(true);

    stream_v1::FeedbackStreamEnvelope feedback;
    feedback.set_protocol_version(1);
    feedback.set_session_id("session-a");
    feedback.set_sequence(1);
    feedback.mutable_queue_depth()->set_queued_access_units(2);

    return send(session_stream, frame(control), QUIC_SEND_FLAG_NONE) &&
           send(input_stream, frame(input), QUIC_SEND_FLAG_FIN) &&
           send(feedback_stream, frame(feedback), QUIC_SEND_FLAG_FIN);
  }
};

QUIC_STATUS QUIC_API stream_callback(HQUIC stream, void *context,
                                     QUIC_STREAM_EVENT *event) {
  auto &stream_context = *static_cast<StreamContext *>(context);
  auto &state = *stream_context.owner;
  switch (event->Type) {
  case QUIC_STREAM_EVENT_RECEIVE:
    if (stream_context.session) {
      bool accepted = false;
      {
        std::lock_guard lock{state.mutex};
        for (std::uint32_t index = 0; index < event->RECEIVE.BufferCount;
             ++index) {
          const auto &buffer = event->RECEIVE.Buffers[index];
          const auto *begin =
              reinterpret_cast<const std::byte *>(buffer.Buffer);
          state.session_bytes.insert(state.session_bytes.end(), begin,
                                     begin + buffer.Length);
        }
        if (state.session_bytes.size() >= 4) {
          const auto size =
              (std::to_integer<std::uint32_t>(state.session_bytes[0]) << 24U) |
              (std::to_integer<std::uint32_t>(state.session_bytes[1]) << 16U) |
              (std::to_integer<std::uint32_t>(state.session_bytes[2]) << 8U) |
              std::to_integer<std::uint32_t>(state.session_bytes[3]);
          if (size > 0 && state.session_bytes.size() >= 4U + size) {
            stream_v1::SessionStreamEnvelope reply;
            const bool parsed = reply.ParseFromArray(
                state.session_bytes.data() + 4, static_cast<int>(size));
            state.authenticated =
                parsed && reply.has_session_authenticated() &&
                reply.session_authenticated().accepted() &&
                reply.session_authenticated().maximum_datagram_bytes() > 0;
            if (parsed && reply.has_session_authenticated()) {
              state.authentication_error =
                  reply.session_authenticated().error_code();
            }
            state.failed = !state.authenticated;
            accepted = state.authenticated;
          }
        }
      }
      if (accepted && state.send_data_after_auth &&
          !state.send_post_auth_messages()) {
        std::lock_guard lock{state.mutex};
        state.failed = true;
      }
      state.changed.notify_all();
    }
    break;
  case QUIC_STREAM_EVENT_SEND_COMPLETE:
    delete static_cast<SendContext *>(event->SEND_COMPLETE.ClientContext);
    break;
  case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE:
    state.api->StreamClose(stream);
    break;
  default:
    break;
  }
  return QUIC_STATUS_SUCCESS;
}

bool open_stream(ClientState &state, QUIC_STREAM_OPEN_FLAGS flags,
                 StreamContext &context, HQUIC &stream) {
  return QUIC_SUCCEEDED(state.api->StreamOpen(
             state.connection, flags, stream_callback, &context, &stream)) &&
         QUIC_SUCCEEDED(
             state.api->StreamStart(stream, QUIC_STREAM_START_FLAG_NONE));
}

bool start_protocol(ClientState &state) {
  if (!open_stream(state, QUIC_STREAM_OPEN_FLAG_NONE, state.session_context,
                   state.session_stream) ||
      !open_stream(state, QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL,
                   state.input_context, state.input_stream) ||
      !open_stream(state, QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL,
                   state.feedback_context, state.feedback_stream)) {
    return false;
  }
  stream_v1::SessionStreamEnvelope auth;
  auth.set_protocol_version(state.protocol_version);
  auth.set_session_id("session-a");
  auth.set_sequence(1);
  auto *request = auth.mutable_authenticate_session();
  request->set_stream_ticket(state.raw_ticket);
  request->set_client_id("z-fold-7");
  request->set_plan_revision(8);
  return state.send(state.session_stream, frame(auth), QUIC_SEND_FLAG_NONE);
}

QUIC_STATUS QUIC_API connection_callback(HQUIC connection, void *context,
                                         QUIC_CONNECTION_EVENT *event) {
  auto &state = *static_cast<ClientState *>(context);
  switch (event->Type) {
  case QUIC_CONNECTION_EVENT_CONNECTED:
    if (!start_protocol(state)) {
      std::lock_guard lock{state.mutex};
      state.failed = true;
      state.changed.notify_all();
    }
    break;
  case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED: {
    const auto &buffer = *event->DATAGRAM_RECEIVED.Buffer;
    const auto parsed = beacon::stream::parse_media_datagram(
        {reinterpret_cast<const std::byte *>(buffer.Buffer), buffer.Length});
    constexpr auto expected_payload =
        beacon::worker::synthetic_access_unit_marker_bytes;
    std::lock_guard lock{state.mutex};
    state.datagram_received =
        parsed.error == beacon::stream::MediaDatagramError::none &&
        parsed.header.sequence == state.expected_marker_sequence &&
        parsed.header.presentation_time_us == 1'000'000 &&
        parsed.header.chunk_count == 1 &&
        parsed.header.flags ==
            (beacon::stream::MediaDatagramFlags::idr |
             beacon::stream::MediaDatagramFlags::end_of_access_unit) &&
        std::ranges::equal(parsed.payload, expected_payload);
    state.failed = !state.datagram_received;
    state.changed.notify_all();
    break;
  }
  case QUIC_CONNECTION_EVENT_PEER_CERTIFICATE_RECEIVED: {
    const bool valid =
        certificate_matches(event->PEER_CERTIFICATE_RECEIVED.Certificate,
                            state.expected_fingerprint);
    {
      std::lock_guard lock{state.mutex};
      state.certificate_seen = true;
      state.failed = !valid;
    }
    state.changed.notify_all();
    return valid ? QUIC_STATUS_SUCCESS : QUIC_STATUS_BAD_CERTIFICATE;
  }
  case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT: {
    std::lock_guard lock{state.mutex};
    state.failed = true;
  }
    state.changed.notify_all();
    break;
  case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE:
    {
      std::lock_guard lock{state.mutex};
      if (state.connection == connection) {
        state.connection = nullptr;
      }
      state.connection_closed = true;
      state.api->ConnectionClose(connection);
    }
    state.changed.notify_all();
    break;
  default:
    break;
  }
  return QUIC_STATUS_SUCCESS;
}

bool start_client(ClientState &state, std::uint16_t port,
                  std::string_view alpn_value = kAlpn) {
  if (QUIC_FAILED(MsQuicOpen2(&state.api)))
    return false;
  const QUIC_REGISTRATION_CONFIG registration_config{
      "beacon-loopback-client", QUIC_EXECUTION_PROFILE_LOW_LATENCY};
  if (QUIC_FAILED(state.api->RegistrationOpen(&registration_config,
                                              &state.registration)))
    return false;
  QUIC_SETTINGS settings{};
  settings.IdleTimeoutMs = 0;
  settings.IsSet.IdleTimeoutMs = true;
  settings.DatagramReceiveEnabled = true;
  settings.IsSet.DatagramReceiveEnabled = true;
  const QUIC_BUFFER alpn{
      static_cast<std::uint32_t>(alpn_value.size()),
      reinterpret_cast<std::uint8_t *>(const_cast<char *>(alpn_value.data()))};
  if (QUIC_FAILED(state.api->ConfigurationOpen(state.registration, &alpn, 1,
                                               &settings, sizeof(settings),
                                               nullptr, &state.configuration)))
    return false;
  QUIC_CREDENTIAL_CONFIG credential{};
  credential.Type = QUIC_CREDENTIAL_TYPE_NONE;
  credential.Flags = QUIC_CREDENTIAL_FLAG_CLIENT |
                     QUIC_CREDENTIAL_FLAG_NO_CERTIFICATE_VALIDATION |
                     QUIC_CREDENTIAL_FLAG_INDICATE_CERTIFICATE_RECEIVED;
  if (QUIC_FAILED(state.api->ConfigurationLoadCredential(state.configuration,
                                                         &credential)))
    return false;
  if (QUIC_FAILED(state.api->ConnectionOpen(
          state.registration, connection_callback, &state, &state.connection)))
    return false;
  return QUIC_SUCCEEDED(state.api->ConnectionStart(
      state.connection, state.configuration, QUIC_ADDRESS_FAMILY_UNSPEC,
      "127.0.0.1", port));
}

void stop_client(ClientState &state, std::uint64_t error_code = 0) {
  {
    std::unique_lock lock{state.mutex};
    if (state.connection != nullptr) {
      state.api->ConnectionShutdown(
          state.connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, error_code);
      state.changed.wait(lock,
                         [&state] { return state.connection_closed; });
    }
  }
  if (state.configuration != nullptr)
    state.api->ConfigurationClose(state.configuration);
  if (state.registration != nullptr)
    state.api->RegistrationClose(state.registration);
  if (state.api != nullptr)
    MsQuicClose(state.api);
}

worker_v1::WorkerIpcEnvelope worker_command(std::uint64_t request_id,
                                            std::string_view session_id) {
  worker_v1::WorkerIpcEnvelope command;
  command.set_protocol_version(1);
  command.set_request_id(request_id);
  command.set_session_id(session_id);
  return command;
}

bool exchange_worker_command(
    beacon::worker::NamedPipeChannel &channel,
    const worker_v1::WorkerIpcEnvelope &command,
    std::vector<worker_v1::WorkerIpcEnvelope> &responses,
    std::vector<worker_v1::WorkerIpcEnvelope> &events) {
  if (!channel.write(command)) {
    return false;
  }
  for (;;) {
    worker_v1::WorkerIpcEnvelope envelope;
    if (channel.read(envelope) != beacon::worker::FrameDecodeStatus::success) {
      return false;
    }
    if (envelope.request_id() == 0) {
      events.push_back(std::move(envelope));
      continue;
    }
    if (envelope.request_id() != command.request_id() ||
        envelope.session_id() != command.session_id()) {
      return false;
    }
    const bool completion = envelope.body_case() ==
                            worker_v1::WorkerIpcEnvelope::kWorkerCompletion;
    const bool succeeded = completion && envelope.worker_completion().succeeded();
    responses.push_back(std::move(envelope));
    if (completion) {
      return succeeded;
    }
  }
}

bool collect_stream_events(
    beacon::worker::NamedPipeChannel &channel,
    std::vector<worker_v1::WorkerIpcEnvelope> &events) {
  bool authenticated = false;
  bool input = false;
  bool feedback = false;
  bool media = false;
  bool connection_observed = false;
  bool connection_configured = false;
  bool transport_connected = false;
  while (!connection_observed || !connection_configured ||
         !transport_connected || !authenticated || !input || !feedback ||
         !media) {
    worker_v1::WorkerIpcEnvelope event;
    if (channel.read(event) != beacon::worker::FrameDecodeStatus::success ||
        event.protocol_version() != 1 || event.request_id() != 0) {
      return false;
    }
    if (event.body_case() == worker_v1::WorkerIpcEnvelope::kWorkerDiagnostic) {
      if (!event.session_id().empty() ||
          event.worker_diagnostic().severity() !=
              worker_v1::DIAGNOSTIC_SEVERITY_INFORMATION ||
          event.worker_diagnostic().boundary() !=
              worker_v1::DIAGNOSTIC_BOUNDARY_TRANSPORT ||
          event.worker_diagnostic().platform_error_code() != 0 ||
          event.worker_diagnostic().numeric_value() != 1) {
        return false;
      }
      switch (event.worker_diagnostic().code()) {
      case worker_v1::DIAGNOSTIC_CODE_CONNECTION_OBSERVED:
        connection_observed = true;
        break;
      case worker_v1::DIAGNOSTIC_CODE_CONNECTION_CONFIGURED:
        connection_configured = true;
        break;
      case worker_v1::DIAGNOSTIC_CODE_TRANSPORT_CONNECTED:
        transport_connected = true;
        break;
      default:
        return false;
      }
      events.push_back(std::move(event));
      continue;
    }
    if (event.session_id() != "session-a") {
      return false;
    }
    switch (event.body_case()) {
    case worker_v1::WorkerIpcEnvelope::kTransportAuthenticated:
      authenticated = event.transport_authenticated().session_generation() == 1 &&
                      event.transport_authenticated().maximum_datagram_bytes() > 0;
      break;
    case worker_v1::WorkerIpcEnvelope::kInputReceived:
      input = event.input_received().session_generation() == 1 &&
              event.input_received().input().sequence() == 1 &&
              event.input_received().input().input_batch().events_size() == 1 &&
              event.input_received()
                      .input()
                      .input_batch()
                      .events(0)
                      .keyboard()
                      .scan_code() == 30 &&
              event.input_received()
                  .input()
                  .input_batch()
                  .events(0)
                  .keyboard()
                  .pressed();
      break;
    case worker_v1::WorkerIpcEnvelope::kFeedbackReceived:
      feedback = event.feedback_received().session_generation() == 1 &&
                 event.feedback_received().feedback().sequence() == 1 &&
                 event.feedback_received().feedback().has_queue_depth() &&
                 event.feedback_received()
                         .feedback()
                         .queue_depth()
                         .queued_access_units() == 2;
      break;
    case worker_v1::WorkerIpcEnvelope::kMediaEvidence:
      media = event.media_evidence().session_generation() == 1 &&
              event.media_evidence().sequence() == 1 &&
              event.media_evidence().presentation_time_us() == 1'000'000 &&
              event.media_evidence().datagram_bytes() > 0;
      break;
    default:
      return false;
    }
    events.push_back(std::move(event));
  }
  return true;
}

bool collect_disconnect_event(
    beacon::worker::NamedPipeChannel &channel,
    std::vector<worker_v1::WorkerIpcEnvelope> &events) {
  for (;;) {
    worker_v1::WorkerIpcEnvelope event;
    if (channel.read(event) != beacon::worker::FrameDecodeStatus::success ||
        event.protocol_version() != 1 || event.request_id() != 0 ||
        event.session_id() != "session-a") {
      return false;
    }
    const bool disconnected =
        event.body_case() ==
            worker_v1::WorkerIpcEnvelope::kTransportDisconnected &&
        event.transport_disconnected().session_generation() == 1;
    events.push_back(std::move(event));
    if (disconnected) {
      return true;
    }
  }
}

int run_worker_process_probe(const std::wstring &worker_path,
                             const std::wstring &identity_path,
                             const beacon::worker::TicketHash &fingerprint) {
  const auto pipe_name = L"\\\\.\\pipe\\beacon-worker-process-probe-" +
                         std::to_wstring(GetCurrentProcessId()) + L"-" +
                         std::to_wstring(GetTickCount64());
  UniqueHandle pipe{CreateNamedPipeW(
      pipe_name.c_str(), PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
      PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1,
      beacon::worker::maximum_worker_message_bytes + 4,
      beacon::worker::maximum_worker_message_bytes + 4, 0, nullptr)};
  if (pipe.get() == INVALID_HANDLE_VALUE) {
    return 80;
  }
  UniqueHandle connected_event{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
  if (connected_event.get() == nullptr) {
    return 81;
  }
  OVERLAPPED connected{};
  connected.hEvent = connected_event.get();
  const BOOL connected_immediately = ConnectNamedPipe(pipe.get(), &connected);
  const DWORD connect_error =
      connected_immediately == FALSE ? GetLastError() : ERROR_SUCCESS;
  if (connected_immediately == FALSE && connect_error != ERROR_IO_PENDING) {
    return 82;
  }

  WorkerProcess process;
  if (!process.start(worker_path, pipe_name, identity_path)) {
    CancelIoEx(pipe.get(), &connected);
    return 83;
  }
  if (connected_immediately == FALSE) {
    const std::array wait_handles{connected_event.get(), process.handle()};
    const auto wait = WaitForMultipleObjects(
        static_cast<DWORD>(wait_handles.size()), wait_handles.data(), FALSE,
        INFINITE);
    if (wait == WAIT_OBJECT_0 + 1) {
      CancelIoEx(pipe.get(), &connected);
      DWORD transferred = 0;
      static_cast<void>(
          GetOverlappedResult(pipe.get(), &connected, &transferred, TRUE));
      const auto worker_exit = process.exit_code();
      std::printf("BEACON_WORKER_STARTUP_EXIT %lu\n",
                  static_cast<unsigned long>(worker_exit.value_or(
                      std::numeric_limits<DWORD>::max())));
      return 97;
    }
    if (wait != WAIT_OBJECT_0) {
      return 84;
    }
    DWORD transferred = 0;
    if (GetOverlappedResult(pipe.get(), &connected, &transferred, FALSE) ==
        FALSE) {
      return 85;
    }
  }

  beacon::worker::NamedPipeChannel channel(pipe.release());
  worker_v1::WorkerIpcEnvelope hello;
  worker_v1::WorkerIpcEnvelope ready;
  if (channel.read(hello) != beacon::worker::FrameDecodeStatus::success ||
      channel.read(ready) != beacon::worker::FrameDecodeStatus::success ||
      hello.body_case() != worker_v1::WorkerIpcEnvelope::kWorkerHello ||
      ready.body_case() != worker_v1::WorkerIpcEnvelope::kWorkerReady ||
      hello.worker_hello().worker_instance_id().empty() ||
      hello.worker_hello().worker_instance_id() !=
          ready.worker_ready().worker_instance_id()) {
    return 86;
  }

  std::vector<worker_v1::WorkerIpcEnvelope> events;
  std::vector<worker_v1::WorkerIpcEnvelope> responses;
  auto authorize = worker_command(1, "session-a");
  const auto ticket_hash = beacon::worker::hash_stream_ticket(
      {reinterpret_cast<const std::byte *>(kRawTicket.data()),
       kRawTicket.size()});
  auto *ticket = authorize.mutable_authorize_ticket();
  ticket->set_ticket_hash(reinterpret_cast<const char *>(ticket_hash.data()),
                          ticket_hash.size());
  ticket->set_client_id("z-fold-7");
  ticket->set_plan_revision(8);
  ticket->set_expires_at_unix_ms(std::numeric_limits<std::uint64_t>::max());
  ticket->set_worker_instance_id(hello.worker_hello().worker_instance_id());
  if (!exchange_worker_command(channel, authorize, responses, events)) {
    return 87;
  }

  responses.clear();
  auto prepare = worker_command(2, "session-a");
  auto *plan = prepare.mutable_prepare_session();
  plan->set_display_target("display-a");
  plan->set_video_codec(worker_v1::WORKER_VIDEO_CODEC_H264);
  plan->set_width(2560);
  plan->set_height(1600);
  plan->set_frames_per_second_numerator(120);
  plan->set_frames_per_second_denominator(1);
  plan->set_dynamic_range(worker_v1::WORKER_DYNAMIC_RANGE_SDR);
  if (!exchange_worker_command(channel, prepare, responses, events)) {
    return 88;
  }

  responses.clear();
  auto start = worker_command(3, "session-a");
  auto *media = start.mutable_start_media();
  media->set_listen_address("0.0.0.0");
  media->set_listen_port(0);
  media->set_certificate_fingerprint(
      reinterpret_cast<const char *>(fingerprint.data()), fingerprint.size());
  if (!exchange_worker_command(channel, start, responses, events)) {
    return 89;
  }
  const auto transport_ready =
      std::ranges::find_if(responses, [](const auto &response) {
        return response.body_case() ==
               worker_v1::WorkerIpcEnvelope::kWorkerTransportReady;
      });
  if (transport_ready == responses.end() ||
      transport_ready->worker_transport_ready().listener_port() == 0 ||
      !events.empty()) {
    return 90;
  }

  ClientState client;
  client.expected_fingerprint = fingerprint;
  if (!start_client(
          client,
          static_cast<std::uint16_t>(
              transport_ready->worker_transport_ready().listener_port()))) {
    stop_client(client);
    return 91;
  }
  {
    std::unique_lock lock{client.mutex};
    client.changed.wait(lock, [&client] {
      return client.datagram_received || client.failed ||
             client.connection_closed;
    });
    if (!client.datagram_received || client.failed ||
        !client.authenticated || !client.certificate_seen) {
      lock.unlock();
      stop_client(client);
      return 92;
    }
  }
  if (!collect_stream_events(channel, events)) {
    stop_client(client);
    return 93;
  }

  stop_client(client);
  if (!collect_disconnect_event(channel, events)) {
    return 94;
  }

  responses.clear();
  auto shutdown = worker_command(4, "");
  shutdown.mutable_shutdown_worker();
  if (!exchange_worker_command(channel, shutdown, responses, events)) {
    return 95;
  }
  const auto exit_code = process.wait();
  if (!exit_code || *exit_code != 0) {
    return 96;
  }
  std::printf("BEACON_WORKER_IPC_QUIC_OK AUTH INPUT FEEDBACK "
              "ACCESS_UNIT_MARKER "
              "DISCONNECT SHUTDOWN\n");
  return 0;
}

bool callback_fault_is_contained(
    const std::wstring &identity_path,
    const beacon::worker::TicketHash &fingerprint,
    beacon::worker::QuicListenerFaultPoint target,
    std::string_view raw_ticket, bool send_post_auth_messages) {
  beacon::worker::AuthorizedQuicTicketStore tickets;
  if (target !=
      beacon::worker::QuicListenerFaultPoint::connection_context_allocation) {
    beacon::worker::AuthorizedQuicTicket ticket{
        .hash = beacon::worker::hash_stream_ticket(
            {reinterpret_cast<const std::byte *>(raw_ticket.data()),
             raw_ticket.size()}),
        .client_id = "z-fold-7",
        .session_id = "session-a",
        .plan_revision = 8,
        .expires_at_unix_ms = std::numeric_limits<std::uint64_t>::max(),
    };
    if (!tickets.authorize(std::move(ticket))) {
      return false;
    }
  }

  auto injected = std::make_shared<std::atomic_bool>(false);
  beacon::worker::QuicListener listener(
      identity_path, tickets,
      [target, injected](beacon::worker::QuicListenerFaultPoint point) {
        bool expected = false;
        if (point == target && injected->compare_exchange_strong(expected, true)) {
          throw std::bad_alloc{};
        }
      });
  if (!listener.configure_listener("127.0.0.1", 0) ||
      !listener.open_connection()) {
    return false;
  }

  ClientState client;
  client.expected_fingerprint = fingerprint;
  client.raw_ticket = raw_ticket;
  client.send_data_after_auth = send_post_auth_messages;
  if (!start_client(client, listener.local_port())) {
    stop_client(client);
    return false;
  }
  {
    std::unique_lock lock{client.mutex};
    client.changed.wait(lock, [&client] {
      return client.failed || client.connection_closed;
    });
  }
  stop_client(client);
  listener.close_connection();
  listener.shutdown();
  return injected->load() &&
         listener.failure() ==
             beacon::worker::QuicListenerFailure::callback_exception &&
         listener.metrics().live_datagram_send_contexts == 0;
}

bool disconnect_callback_fault_is_contained(
    const std::wstring &identity_path,
    const beacon::worker::TicketHash &fingerprint,
    beacon::worker::QuicListenerFaultPoint target,
    std::string_view raw_ticket) {
  beacon::worker::AuthorizedQuicTicketStore tickets;
  beacon::worker::AuthorizedQuicTicket ticket{
      .hash = beacon::worker::hash_stream_ticket(
          {reinterpret_cast<const std::byte *>(raw_ticket.data()),
           raw_ticket.size()}),
      .client_id = "z-fold-7",
      .session_id = "session-a",
      .plan_revision = 8,
      .expires_at_unix_ms = std::numeric_limits<std::uint64_t>::max(),
  };
  if (!tickets.authorize(std::move(ticket))) {
    return false;
  }

  auto injected = std::make_shared<std::atomic_bool>(false);
  beacon::worker::QuicListener listener(
      identity_path, tickets,
      [target, injected](beacon::worker::QuicListenerFaultPoint point) {
        bool expected = false;
        if (point == target &&
            injected->compare_exchange_strong(expected, true)) {
          throw std::bad_alloc{};
        }
      });
  if (!listener.configure_listener("127.0.0.1", 0) ||
      !listener.open_connection()) {
    return false;
  }

  ClientState client;
  client.expected_fingerprint = fingerprint;
  client.raw_ticket = raw_ticket;
  client.send_data_after_auth = false;
  if (!start_client(client, listener.local_port())) {
    stop_client(client);
    return false;
  }
  {
    std::unique_lock lock{client.mutex};
    client.changed.wait(lock, [&client] {
      return client.authenticated || client.failed || client.connection_closed;
    });
    if (!client.authenticated || client.failed) {
      lock.unlock();
      stop_client(client);
      return false;
    }
  }
  stop_client(client);
  listener.wait_until_disconnected();
  listener.close_connection();
  listener.shutdown();
  return injected->load() &&
         listener.failure() ==
             beacon::worker::QuicListenerFailure::callback_exception &&
         listener.metrics().closed_connection_handles == 1;
}

} // namespace

int wmain(int argument_count, wchar_t **arguments) {
  if (argument_count == 7 && std::wstring_view(arguments[1]) == L"--worker" &&
      std::wstring_view(arguments[3]) == L"--identity" &&
      std::wstring_view(arguments[5]) == L"--fingerprint") {
    beacon::worker::TicketHash fingerprint{};
    const std::wstring_view wide_fingerprint{arguments[6]};
    std::string fingerprint_text;
    fingerprint_text.reserve(wide_fingerprint.size());
    for (const wchar_t value : wide_fingerprint) {
      if (value < 0 || value > 0x7f) {
        return 65;
      }
      fingerprint_text.push_back(static_cast<char>(value));
    }
    if (!decode_fingerprint(fingerprint_text, fingerprint)) {
      return 66;
    }
    return run_worker_process_probe(arguments[2], arguments[4], fingerprint);
  }
  if (argument_count != 4)
    return 64;
  beacon::worker::TicketHash expected_fingerprint{};
  const std::wstring fingerprint_wide{arguments[2]};
  std::string fingerprint;
  fingerprint.reserve(fingerprint_wide.size());
  for (const wchar_t value : fingerprint_wide) {
    if (value < 0 || value > 0x7f)
      return 65;
    fingerprint.push_back(static_cast<char>(value));
  }
  if (!decode_fingerprint(fingerprint, expected_fingerprint))
    return 66;

  if (!callback_fault_is_contained(
          arguments[1], expected_fingerprint,
          beacon::worker::QuicListenerFaultPoint::connection_context_allocation,
          "unused-connection-fault-ticket", false))
    return 24;
  if (!callback_fault_is_contained(
          arguments[1], expected_fingerprint,
          beacon::worker::QuicListenerFaultPoint::event_serialization,
          "loopback-ticket-event-fault", false))
    return 25;
  if (!callback_fault_is_contained(
          arguments[1], expected_fingerprint,
          beacon::worker::QuicListenerFaultPoint::datagram_context_allocation,
          "loopback-ticket-datagram-fault", true))
    return 26;
  if (!callback_fault_is_contained(
          arguments[1], expected_fingerprint,
          beacon::worker::QuicListenerFaultPoint::datagram_final_state_telemetry,
          "loopback-ticket-datagram-final-fault", true))
    return 29;
  if (!disconnect_callback_fault_is_contained(
          arguments[1], expected_fingerprint,
          beacon::worker::QuicListenerFaultPoint::disconnect_event_construction,
          "loopback-ticket-disconnect-construction-fault"))
    return 27;
  if (!disconnect_callback_fault_is_contained(
          arguments[1], expected_fingerprint,
          beacon::worker::QuicListenerFaultPoint::disconnect_event_publication,
          "loopback-ticket-disconnect-publication-fault"))
    return 28;

  beacon::worker::AuthorizedQuicTicketStore retry_tickets;
  beacon::worker::AuthorizedQuicTicket retry_ticket{
      .hash = beacon::worker::hash_stream_ticket(
          {reinterpret_cast<const std::byte *>(kRawTicket.data()),
           kRawTicket.size()}),
      .client_id = "z-fold-7",
      .session_id = "session-a",
      .plan_revision = 8,
      .expires_at_unix_ms = std::numeric_limits<std::uint64_t>::max(),
  };
  if (!retry_tickets.authorize(std::move(retry_ticket)))
    return 67;
  beacon::worker::QuicListener retry_listener(arguments[3], retry_tickets);
  if (!retry_listener.configure_listener("127.0.0.1", 0) ||
      retry_listener.open_connection() ||
      retry_listener.failure() !=
          beacon::worker::QuicListenerFailure::identity_import)
    return 68;
  std::error_code copy_error;
  std::filesystem::copy_file(arguments[1], arguments[3],
                             std::filesystem::copy_options::overwrite_existing,
                             copy_error);
  if (copy_error || !retry_listener.open_connection() ||
      retry_listener.failure() != beacon::worker::QuicListenerFailure::none)
    return 69;
  ClientState retry_client;
  retry_client.expected_fingerprint = expected_fingerprint;
  retry_client.send_data_after_auth = false;
  if (!start_client(retry_client, retry_listener.local_port()))
    return 70;
  {
    std::unique_lock lock{retry_client.mutex};
    retry_client.changed.wait(lock, [&retry_client] {
      return retry_client.authenticated || retry_client.failed ||
             retry_client.connection_closed;
    });
    if (retry_client.failed || !retry_client.authenticated ||
        !retry_client.certificate_seen)
      return 71;
  }
  stop_client(retry_client);
  retry_listener.wait_until_disconnected();
  retry_listener.close_connection();
  retry_listener.shutdown();

  beacon::worker::AuthorizedQuicTicketStore tickets;
  beacon::worker::AuthorizedQuicTicket ticket{
      .hash = beacon::worker::hash_stream_ticket(
          {reinterpret_cast<const std::byte *>(kRawTicket.data()),
           kRawTicket.size()}),
      .client_id = "z-fold-7",
      .session_id = "session-a",
      .plan_revision = 8,
      .expires_at_unix_ms = std::numeric_limits<std::uint64_t>::max(),
  };
  if (!tickets.authorize(std::move(ticket)))
    return 1;

  beacon::worker::QuicListener listener(arguments[1], tickets);
  std::mutex event_mutex;
  std::vector<beacon::worker::v1::WorkerIpcEnvelope> worker_events;
  listener.set_event_sink(
      [&event_mutex, &worker_events](
          beacon::worker::v1::WorkerIpcEnvelope event) {
        std::lock_guard lock{event_mutex};
        worker_events.push_back(std::move(event));
      });
  if (listener.configure_listener("not-an-address", 0))
    return 2;
  if (!listener.configure_listener("127.0.0.1", 0) ||
      !listener.open_connection()) {
    std::fprintf(stderr, "failure=%u platform_error=%llu\n",
                 static_cast<unsigned int>(listener.failure()),
                 static_cast<unsigned long long>(listener.platform_error()));
    return 3;
  }

  ClientState client;
  client.expected_fingerprint = expected_fingerprint;
  if (!start_client(client, listener.local_port()))
    return 4;
  {
    std::unique_lock lock{client.mutex};
    client.changed.wait(
        lock, [&client] { return client.authenticated || client.failed; });
    if (client.failed || !client.certificate_seen)
      return 5;
  }
  if (!listener.wait_until_media_ready())
    return 6;
  {
    std::unique_lock lock{client.mutex};
    client.changed.wait(
        lock, [&client] { return client.datagram_received || client.failed; });
    if (client.failed)
      return 9;
  }
  if (!listener.wait_for_received_packets(3))
    return 10;
  const auto packets = listener.take_received_packets();
  const bool session = std::ranges::any_of(packets, [](const auto &packet) {
    return packet.channel == beacon::stream::StreamChannel::session;
  });
  const bool input = std::ranges::any_of(packets, [](const auto &packet) {
    return packet.channel == beacon::stream::StreamChannel::input;
  });
  const bool feedback = std::ranges::any_of(packets, [](const auto &packet) {
    return packet.channel == beacon::stream::StreamChannel::feedback;
  });
  if (!session || !input || !feedback)
    return 11;
  {
    std::lock_guard lock{event_mutex};
    const auto has_event = [&worker_events](auto body_case) {
      return std::ranges::any_of(worker_events, [body_case](const auto &event) {
        return event.protocol_version() == 1 && event.request_id() == 0 &&
               event.session_id() == "session-a" &&
               event.body_case() == body_case;
      });
    };
    if (!has_event(beacon::worker::v1::WorkerIpcEnvelope::
                       kTransportAuthenticated) ||
        !has_event(beacon::worker::v1::WorkerIpcEnvelope::kInputReceived) ||
        !has_event(beacon::worker::v1::WorkerIpcEnvelope::kFeedbackReceived) ||
        !has_event(beacon::worker::v1::WorkerIpcEnvelope::kMediaEvidence))
      return 13;
    if (std::ranges::any_of(worker_events, [](const auto &event) {
          switch (event.body_case()) {
          case beacon::worker::v1::WorkerIpcEnvelope::
              kTransportAuthenticated:
            return event.transport_authenticated().session_generation() != 1;
          case beacon::worker::v1::WorkerIpcEnvelope::kInputReceived:
            return event.input_received().session_generation() != 1 ||
                   event.input_received().input().sequence() != 1;
          case beacon::worker::v1::WorkerIpcEnvelope::kFeedbackReceived:
            return event.feedback_received().session_generation() != 1 ||
                   event.feedback_received().feedback().sequence() != 1;
          case beacon::worker::v1::WorkerIpcEnvelope::kMediaEvidence:
            return event.media_evidence().session_generation() != 1 ||
                   event.media_evidence().sequence() != 1;
          default:
            return false;
          }
        }))
      return 14;
  }
  const auto metrics = listener.metrics();
  if (metrics.sent_datagrams == 0 || metrics.session_messages != 1 ||
      metrics.input_messages != 1 || metrics.feedback_messages != 1 ||
      metrics.path_mtu == 0)
    return 12;

  stop_client(client);
  listener.wait_until_disconnected();
  {
    std::lock_guard lock{event_mutex};
    if (worker_events.empty() ||
        worker_events.back().body_case() !=
            beacon::worker::v1::WorkerIpcEnvelope::kTransportDisconnected ||
        worker_events.back()
                .transport_disconnected()
                .session_generation() != 1)
      return 15;
  }

  ClientState replay_client;
  replay_client.expected_fingerprint = expected_fingerprint;
  if (!start_client(replay_client, listener.local_port()))
    return 11;
  {
    std::unique_lock lock{replay_client.mutex};
    replay_client.changed.wait(lock, [&replay_client] {
      return replay_client.failed || replay_client.authenticated ||
             replay_client.connection_closed;
    });
    if (!replay_client.failed || !replay_client.certificate_seen ||
        replay_client.authentication_error !=
            stream_v1::SESSION_ERROR_CODE_TICKET_REPLAYED)
      return 12;
  }
  stop_client(replay_client);
  listener.wait_until_disconnected();

  ClientState version_client;
  version_client.expected_fingerprint = expected_fingerprint;
  version_client.protocol_version = 2;
  if (!start_client(version_client, listener.local_port()))
    return 13;
  {
    std::unique_lock lock{version_client.mutex};
    version_client.changed.wait(lock, [&version_client] {
      return version_client.failed || version_client.authenticated ||
             version_client.connection_closed;
    });
    if (!version_client.failed || !version_client.certificate_seen ||
        version_client.authentication_error !=
            stream_v1::SESSION_ERROR_CODE_UNSUPPORTED_VERSION)
      return 14;
  }
  stop_client(version_client);
  listener.wait_until_disconnected();

  ClientState rejected_client;
  rejected_client.expected_fingerprint = expected_fingerprint;
  rejected_client.expected_fingerprint[0] ^= std::byte{0xff};
  if (!start_client(rejected_client, listener.local_port()))
    return 15;
  {
    std::unique_lock lock{rejected_client.mutex};
    rejected_client.changed.wait(lock, [&rejected_client] {
      return rejected_client.failed || rejected_client.authenticated ||
             rejected_client.connection_closed;
    });
    if (!rejected_client.failed || !rejected_client.certificate_seen ||
        rejected_client.authenticated)
      return 16;
  }
  stop_client(rejected_client);
  listener.wait_until_disconnected();

  ClientState wrong_alpn_client;
  wrong_alpn_client.expected_fingerprint = expected_fingerprint;
  if (!start_client(wrong_alpn_client, listener.local_port(),
                    "beacon-stream/2"))
    return 17;
  {
    std::unique_lock lock{wrong_alpn_client.mutex};
    wrong_alpn_client.changed.wait(lock, [&wrong_alpn_client] {
      return wrong_alpn_client.failed || wrong_alpn_client.connection_closed;
    });
    if (!wrong_alpn_client.failed || wrong_alpn_client.authenticated ||
        wrong_alpn_client.certificate_seen)
      return 18;
  }
  stop_client(wrong_alpn_client);
  listener.wait_until_disconnected();

  constexpr std::string_view fresh_raw_ticket{"loopback-ticket-fresh"};
  beacon::worker::AuthorizedQuicTicket fresh_ticket{
      .hash = beacon::worker::hash_stream_ticket(
          {reinterpret_cast<const std::byte *>(fresh_raw_ticket.data()),
           fresh_raw_ticket.size()}),
      .client_id = "z-fold-7",
      .session_id = "session-a",
      .plan_revision = 8,
      .expires_at_unix_ms = std::numeric_limits<std::uint64_t>::max(),
  };
  if (!tickets.authorize(std::move(fresh_ticket)))
    return 19;
  ClientState fresh_client;
  fresh_client.expected_fingerprint = expected_fingerprint;
  fresh_client.raw_ticket = fresh_raw_ticket;
  fresh_client.expected_marker_sequence = 2;
  if (!start_client(fresh_client, listener.local_port()))
    return 20;
  {
    std::unique_lock lock{fresh_client.mutex};
    fresh_client.changed.wait(lock, [&fresh_client] {
      return fresh_client.failed || fresh_client.authenticated ||
             fresh_client.connection_closed;
    });
    fresh_client.changed.wait(lock, [&fresh_client] {
      return fresh_client.datagram_received || fresh_client.failed;
    });
    if (fresh_client.failed || !fresh_client.authenticated ||
        !fresh_client.datagram_received ||
        !fresh_client.certificate_seen)
      return 21;
  }
  stop_client(fresh_client, 77);
  listener.wait_until_disconnected();
  {
    std::lock_guard lock{event_mutex};
    std::vector<std::pair<std::uint64_t, std::uint64_t>> media_evidence;
    for (const auto &event : worker_events) {
      if (event.body_case() ==
          beacon::worker::v1::WorkerIpcEnvelope::kMediaEvidence) {
        media_evidence.emplace_back(event.media_evidence().session_generation(),
                                    event.media_evidence().sequence());
      }
    }
    const auto has_connection_diagnostic =
        [&worker_events](beacon::worker::v1::DiagnosticCode code,
                         std::uint64_t connection_generation) {
          return std::ranges::any_of(worker_events, [&](const auto &event) {
            return event.body_case() ==
                       beacon::worker::v1::WorkerIpcEnvelope::kWorkerDiagnostic &&
                   event.worker_diagnostic().boundary() ==
                       beacon::worker::v1::DIAGNOSTIC_BOUNDARY_TRANSPORT &&
                   event.worker_diagnostic().code() == code &&
                   event.worker_diagnostic().numeric_value() ==
                       connection_generation;
          });
        };
    if (worker_events.empty() ||
        worker_events.front().body_case() !=
            beacon::worker::v1::WorkerIpcEnvelope::kWorkerDiagnostic ||
        worker_events.front().worker_diagnostic().boundary() !=
            beacon::worker::v1::DIAGNOSTIC_BOUNDARY_TRANSPORT ||
        worker_events.front().worker_diagnostic().code() !=
            beacon::worker::v1::DIAGNOSTIC_CODE_CONNECTION_OBSERVED ||
        worker_events.front().worker_diagnostic().numeric_value() != 1 ||
        !has_connection_diagnostic(
            beacon::worker::v1::DIAGNOSTIC_CODE_CONNECTION_CONFIGURED, 1) ||
        !has_connection_diagnostic(
            beacon::worker::v1::DIAGNOSTIC_CODE_TRANSPORT_CONNECTED, 1) ||
        media_evidence !=
            std::vector<std::pair<std::uint64_t, std::uint64_t>>{{1, 1},
                                                                 {2, 2}} ||
        worker_events.empty() ||
        worker_events.back().body_case() !=
            beacon::worker::v1::WorkerIpcEnvelope::kTransportDisconnected ||
        worker_events.back().transport_disconnected().session_generation() != 2)
      return 23;
  }
  const auto close_events = listener.take_transport_events();
  const bool peer_abort =
      std::ranges::any_of(close_events, [](const auto &event) {
        return event.kind == beacon::stream::MsQuicEventKind::peer_closed &&
               event.error_code == 77;
      });
  if (!peer_abort)
    return 22;
  listener.close_connection();
  listener.shutdown();
  std::printf("BEACON_QUIC_LOOPBACK_OK %u CERT_PIN_OK ALPN_VERSION_OK "
              "REPLAY_RECONNECT_OK CALLBACK_FAULTS_OK "
              "DISCONNECT_FAULTS_OK\n",
              static_cast<unsigned int>(packets.size()));
  return 0;
}
