#include "beacon/worker/worker_ipc_frame.h"

#include <limits>
#include <stdexcept>

namespace beacon::worker {
namespace {

std::uint32_t read_u32(std::span<const std::byte> input) noexcept {
  std::uint32_t value = 0;
  for (const auto byte : input) {
    value = (value << 8U) | std::to_integer<std::uint32_t>(byte);
  }
  return value;
}

void write_u32(std::span<std::byte, 4> output, std::uint32_t value) noexcept {
  for (std::size_t index = 0; index < output.size(); ++index) {
    output[index] = static_cast<std::byte>(
        (value >> ((3U - index) * 8U)) & 0xffU);
  }
}

} // namespace

FrameLengthResult
decode_worker_frame_length(std::span<const std::byte> prefix) noexcept {
  if (prefix.size() != sizeof(std::uint32_t)) {
    return {.status = FrameDecodeStatus::malformed_length, .message_bytes = 0};
  }
  const auto message_bytes = read_u32(prefix);
  if (message_bytes > maximum_worker_message_bytes) {
    return {.status = FrameDecodeStatus::message_too_large,
            .message_bytes = 0};
  }
  return {.status = FrameDecodeStatus::success,
          .message_bytes = message_bytes};
}

std::vector<std::byte>
encode_worker_frame(const v1::WorkerIpcEnvelope &envelope) {
  const auto message_size = envelope.ByteSizeLong();
  if (message_size > maximum_worker_message_bytes ||
      message_size > std::numeric_limits<std::uint32_t>::max()) {
    throw std::length_error("Worker IPC message exceeds the maximum frame size.");
  }
  std::vector<std::byte> frame(sizeof(std::uint32_t) + message_size);
  write_u32(std::span<std::byte, 4>{frame.data(), 4},
            static_cast<std::uint32_t>(message_size));
  if (!envelope.SerializeToArray(frame.data() + sizeof(std::uint32_t),
                                 static_cast<int>(message_size))) {
    throw std::runtime_error("Worker IPC message serialization failed.");
  }
  return frame;
}

FrameDecodeStatus decode_worker_frame(
    std::span<const std::byte> frame,
    v1::WorkerIpcEnvelope &envelope) noexcept {
  if (frame.size() < sizeof(std::uint32_t)) {
    return FrameDecodeStatus::malformed_length;
  }
  const auto length =
      decode_worker_frame_length(frame.first(sizeof(std::uint32_t)));
  if (length.status != FrameDecodeStatus::success) {
    return length.status;
  }
  if (frame.size() - sizeof(std::uint32_t) != length.message_bytes) {
    return FrameDecodeStatus::size_mismatch;
  }
  if (!envelope.ParseFromArray(frame.data() + sizeof(std::uint32_t),
                               static_cast<int>(length.message_bytes))) {
    return FrameDecodeStatus::invalid_protobuf;
  }
  return FrameDecodeStatus::success;
}

} // namespace beacon::worker
