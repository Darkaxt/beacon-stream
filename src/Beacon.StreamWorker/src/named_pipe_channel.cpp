#include "beacon/worker/named_pipe_channel.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <limits>
#include <stdexcept>
#include <utility>

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
    output[index] = static_cast<std::byte>((value >> ((3U - index) * 8U)) & 0xffU);
  }
}

bool overlapped_transfer(HANDLE handle,
                         void* buffer,
                         DWORD bytes,
                         bool write,
                         DWORD& transferred) noexcept {
  const auto event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  if (event == nullptr) {
    return false;
  }
  OVERLAPPED operation{};
  operation.hEvent = event;
  const auto started = write ? WriteFile(handle, buffer, bytes, &transferred, &operation)
                             : ReadFile(handle, buffer, bytes, &transferred, &operation);
  if (started == FALSE) {
    const auto error = GetLastError();
    if (error != ERROR_IO_PENDING) {
      CloseHandle(event);
      return false;
    }
    const auto wait = WaitForSingleObject(event, INFINITE);
    if (wait != WAIT_OBJECT_0 ||
        GetOverlappedResult(handle, &operation, &transferred, FALSE) == FALSE) {
      CloseHandle(event);
      return false;
    }
  }
  CloseHandle(event);
  return true;
}

}  // namespace

FrameLengthResult decode_worker_frame_length(std::span<const std::byte> prefix) noexcept {
  if (prefix.size() != sizeof(std::uint32_t)) {
    return {.status = FrameDecodeStatus::malformed_length, .message_bytes = 0};
  }
  const auto message_bytes = read_u32(prefix);
  if (message_bytes > maximum_worker_message_bytes) {
    return {.status = FrameDecodeStatus::message_too_large, .message_bytes = 0};
  }
  return {.status = FrameDecodeStatus::success, .message_bytes = message_bytes};
}

std::vector<std::byte> encode_worker_frame(const v1::WorkerIpcEnvelope& envelope) {
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

FrameDecodeStatus decode_worker_frame(std::span<const std::byte> frame,
                                      v1::WorkerIpcEnvelope& envelope) noexcept {
  if (frame.size() < sizeof(std::uint32_t)) {
    return FrameDecodeStatus::malformed_length;
  }
  const auto length = decode_worker_frame_length(frame.first(sizeof(std::uint32_t)));
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

NamedPipeChannel::NamedPipeChannel(void* handle) noexcept : handle_(handle) {}

NamedPipeChannel::~NamedPipeChannel() { close(); }

NamedPipeChannel::NamedPipeChannel(NamedPipeChannel&& other) noexcept
    : handle_(std::exchange(other.handle_, nullptr)) {}

NamedPipeChannel& NamedPipeChannel::operator=(NamedPipeChannel&& other) noexcept {
  if (this != &other) {
    close();
    handle_ = std::exchange(other.handle_, nullptr);
  }
  return *this;
}

NamedPipeChannel NamedPipeChannel::connect(const std::wstring& pipe_name) {
  const auto handle = CreateFileW(pipe_name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                                  OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
  if (handle == INVALID_HANDLE_VALUE) {
    throw std::runtime_error("Worker IPC pipe connection failed.");
  }
  return NamedPipeChannel(handle);
}

bool NamedPipeChannel::valid() const noexcept {
  return handle_ != nullptr && static_cast<HANDLE>(handle_) != INVALID_HANDLE_VALUE;
}

FrameDecodeStatus NamedPipeChannel::read(v1::WorkerIpcEnvelope& envelope) noexcept {
  std::array<std::byte, sizeof(std::uint32_t)> prefix{};
  if (!read_exact(prefix)) {
    return GetLastError() == ERROR_BROKEN_PIPE ? FrameDecodeStatus::pipe_closed
                                               : FrameDecodeStatus::io_error;
  }
  const auto length = decode_worker_frame_length(prefix);
  if (length.status != FrameDecodeStatus::success) {
    return length.status;
  }
  std::vector<std::byte> frame(sizeof(std::uint32_t) + length.message_bytes);
  std::ranges::copy(prefix, frame.begin());
  if (length.message_bytes > 0 &&
      !read_exact(std::span<std::byte>{frame}.subspan(sizeof(std::uint32_t)))) {
    return GetLastError() == ERROR_BROKEN_PIPE ? FrameDecodeStatus::pipe_closed
                                               : FrameDecodeStatus::io_error;
  }
  return decode_worker_frame(frame, envelope);
}

bool NamedPipeChannel::write(const v1::WorkerIpcEnvelope& envelope) noexcept {
  try {
    const auto frame = encode_worker_frame(envelope);
    return write_exact(frame);
  } catch (...) {
    return false;
  }
}

bool NamedPipeChannel::read_exact(std::span<std::byte> output) noexcept {
  std::size_t offset = 0;
  while (offset < output.size()) {
    const auto remaining = output.size() - offset;
    const auto requested = static_cast<DWORD>(
        std::min<std::size_t>(remaining, std::numeric_limits<DWORD>::max()));
    DWORD transferred = 0;
    if (!overlapped_transfer(static_cast<HANDLE>(handle_), output.data() + offset, requested,
                             false, transferred) ||
        transferred == 0) {
      return false;
    }
    offset += transferred;
  }
  return true;
}

bool NamedPipeChannel::write_exact(std::span<const std::byte> input) noexcept {
  std::size_t offset = 0;
  while (offset < input.size()) {
    const auto remaining = input.size() - offset;
    const auto requested = static_cast<DWORD>(
        std::min<std::size_t>(remaining, std::numeric_limits<DWORD>::max()));
    DWORD transferred = 0;
    if (!overlapped_transfer(static_cast<HANDLE>(handle_),
                             const_cast<std::byte*>(input.data() + offset), requested, true,
                             transferred) ||
        transferred == 0) {
      return false;
    }
    offset += transferred;
  }
  return true;
}

void NamedPipeChannel::close() noexcept {
  if (valid()) {
    CloseHandle(static_cast<HANDLE>(handle_));
  }
  handle_ = nullptr;
}

}  // namespace beacon::worker
