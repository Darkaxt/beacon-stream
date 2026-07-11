#include "beacon/worker/named_pipe_channel.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "worker_ipc.pb.h"

#include <Windows.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <future>
#include <string>
#include <thread>
#include <vector>

namespace {

using beacon::worker::FrameDecodeStatus;
using beacon::worker::v1::WorkerIpcEnvelope;

struct PipePair {
  HANDLE server{INVALID_HANDLE_VALUE};
  beacon::worker::NamedPipeChannel client;

  PipePair(HANDLE server_handle, HANDLE client_handle)
      : server(server_handle), client(client_handle) {}
  ~PipePair() {
    if (server != INVALID_HANDLE_VALUE) {
      CloseHandle(server);
    }
  }

  PipePair(const PipePair &) = delete;
  PipePair &operator=(const PipePair &) = delete;
};

PipePair create_pipe_pair() {
  const auto name = L"\\\\.\\pipe\\beacon-worker-test-" +
                    std::to_wstring(GetCurrentProcessId()) + L"-" +
                    std::to_wstring(GetTickCount64());
  const auto server = CreateNamedPipeW(
      name.c_str(), PIPE_ACCESS_DUPLEX,
      PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1,
      beacon::worker::maximum_worker_message_bytes + 4,
      beacon::worker::maximum_worker_message_bytes + 4, 0, nullptr);
  BEACON_TEST_REQUIRE(server != INVALID_HANDLE_VALUE);
  const auto client =
      CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                  OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
  BEACON_TEST_REQUIRE(client != INVALID_HANDLE_VALUE);
  const auto connected = ConnectNamedPipe(server, nullptr);
  BEACON_TEST_REQUIRE(connected != FALSE || GetLastError() == ERROR_PIPE_CONNECTED);
  return PipePair(server, client);
}

bool read_exact(HANDLE handle, std::span<std::byte> output) {
  std::size_t offset = 0;
  while (offset < output.size()) {
    DWORD transferred = 0;
    if (ReadFile(handle, output.data() + offset,
                 static_cast<DWORD>(output.size() - offset), &transferred,
                 nullptr) == FALSE ||
        transferred == 0) {
      return false;
    }
    offset += transferred;
  }
  return true;
}

bool write_exact(HANDLE handle, std::span<const std::byte> input) {
  std::size_t offset = 0;
  while (offset < input.size()) {
    DWORD transferred = 0;
    if (WriteFile(handle, input.data() + offset,
                  static_cast<DWORD>(input.size() - offset), &transferred,
                  nullptr) == FALSE ||
        transferred == 0) {
      return false;
    }
    offset += transferred;
  }
  return true;
}

void frame_round_trip_uses_network_order_length() {
  WorkerIpcEnvelope source;
  source.set_protocol_version(1);
  source.set_request_id(7);
  source.set_session_id("s");
  source.mutable_worker_health()->set_active_sessions(2);

  const auto frame = beacon::worker::encode_worker_frame(source);
  WorkerIpcEnvelope decoded;
  const auto result = beacon::worker::decode_worker_frame(frame, decoded);

  BEACON_TEST_REQUIRE(result == FrameDecodeStatus::success);
  BEACON_TEST_REQUIRE(frame[0] == std::byte{0});
  BEACON_TEST_REQUIRE(frame[1] == std::byte{0});
  BEACON_TEST_REQUIRE(decoded.request_id() == 7);
  BEACON_TEST_REQUIRE(decoded.worker_health().active_sessions() == 2);
}

void malformed_and_oversized_frames_are_rejected_before_message_allocation() {
  WorkerIpcEnvelope decoded;
  constexpr std::array short_prefix{std::byte{0}, std::byte{0}, std::byte{0}};
  BEACON_TEST_REQUIRE(
      beacon::worker::decode_worker_frame(short_prefix, decoded) ==
      FrameDecodeStatus::malformed_length);

  constexpr std::array oversized_prefix{
      std::byte{0}, std::byte{0x10}, std::byte{0}, std::byte{1}};
  const auto length = beacon::worker::decode_worker_frame_length(oversized_prefix);
  BEACON_TEST_REQUIRE(length.status == FrameDecodeStatus::message_too_large);
  BEACON_TEST_REQUIRE(length.message_bytes == 0);

  constexpr std::array invalid_message{
      std::byte{0}, std::byte{0}, std::byte{0}, std::byte{1}, std::byte{0xff}};
  BEACON_TEST_REQUIRE(
      beacon::worker::decode_worker_frame(invalid_message, decoded) ==
      FrameDecodeStatus::invalid_protobuf);
}

void blocked_read_is_released_by_terminal_cancellation() {
  auto pair = create_pipe_pair();
  std::promise<void> read_started;
  auto started = read_started.get_future();
  FrameDecodeStatus result = FrameDecodeStatus::success;
  std::thread reader([&] {
    read_started.set_value();
    WorkerIpcEnvelope envelope;
    result = pair.client.read(envelope);
  });
  started.wait();

  pair.client.cancel_pending_io();
  reader.join();

  BEACON_TEST_REQUIRE(result == FrameDecodeStatus::io_error);
}

void one_reader_and_one_writer_are_full_duplex_and_keep_frames_intact() {
  auto pair = create_pipe_pair();
  WorkerIpcEnvelope inbound;
  inbound.set_protocol_version(1);
  inbound.set_request_id(91);
  inbound.set_session_id("from-server");
  inbound.mutable_worker_health()->set_active_sessions(1);
  WorkerIpcEnvelope outbound;
  outbound.set_protocol_version(1);
  outbound.set_request_id(92);
  outbound.set_session_id("from-worker");
  outbound.mutable_worker_health()->set_active_sessions(2);
  const auto inbound_frame = beacon::worker::encode_worker_frame(inbound);
  std::vector<std::byte> outbound_frame;
  WorkerIpcEnvelope received;
  FrameDecodeStatus read_result = FrameDecodeStatus::io_error;
  bool write_result = false;

  std::thread server_writer([&] {
    BEACON_TEST_REQUIRE(write_exact(pair.server, inbound_frame));
  });
  std::thread server_reader([&] {
    std::array<std::byte, 4> prefix{};
    BEACON_TEST_REQUIRE(read_exact(pair.server, prefix));
    const auto length = beacon::worker::decode_worker_frame_length(prefix);
    BEACON_TEST_REQUIRE(length.status == FrameDecodeStatus::success);
    outbound_frame.resize(4 + length.message_bytes);
    std::ranges::copy(prefix, outbound_frame.begin());
    BEACON_TEST_REQUIRE(read_exact(
        pair.server, std::span<std::byte>{outbound_frame}.subspan(4)));
  });
  std::thread client_reader(
      [&] { read_result = pair.client.read(received); });
  std::thread client_writer(
      [&] { write_result = pair.client.write(outbound); });

  server_writer.join();
  server_reader.join();
  client_reader.join();
  client_writer.join();

  WorkerIpcEnvelope decoded_outbound;
  BEACON_TEST_REQUIRE(read_result == FrameDecodeStatus::success);
  BEACON_TEST_REQUIRE(write_result);
  BEACON_TEST_REQUIRE(received.request_id() == 91);
  BEACON_TEST_REQUIRE(
      beacon::worker::decode_worker_frame(outbound_frame, decoded_outbound) ==
      FrameDecodeStatus::success);
  BEACON_TEST_REQUIRE(decoded_outbound.request_id() == 92);
  BEACON_TEST_REQUIRE(decoded_outbound.session_id() == "from-worker");
}

}  // namespace

int main() {
  frame_round_trip_uses_network_order_length();
  malformed_and_oversized_frames_are_rejected_before_message_allocation();
  blocked_read_is_released_by_terminal_cancellation();
  one_reader_and_one_writer_are_full_duplex_and_keep_frames_intact();
  return 0;
}
