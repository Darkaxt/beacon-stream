#include <msquic.h>

// The MsQuic callback/resource lifecycle follows microsoft/msquic's MIT-licensed
// src/tools/sample/sample.c at revision 87b53085d76bd7920d490a6f226c9999b6614d14.

#include <array>
#include <condition_variable>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <mutex>
#include <string>
#include <string_view>

namespace {

constexpr std::string_view reliable_message = "beacon-reliable-proof";
constexpr std::string_view datagram_message = "beacon-datagram-proof";
constexpr std::string_view alpn_value = "beacon-stream/1";

struct proof_state {
  const QUIC_API_TABLE *api = nullptr;
  HQUIC configuration = nullptr;
  HQUIC connection = nullptr;
  std::mutex mutex;
  std::condition_variable changed;
  std::string reliable_bytes;
  bool reliable_received = false;
  bool datagram_received = false;
  bool connected = false;
  bool shutdown_requested = false;
  bool completed = false;
  bool success = false;
  std::array<std::uint8_t, reliable_message.size()> reliable_storage {};
  std::array<std::uint8_t, datagram_message.size()> datagram_storage {};
  QUIC_BUFFER reliable_buffer {};
  QUIC_BUFFER datagram_buffer {};
};

bool buffer_equals(const QUIC_BUFFER &buffer, std::string_view expected) {
  return buffer.Length == expected.size() &&
         std::memcmp(buffer.Buffer, expected.data(), expected.size()) == 0;
}

void complete(proof_state &state, bool success) {
  {
    std::lock_guard lock {state.mutex};
    state.success = success;
    state.completed = true;
  }
  state.changed.notify_all();
}

void request_server_shutdown(proof_state &state, HQUIC connection) {
  bool should_shutdown = false;
  {
    std::lock_guard lock {state.mutex};
    should_shutdown = state.reliable_received && state.datagram_received && !state.shutdown_requested;
    if (should_shutdown) {
      state.shutdown_requested = true;
    }
  }

  if (should_shutdown) {
    state.api->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 0);
  }
}

QUIC_STATUS QUIC_API server_stream_callback(
    HQUIC stream,
    void *context,
    QUIC_STREAM_EVENT *event) {
  auto &state = *static_cast<proof_state *>(context);
  switch (event->Type) {
    case QUIC_STREAM_EVENT_RECEIVE:
      {
        std::lock_guard lock {state.mutex};
        for (std::uint32_t index = 0; index < event->RECEIVE.BufferCount; ++index) {
          const QUIC_BUFFER &buffer = event->RECEIVE.Buffers[index];
          state.reliable_bytes.append(
              reinterpret_cast<const char *>(buffer.Buffer),
              buffer.Length);
        }
        state.reliable_received = state.reliable_bytes == reliable_message;
      }
      request_server_shutdown(state, state.connection);
      break;
    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE:
      state.api->StreamClose(stream);
      break;
    default:
      break;
  }
  return QUIC_STATUS_SUCCESS;
}

QUIC_STATUS QUIC_API server_connection_callback(
    HQUIC connection,
    void *context,
    QUIC_CONNECTION_EVENT *event) {
  auto &state = *static_cast<proof_state *>(context);
  switch (event->Type) {
    case QUIC_CONNECTION_EVENT_CONNECTED:
      {
        std::lock_guard lock {state.mutex};
        state.connected = true;
      }
      break;
    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED:
      state.api->SetCallbackHandler(
          event->PEER_STREAM_STARTED.Stream,
          reinterpret_cast<void *>(server_stream_callback),
          &state);
      break;
    case QUIC_CONNECTION_EVENT_DATAGRAM_RECEIVED:
      {
        std::lock_guard lock {state.mutex};
        state.datagram_received = buffer_equals(*event->DATAGRAM_RECEIVED.Buffer, datagram_message);
      }
      request_server_shutdown(state, connection);
      break;
    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT:
      complete(state, false);
      break;
    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE:
      {
        bool success;
        {
          std::lock_guard lock {state.mutex};
          success = state.connected && state.reliable_received && state.datagram_received;
        }
        complete(state, success);
      }
      state.api->ConnectionClose(connection);
      break;
    default:
      break;
  }
  return QUIC_STATUS_SUCCESS;
}

QUIC_STATUS QUIC_API server_listener_callback(
    HQUIC,
    void *context,
    QUIC_LISTENER_EVENT *event) {
  auto &state = *static_cast<proof_state *>(context);
  if (event->Type != QUIC_LISTENER_EVENT_NEW_CONNECTION) {
    return QUIC_STATUS_NOT_SUPPORTED;
  }

  state.api->SetCallbackHandler(
      event->NEW_CONNECTION.Connection,
      reinterpret_cast<void *>(server_connection_callback),
      &state);
  {
    std::lock_guard lock {state.mutex};
    state.connection = event->NEW_CONNECTION.Connection;
  }
  return state.api->ConnectionSetConfiguration(
      event->NEW_CONNECTION.Connection,
      state.configuration);
}

QUIC_STATUS QUIC_API client_stream_callback(
    HQUIC stream,
    void *context,
    QUIC_STREAM_EVENT *event) {
  auto &state = *static_cast<proof_state *>(context);
  if (event->Type == QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE) {
    state.api->StreamClose(stream);
  }
  return QUIC_STATUS_SUCCESS;
}

bool client_send(proof_state &state, HQUIC connection) {
  HQUIC stream = nullptr;
  if (QUIC_FAILED(state.api->StreamOpen(
          connection,
          QUIC_STREAM_OPEN_FLAG_NONE,
          client_stream_callback,
          &state,
          &stream))) {
    return false;
  }

  if (QUIC_FAILED(state.api->StreamStart(stream, QUIC_STREAM_START_FLAG_NONE))) {
    state.api->StreamClose(stream);
    return false;
  }

  std::memcpy(state.reliable_storage.data(), reliable_message.data(), reliable_message.size());
  state.reliable_buffer = {
      static_cast<std::uint32_t>(state.reliable_storage.size()),
      state.reliable_storage.data()};
  if (QUIC_FAILED(state.api->StreamSend(
          stream,
          &state.reliable_buffer,
          1,
          QUIC_SEND_FLAG_FIN,
          nullptr))) {
    state.api->StreamShutdown(stream, QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 1);
    return false;
  }

  std::memcpy(state.datagram_storage.data(), datagram_message.data(), datagram_message.size());
  state.datagram_buffer = {
      static_cast<std::uint32_t>(state.datagram_storage.size()),
      state.datagram_storage.data()};
  return QUIC_SUCCEEDED(state.api->DatagramSend(
      connection,
      &state.datagram_buffer,
      1,
      QUIC_SEND_FLAG_NONE,
      nullptr));
}

QUIC_STATUS QUIC_API client_connection_callback(
    HQUIC connection,
    void *context,
    QUIC_CONNECTION_EVENT *event) {
  auto &state = *static_cast<proof_state *>(context);
  switch (event->Type) {
    case QUIC_CONNECTION_EVENT_CONNECTED:
      {
        std::lock_guard lock {state.mutex};
        state.connected = true;
      }
      if (!client_send(state, connection)) {
        state.api->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 2);
      }
      break;
    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT:
      complete(state, false);
      break;
    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_PEER:
      {
        std::lock_guard lock {state.mutex};
        state.success = event->SHUTDOWN_INITIATED_BY_PEER.ErrorCode == 0;
      }
      break;
    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE:
      {
        bool success;
        {
          std::lock_guard lock {state.mutex};
          success = state.connected && state.success;
        }
        complete(state, success);
      }
      state.api->ConnectionClose(connection);
      break;
    default:
      break;
  }
  return QUIC_STATUS_SUCCESS;
}

bool decode_hash(std::string_view value, QUIC_CERTIFICATE_HASH &hash) {
  if (value.size() != sizeof(hash.ShaHash) * 2) {
    return false;
  }
  auto hex = [](char character) -> int {
    if (character >= '0' && character <= '9') return character - '0';
    if (character >= 'a' && character <= 'f') return character - 'a' + 10;
    if (character >= 'A' && character <= 'F') return character - 'A' + 10;
    return -1;
  };
  for (std::size_t index = 0; index < sizeof(hash.ShaHash); ++index) {
    const int high = hex(value[index * 2]);
    const int low = hex(value[index * 2 + 1]);
    if (high < 0 || low < 0) return false;
    hash.ShaHash[index] = static_cast<std::uint8_t>((high << 4) | low);
  }
  return true;
}

int run_server(const QUIC_API_TABLE *api, HQUIC registration, std::uint16_t port, std::string_view hash_text) {
  proof_state state;
  state.api = api;

  QUIC_SETTINGS settings {};
  settings.PeerBidiStreamCount = 1;
  settings.IsSet.PeerBidiStreamCount = true;
  settings.DatagramReceiveEnabled = true;
  settings.IsSet.DatagramReceiveEnabled = true;
  const QUIC_BUFFER alpn {
      static_cast<std::uint32_t>(alpn_value.size()),
      reinterpret_cast<std::uint8_t *>(const_cast<char *>(alpn_value.data()))};
  if (QUIC_FAILED(api->ConfigurationOpen(
          registration, &alpn, 1, &settings, sizeof(settings), nullptr, &state.configuration))) {
    return 2;
  }

  QUIC_CERTIFICATE_HASH hash {};
  if (!decode_hash(hash_text, hash)) return 3;
  QUIC_CREDENTIAL_CONFIG credentials {};
  credentials.Type = QUIC_CREDENTIAL_TYPE_CERTIFICATE_HASH;
  credentials.CertificateHash = &hash;
  if (QUIC_FAILED(api->ConfigurationLoadCredential(state.configuration, &credentials))) return 4;

  HQUIC listener = nullptr;
  if (QUIC_FAILED(api->ListenerOpen(registration, server_listener_callback, &state, &listener))) return 5;
  QUIC_ADDR address {};
  QuicAddrSetFamily(&address, QUIC_ADDRESS_FAMILY_UNSPEC);
  QuicAddrSetPort(&address, port);
  if (QUIC_FAILED(api->ListenerStart(listener, &alpn, 1, &address))) return 6;

  std::puts("BEACON_QUIC_READY");
  std::fflush(stdout);
  {
    std::unique_lock lock {state.mutex};
    state.changed.wait(lock, [&state] { return state.completed; });
  }

  api->ListenerClose(listener);
  api->ConfigurationClose(state.configuration);
  std::puts(state.success ? "BEACON_QUIC_SERVER_OK" : "BEACON_QUIC_SERVER_FAILED");
  return state.success ? 0 : 7;
}

int run_client(const QUIC_API_TABLE *api, HQUIC registration, const char *host, std::uint16_t port) {
  proof_state state;
  state.api = api;

  QUIC_SETTINGS settings {};
  settings.DatagramReceiveEnabled = true;
  settings.IsSet.DatagramReceiveEnabled = true;
  const QUIC_BUFFER alpn {
      static_cast<std::uint32_t>(alpn_value.size()),
      reinterpret_cast<std::uint8_t *>(const_cast<char *>(alpn_value.data()))};
  if (QUIC_FAILED(api->ConfigurationOpen(
          registration, &alpn, 1, &settings, sizeof(settings), nullptr, &state.configuration))) {
    return 2;
  }

  QUIC_CREDENTIAL_CONFIG credentials {};
  credentials.Type = QUIC_CREDENTIAL_TYPE_NONE;
  credentials.Flags = QUIC_CREDENTIAL_FLAG_CLIENT | QUIC_CREDENTIAL_FLAG_NO_CERTIFICATE_VALIDATION;
  if (QUIC_FAILED(api->ConfigurationLoadCredential(state.configuration, &credentials))) return 3;

  HQUIC connection = nullptr;
  if (QUIC_FAILED(api->ConnectionOpen(registration, client_connection_callback, &state, &connection))) return 4;
  if (QUIC_FAILED(api->ConnectionStart(
          connection, state.configuration, QUIC_ADDRESS_FAMILY_UNSPEC, host, port))) {
    api->ConnectionClose(connection);
    return 5;
  }

  {
    std::unique_lock lock {state.mutex};
    state.changed.wait(lock, [&state] { return state.completed; });
  }
  api->ConfigurationClose(state.configuration);
  std::puts(state.success ? "BEACON_QUIC_CLIENT_OK" : "BEACON_QUIC_CLIENT_FAILED");
  return state.success ? 0 : 6;
}

}  // namespace

int main(int argc, char **argv) {
  if (argc < 4) {
    std::fprintf(stderr, "usage: %s server <port> <certificate-sha1> | client <host> <port>\n", argv[0]);
    return 64;
  }

  const QUIC_API_TABLE *api = nullptr;
  if (QUIC_FAILED(MsQuicOpen2(&api))) return 65;
  const QUIC_REGISTRATION_CONFIG registration_config {
      "beacon-msquic-proof", QUIC_EXECUTION_PROFILE_LOW_LATENCY};
  HQUIC registration = nullptr;
  if (QUIC_FAILED(api->RegistrationOpen(&registration_config, &registration))) {
    MsQuicClose(api);
    return 66;
  }

  int result = 67;
  if (std::string_view {argv[1]} == "server") {
    result = run_server(api, registration, static_cast<std::uint16_t>(std::stoi(argv[2])), argv[3]);
  } else if (std::string_view {argv[1]} == "client") {
    result = run_client(api, registration, argv[2], static_cast<std::uint16_t>(std::stoi(argv[3])));
  }

  api->RegistrationClose(registration);
  MsQuicClose(api);
  return result;
}
