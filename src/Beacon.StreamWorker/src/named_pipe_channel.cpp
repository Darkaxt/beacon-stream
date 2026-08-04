#include "beacon/worker/named_pipe_channel.h"

#include <Windows.h>

#include <algorithm>
#include <array>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <utility>

namespace beacon::worker {
namespace {

bool overlapped_transfer(HANDLE handle,
                         void* buffer,
                         DWORD bytes,
                         bool write,
                         const std::atomic_bool& canceled,
                         DWORD& transferred,
                         DWORD& failure_error) noexcept {
  failure_error = ERROR_SUCCESS;
  if (canceled.load(std::memory_order_acquire)) {
    failure_error = ERROR_OPERATION_ABORTED;
    return false;
  }
  const auto event = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  if (event == nullptr) {
    failure_error = GetLastError();
    return false;
  }
  OVERLAPPED operation{};
  operation.hEvent = event;
  const auto started = write ? WriteFile(handle, buffer, bytes, &transferred, &operation)
                             : ReadFile(handle, buffer, bytes, &transferred, &operation);
  if (started == FALSE) {
    const auto error = GetLastError();
    if (error != ERROR_IO_PENDING) {
      failure_error = error;
      CloseHandle(event);
      return false;
    }
    if (canceled.load(std::memory_order_acquire)) {
      CancelIoEx(handle, &operation);
    }
    const auto wait = WaitForSingleObject(event, INFINITE);
    if (wait != WAIT_OBJECT_0) {
      const auto wait_error = GetLastError();
      CancelIoEx(handle, &operation);
      DWORD ignored = 0;
      static_cast<void>(
          GetOverlappedResult(handle, &operation, &ignored, TRUE));
      failure_error = wait_error;
      CloseHandle(event);
      return false;
    }
    if (GetOverlappedResult(handle, &operation, &transferred, FALSE) == FALSE) {
      failure_error = GetLastError();
      CloseHandle(event);
      return false;
    }
  }
  CloseHandle(event);
  return true;
}

}  // namespace

struct NamedPipeChannel::State {
  State(void *value, NamedPipeChannelOperationHooks operation_hooks) noexcept
      : handle(static_cast<HANDLE>(value)), hooks(operation_hooks) {}

  ~State() {
    if (handle != nullptr && handle != INVALID_HANDLE_VALUE) {
      CloseHandle(handle);
    }
  }

  void cancel() noexcept {
    canceled.store(true, std::memory_order_release);
    if (handle != nullptr && handle != INVALID_HANDLE_VALUE) {
      CancelIoEx(handle, nullptr);
    }
  }

  HANDLE handle{};
  std::atomic_bool canceled{};
  NamedPipeChannelOperationHooks hooks;
  std::mutex read_mutex;
  std::mutex write_mutex;
};

NamedPipeChannel::NamedPipeChannel() noexcept : state_(nullptr) {}

NamedPipeChannel::NamedPipeChannel(void *handle,
                                   NamedPipeChannelOperationHooks hooks)
    : state_(std::make_shared<State>(handle, hooks)) {}

NamedPipeChannel::~NamedPipeChannel() { release_owner(); }

NamedPipeChannel::NamedPipeChannel(NamedPipeChannel&& other) noexcept
    : state_(other.state_.exchange(nullptr, std::memory_order_acq_rel)) {}

NamedPipeChannel& NamedPipeChannel::operator=(NamedPipeChannel&& other) noexcept {
  if (this != &other) {
    release_owner();
    state_.store(other.state_.exchange(nullptr, std::memory_order_acq_rel),
                 std::memory_order_release);
  }
  return *this;
}

NamedPipeChannel NamedPipeChannel::connect(const std::wstring& pipe_name) {
  const auto handle = CreateFileW(pipe_name.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                                  OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr);
  if (handle == INVALID_HANDLE_VALUE) {
    throw std::runtime_error("Worker IPC pipe connection failed.");
  }
  try {
    return NamedPipeChannel(handle);
  } catch (...) {
    CloseHandle(handle);
    throw;
  }
}

bool NamedPipeChannel::valid() const noexcept {
  const auto state = state_.load(std::memory_order_acquire);
  return state && state->handle != nullptr &&
         state->handle != INVALID_HANDLE_VALUE;
}

FrameDecodeStatus NamedPipeChannel::read(v1::WorkerIpcEnvelope& envelope) noexcept {
  const auto state = state_.load(std::memory_order_acquire);
  if (!state) {
    return FrameDecodeStatus::io_error;
  }
  if (state->hooks.after_state_acquired != nullptr) {
    state->hooks.after_state_acquired(state->hooks.context);
  }
  std::lock_guard operation_lock{state->read_mutex};
  std::array<std::byte, sizeof(std::uint32_t)> prefix{};
  const auto prefix_error = read_exact(state, prefix);
  if (prefix_error != ERROR_SUCCESS) {
    return prefix_error == ERROR_BROKEN_PIPE ? FrameDecodeStatus::pipe_closed
                                             : FrameDecodeStatus::io_error;
  }
  const auto length = decode_worker_frame_length(prefix);
  if (length.status != FrameDecodeStatus::success) {
    return length.status;
  }
  std::vector<std::byte> frame(sizeof(std::uint32_t) + length.message_bytes);
  std::ranges::copy(prefix, frame.begin());
  const auto body_error =
      length.message_bytes == 0
          ? static_cast<std::uint32_t>(ERROR_SUCCESS)
          : read_exact(state,
                std::span<std::byte>{frame}.subspan(sizeof(std::uint32_t)));
  if (body_error != ERROR_SUCCESS) {
    return body_error == ERROR_BROKEN_PIPE ? FrameDecodeStatus::pipe_closed
                                           : FrameDecodeStatus::io_error;
  }
  return decode_worker_frame(frame, envelope);
}

bool NamedPipeChannel::write(const v1::WorkerIpcEnvelope& envelope) noexcept {
  const auto state = state_.load(std::memory_order_acquire);
  if (!state) {
    return false;
  }
  if (state->hooks.after_state_acquired != nullptr) {
    state->hooks.after_state_acquired(state->hooks.context);
  }
  std::lock_guard operation_lock{state->write_mutex};
  try {
    const auto frame = encode_worker_frame(envelope);
    return write_exact(state, frame) == ERROR_SUCCESS;
  } catch (...) {
    return false;
  }
}

std::uint32_t
NamedPipeChannel::read_exact(const std::shared_ptr<State> &state,
                             std::span<std::byte> output) noexcept {
  std::size_t offset = 0;
  while (offset < output.size()) {
    const auto remaining = output.size() - offset;
    const auto requested = static_cast<DWORD>(
        std::min<std::size_t>(remaining, std::numeric_limits<DWORD>::max()));
    DWORD transferred = 0;
    DWORD failure_error = ERROR_SUCCESS;
    if (!overlapped_transfer(state->handle,
                             output.data() + offset, requested, false,
                             state->canceled, transferred, failure_error) ||
        transferred == 0) {
      return failure_error == ERROR_SUCCESS ? ERROR_BROKEN_PIPE
                                            : failure_error;
    }
    offset += transferred;
  }
  return ERROR_SUCCESS;
}

std::uint32_t
NamedPipeChannel::write_exact(const std::shared_ptr<State> &state,
                              std::span<const std::byte> input) noexcept {
  std::size_t offset = 0;
  while (offset < input.size()) {
    const auto remaining = input.size() - offset;
    const auto requested = static_cast<DWORD>(
        std::min<std::size_t>(remaining, std::numeric_limits<DWORD>::max()));
    DWORD transferred = 0;
    DWORD failure_error = ERROR_SUCCESS;
    if (!overlapped_transfer(state->handle,
                             const_cast<std::byte*>(input.data() + offset), requested, true,
                             state->canceled, transferred, failure_error) ||
        transferred == 0) {
      return failure_error == ERROR_SUCCESS ? ERROR_BROKEN_PIPE
                                            : failure_error;
    }
    offset += transferred;
  }
  return ERROR_SUCCESS;
}

void NamedPipeChannel::cancel_pending_io() noexcept {
  const auto state = state_.load(std::memory_order_acquire);
  if (state) {
    state->cancel();
  }
}

void NamedPipeChannel::release_owner() noexcept {
  auto state = state_.exchange(nullptr, std::memory_order_acq_rel);
  if (state) {
    state->cancel();
  }
}

}  // namespace beacon::worker
