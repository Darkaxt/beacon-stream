#pragma once

#include "worker_ipc.pb.h"

#include <cstddef>
#include <cstdint>
#include <span>
#include <string>
#include <vector>

namespace beacon::worker {

inline constexpr std::uint32_t maximum_worker_message_bytes = 1024U * 1024U;

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

[[nodiscard]] FrameLengthResult decode_worker_frame_length(
    std::span<const std::byte> prefix) noexcept;
[[nodiscard]] std::vector<std::byte> encode_worker_frame(
    const v1::WorkerIpcEnvelope& envelope);
[[nodiscard]] FrameDecodeStatus decode_worker_frame(
    std::span<const std::byte> frame,
    v1::WorkerIpcEnvelope& envelope) noexcept;

class NamedPipeChannel {
 public:
  NamedPipeChannel() = default;
  explicit NamedPipeChannel(void* handle) noexcept;
  ~NamedPipeChannel();

  NamedPipeChannel(const NamedPipeChannel&) = delete;
  NamedPipeChannel& operator=(const NamedPipeChannel&) = delete;
  NamedPipeChannel(NamedPipeChannel&& other) noexcept;
  NamedPipeChannel& operator=(NamedPipeChannel&& other) noexcept;

  [[nodiscard]] static NamedPipeChannel connect(const std::wstring& pipe_name);
  [[nodiscard]] bool valid() const noexcept;
  [[nodiscard]] FrameDecodeStatus read(v1::WorkerIpcEnvelope& envelope) noexcept;
  [[nodiscard]] bool write(const v1::WorkerIpcEnvelope& envelope) noexcept;

 private:
  [[nodiscard]] bool read_exact(std::span<std::byte> output) noexcept;
  [[nodiscard]] bool write_exact(std::span<const std::byte> input) noexcept;
  void close() noexcept;

  void* handle_{};
  std::uint32_t last_error_{};
};

}  // namespace beacon::worker
