#include "beacon/worker/quic_listener.h"

#include "stream_control.pb.h"

#include <Windows.h>
#include <msquic.h>
#include <wincrypt.h>

#include <algorithm>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <mutex>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

constexpr std::string_view kAlpn{"beacon-stream/1"};
constexpr std::string_view kRawTicket{"loopback-ticket"};
namespace stream_v1 = beacon::stream::v1;

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
    control.mutable_request_idr()->set_reason(
        stream_v1::IDR_REQUEST_REASON_DATAGRAM_LOSS);

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
    constexpr std::byte expected[]{std::byte{'B'}, std::byte{42}};
    const auto &buffer = *event->DATAGRAM_RECEIVED.Buffer;
    std::lock_guard lock{state.mutex};
    state.datagram_received =
        buffer.Length == sizeof(expected) &&
        std::memcmp(buffer.Buffer, expected, sizeof(expected)) == 0;
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
    state.api->ConnectionClose(connection);
    {
      std::lock_guard lock{state.mutex};
      state.connection = nullptr;
      state.connection_closed = true;
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
  if (state.connection != nullptr) {
    state.api->ConnectionShutdown(
        state.connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, error_code);
    std::unique_lock lock{state.mutex};
    state.changed.wait(lock, [&state] { return state.connection_closed; });
  }
  if (state.configuration != nullptr)
    state.api->ConfigurationClose(state.configuration);
  if (state.registration != nullptr)
    state.api->RegistrationClose(state.registration);
  if (state.api != nullptr)
    MsQuicClose(state.api);
}

} // namespace

int wmain(int argument_count, wchar_t **arguments) {
  if (argument_count != 3)
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
  if (listener.send(
          {.channel = beacon::stream::StreamChannel::media,
           .sequence = 41,
           .payload = std::vector<std::byte>(70'000, std::byte{1})}) !=
      beacon::stream::TransportSendResult::connection_closed)
    return 7;
  if (listener.send({.channel = beacon::stream::StreamChannel::media,
                     .sequence = 42,
                     .payload = {std::byte{'B'}, std::byte{42}}}) !=
      beacon::stream::TransportSendResult::accepted)
    return 8;
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
  const auto metrics = listener.metrics();
  if (metrics.sent_datagrams == 0 || metrics.session_messages != 1 ||
      metrics.input_messages != 1 || metrics.feedback_messages != 1 ||
      metrics.path_mtu == 0)
    return 12;

  stop_client(client);
  listener.wait_until_disconnected();

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
  fresh_client.send_data_after_auth = false;
  if (!start_client(fresh_client, listener.local_port()))
    return 20;
  {
    std::unique_lock lock{fresh_client.mutex};
    fresh_client.changed.wait(lock, [&fresh_client] {
      return fresh_client.failed || fresh_client.authenticated ||
             fresh_client.connection_closed;
    });
    if (fresh_client.failed || !fresh_client.authenticated ||
        !fresh_client.certificate_seen)
      return 21;
  }
  stop_client(fresh_client, 77);
  listener.wait_until_disconnected();
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
              "REPLAY_RECONNECT_OK\n",
              static_cast<unsigned int>(packets.size()));
  return 0;
}
