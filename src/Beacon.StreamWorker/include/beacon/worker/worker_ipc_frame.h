#pragma once

#include "beacon/worker/worker_ipc_limits.h"
#include "worker_ipc.pb.h"

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace beacon::worker {

enum class FrameDecodeStatus {
  success,
  malformed_length,
  message_too_large,
  size_mismatch,
  invalid_protobuf,
  pipe_closed,
  io_error,
};

struct FrameLengthResult {
  FrameDecodeStatus status{FrameDecodeStatus::malformed_length};
  std::uint32_t message_bytes{};
};

[[nodiscard]] FrameLengthResult
decode_worker_frame_length(std::span<const std::byte> prefix) noexcept;
[[nodiscard]] std::vector<std::byte>
encode_worker_frame(const v1::WorkerIpcEnvelope &envelope);
[[nodiscard]] FrameDecodeStatus
decode_worker_frame(std::span<const std::byte> frame,
                    v1::WorkerIpcEnvelope &envelope) noexcept;

} // namespace beacon::worker
