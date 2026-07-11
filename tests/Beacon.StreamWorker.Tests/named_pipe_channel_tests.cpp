#include "beacon/worker/named_pipe_channel.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "worker_ipc.pb.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace {

using beacon::worker::FrameDecodeStatus;
using beacon::worker::v1::WorkerIpcEnvelope;

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

}  // namespace

int main() {
  frame_round_trip_uses_network_order_length();
  malformed_and_oversized_frames_are_rejected_before_message_allocation();
  return 0;
}
