#include "beacon/worker/named_pipe_channel.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "worker_ipc.pb.h"

#include <Windows.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <future>
#include <latch>
#include <string>
#include <thread>
#include <vector>

namespace {

using beacon::worker::FrameDecodeStatus;
using beacon::worker::v1::WorkerIpcEnvelope;

struct PipePair {
  HANDLE server{INVALID_HANDLE_VALUE};
  HANDLE client_handle{INVALID_HANDLE_VALUE};
  beacon::worker::NamedPipeChannel client;

  PipePair(HANDLE server_value, HANDLE client_value,
           beacon::worker::NamedPipeChannelOperationHooks hooks = {})
      : server(server_value), client_handle(client_value),
        client(client_value, hooks) {}
  ~PipePair() {
    if (server != INVALID_HANDLE_VALUE) {
      CloseHandle(server);
    }
  }

  PipePair(const PipePair &) = delete;
  PipePair &operator=(const PipePair &) = delete;
};

bool read_exact(HANDLE handle, std::span<std::byte> output);

PipePair create_pipe_pair(
    DWORD buffer_bytes = beacon::worker::maximum_worker_message_bytes + 4,
    beacon::worker::NamedPipeChannelOperationHooks hooks = {}) {
  const auto name = L"\\\\.\\pipe\\beacon-worker-test-" +
                    std::to_wstring(GetCurrentProcessId()) + L"-" +
                    std::to_wstring(GetTickCount64());
  const auto server = CreateNamedPipeW(
      name.c_str(), PIPE_ACCESS_DUPLEX,
      PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1,
      buffer_bytes, buffer_bytes, 0, nullptr);
  BEACON_TEST_REQUIRE(server != INVALID_HANDLE_VALUE);
  const auto client =
      CreateFileW(name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                  OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
  BEACON_TEST_REQUIRE(client != INVALID_HANDLE_VALUE);
  const auto connected = ConnectNamedPipe(server, nullptr);
  BEACON_TEST_REQUIRE(connected != FALSE || GetLastError() == ERROR_PIPE_CONNECTED);
  return PipePair(server, client, hooks);
}

struct OperationGate {
  HANDLE acquired{};
  HANDLE release{};
};

void hold_acquired_operation(void *context) noexcept {
  auto &gate = *static_cast<OperationGate *>(context);
  SetEvent(gate.acquired);
  WaitForSingleObject(gate.release, INFINITE);
}

WorkerIpcEnvelope large_envelope(std::uint64_t request_id) {
  WorkerIpcEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_request_id(request_id);
  envelope.set_session_id("large-frame");
  envelope.mutable_worker_hello()->set_worker_instance_id(
      std::string(900'000, static_cast<char>('a' + request_id % 20)));
  envelope.mutable_worker_hello()->set_process_id(42);
  return envelope;
}

FrameDecodeStatus read_worker_frame(HANDLE handle,
                                    WorkerIpcEnvelope &envelope) {
  std::array<std::byte, 4> prefix{};
  if (!read_exact(handle, prefix)) {
    return FrameDecodeStatus::io_error;
  }
  const auto length = beacon::worker::decode_worker_frame_length(prefix);
  if (length.status != FrameDecodeStatus::success) {
    return length.status;
  }
  std::vector<std::byte> frame(4 + length.message_bytes);
  std::ranges::copy(prefix, frame.begin());
  if (!read_exact(handle, std::span<std::byte>{frame}.subspan(4))) {
    return FrameDecodeStatus::io_error;
  }
  return beacon::worker::decode_worker_frame(frame, envelope);
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


void blocked_write_is_released_by_terminal_cancellation() {
  auto pair = create_pipe_pair(64);
  const auto outbound = large_envelope(101);
  std::promise<void> first_byte_read;
  auto write_started = first_byte_read.get_future();
  bool write_result = true;

  std::thread server_reader([&] {
    std::byte first{};
    BEACON_TEST_REQUIRE(read_exact(pair.server, {&first, 1}));
    first_byte_read.set_value();
  });
  std::thread writer([&] { write_result = pair.client.write(outbound); });

  write_started.wait();
  pair.client.cancel_pending_io();
  writer.join();
  server_reader.join();

  BEACON_TEST_REQUIRE(!write_result);
}

void owner_release_cancels_operation_without_closing_its_live_handle() {
  OperationGate gate{.acquired = CreateEventW(nullptr, TRUE, FALSE, nullptr),
                     .release = CreateEventW(nullptr, TRUE, FALSE, nullptr)};
  BEACON_TEST_REQUIRE(gate.acquired != nullptr);
  BEACON_TEST_REQUIRE(gate.release != nullptr);
  auto pair = create_pipe_pair(
      64, {.after_state_acquired = hold_acquired_operation,
           .context = &gate});
  const auto outbound = large_envelope(102);
  bool write_result = true;

  std::thread writer([&] { write_result = pair.client.write(outbound); });

  BEACON_TEST_REQUIRE(WaitForSingleObject(gate.acquired, INFINITE) ==
                      WAIT_OBJECT_0);
  pair.client.release_owner();
  SetLastError(ERROR_SUCCESS);
  BEACON_TEST_REQUIRE(GetFileType(pair.client_handle) == FILE_TYPE_PIPE);
  BEACON_TEST_REQUIRE(GetLastError() != ERROR_INVALID_HANDLE);

  SetEvent(gate.release);
  writer.join();

  BEACON_TEST_REQUIRE(!write_result);
  BEACON_TEST_REQUIRE(!pair.client.valid());
  SetLastError(ERROR_SUCCESS);
  BEACON_TEST_REQUIRE(GetFileType(pair.client_handle) == FILE_TYPE_UNKNOWN);
  BEACON_TEST_REQUIRE(GetLastError() == ERROR_INVALID_HANDLE);
  CloseHandle(gate.acquired);
  CloseHandle(gate.release);
}

void concurrent_writers_deliver_only_complete_non_interleaved_frames() {
  auto pair = create_pipe_pair(64);
  const auto first = large_envelope(201);
  const auto second = large_envelope(202);
  std::latch start{3};
  bool first_written = false;
  bool second_written = false;
  std::array<WorkerIpcEnvelope, 2> received;
  std::array results{FrameDecodeStatus::io_error,
                     FrameDecodeStatus::io_error};

  std::thread server_reader([&] {
    start.arrive_and_wait();
    results[0] = read_worker_frame(pair.server, received[0]);
    results[1] = read_worker_frame(pair.server, received[1]);
  });
  std::thread first_writer([&] {
    start.arrive_and_wait();
    first_written = pair.client.write(first);
  });
  std::thread second_writer([&] {
    start.arrive_and_wait();
    second_written = pair.client.write(second);
  });

  server_reader.join();
  first_writer.join();
  second_writer.join();

  BEACON_TEST_REQUIRE(first_written);
  BEACON_TEST_REQUIRE(second_written);
  BEACON_TEST_REQUIRE(results[0] == FrameDecodeStatus::success);
  BEACON_TEST_REQUIRE(results[1] == FrameDecodeStatus::success);
  std::array request_ids{received[0].request_id(), received[1].request_id()};
  std::ranges::sort(request_ids);
  BEACON_TEST_REQUIRE(request_ids[0] == 201);
  BEACON_TEST_REQUIRE(request_ids[1] == 202);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    frame_round_trip_uses_network_order_length();
    malformed_and_oversized_frames_are_rejected_before_message_allocation();
    blocked_read_is_released_by_terminal_cancellation();
    one_reader_and_one_writer_are_full_duplex_and_keep_frames_intact();
    blocked_write_is_released_by_terminal_cancellation();
    owner_release_cancels_operation_without_closing_its_live_handle();
    concurrent_writers_deliver_only_complete_non_interleaved_frames();
  });
}
