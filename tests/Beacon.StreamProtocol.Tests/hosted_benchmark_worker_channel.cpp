#include "hosted_benchmark_worker_channel.h"

#include "beacon/worker/worker_ipc_frame.h"

#include <fcntl.h>
#include <poll.h>
#include <unistd.h>

#include <array>
#include <atomic>
#include <cerrno>
#include <cstddef>
#include <algorithm>
#include <span>
#include <stdexcept>
#include <thread>
#include <utility>
#include <vector>

namespace beacon::testing {
namespace {

enum class ExactReadStatus {
  success,
  end_of_stream,
  canceled,
  truncated,
  io_error,
};

void set_close_on_exec(int descriptor) {
  const auto flags = ::fcntl(descriptor, F_GETFD);
  if (flags < 0 || ::fcntl(descriptor, F_SETFD, flags | FD_CLOEXEC) < 0) {
    throw std::runtime_error("Could not protect the hosted channel handle.");
  }
}

constexpr int kControlRunning{-1};

bool replace_running(std::atomic_int &result,
                     HostedWorkerControlResult replacement) noexcept {
  auto expected = kControlRunning;
  return result.compare_exchange_strong(
      expected, static_cast<int>(replacement), std::memory_order_acq_rel);
}

} // namespace

class HostedBenchmarkWorkerChannel::Impl {
public:
  Impl(int input_descriptor, int output_descriptor)
      : input_descriptor_(input_descriptor),
        output_descriptor_(output_descriptor) {
    if (input_descriptor_ < 0 || output_descriptor_ < 0) {
      throw std::invalid_argument(
          "Hosted Worker channel descriptors must be valid.");
    }
    std::array<int, 2> cancellation{};
    if (::pipe(cancellation.data()) != 0) {
      throw std::runtime_error("Could not create the hosted channel signal.");
    }
    cancel_read_descriptor_ = cancellation[0];
    cancel_write_descriptor_ = cancellation[1];
    try {
      set_close_on_exec(cancel_read_descriptor_);
      set_close_on_exec(cancel_write_descriptor_);
    } catch (...) {
      ::close(cancel_read_descriptor_);
      ::close(cancel_write_descriptor_);
      cancel_read_descriptor_ = -1;
      cancel_write_descriptor_ = -1;
      throw;
    }
  }

  ~Impl() {
    cancel_read();
    if (cancel_read_descriptor_ >= 0) {
      ::close(cancel_read_descriptor_);
    }
    if (cancel_write_descriptor_ >= 0) {
      ::close(cancel_write_descriptor_);
    }
  }

  HostedWorkerChannelReadStatus
  read(worker::v1::WorkerIpcEnvelope &envelope) noexcept {
    try {
      std::array<std::byte, 4> prefix{};
      const auto prefix_status = read_exact(prefix);
      if (prefix_status == ExactReadStatus::end_of_stream) {
        return HostedWorkerChannelReadStatus::end_of_stream;
      }
      if (prefix_status == ExactReadStatus::canceled) {
        return HostedWorkerChannelReadStatus::canceled;
      }
      if (prefix_status == ExactReadStatus::truncated) {
        return HostedWorkerChannelReadStatus::truncated_frame;
      }
      if (prefix_status != ExactReadStatus::success) {
        return HostedWorkerChannelReadStatus::io_error;
      }

      const auto length = worker::decode_worker_frame_length(prefix);
      if (length.status == worker::FrameDecodeStatus::message_too_large) {
        return HostedWorkerChannelReadStatus::message_too_large;
      }
      if (length.status != worker::FrameDecodeStatus::success) {
        return HostedWorkerChannelReadStatus::io_error;
      }
      std::vector<std::byte> frame(4U + length.message_bytes);
      std::ranges::copy(prefix, frame.begin());
      if (length.message_bytes != 0) {
        const auto body_status =
            read_exact(std::span<std::byte>{frame}.subspan(4));
        if (body_status == ExactReadStatus::canceled) {
          return HostedWorkerChannelReadStatus::canceled;
        }
        if (body_status == ExactReadStatus::end_of_stream ||
            body_status == ExactReadStatus::truncated) {
          return HostedWorkerChannelReadStatus::truncated_frame;
        }
        if (body_status != ExactReadStatus::success) {
          return HostedWorkerChannelReadStatus::io_error;
        }
      }
      const auto decoded = worker::decode_worker_frame(frame, envelope);
      if (decoded == worker::FrameDecodeStatus::invalid_protobuf) {
        return HostedWorkerChannelReadStatus::invalid_protobuf;
      }
      return decoded == worker::FrameDecodeStatus::success
                 ? HostedWorkerChannelReadStatus::success
                 : HostedWorkerChannelReadStatus::io_error;
    } catch (...) {
      return HostedWorkerChannelReadStatus::io_error;
    }
  }

  bool write(const worker::v1::WorkerIpcEnvelope &envelope) noexcept {
    try {
      const auto frame = worker::encode_worker_frame(envelope);
      std::size_t offset = 0;
      while (offset < frame.size()) {
        const auto written = ::write(output_descriptor_, frame.data() + offset,
                                     frame.size() - offset);
        if (written < 0 && errno == EINTR) {
          continue;
        }
        if (written <= 0) {
          return false;
        }
        offset += static_cast<std::size_t>(written);
      }
      return true;
    } catch (...) {
      return false;
    }
  }

  void cancel_read() noexcept {
    bool expected = false;
    if (!read_canceled_.compare_exchange_strong(expected, true,
                                                std::memory_order_acq_rel)) {
      return;
    }
    const std::byte signal{1};
    for (;;) {
      const auto written = ::write(cancel_write_descriptor_, &signal, 1);
      if (written >= 0 || errno != EINTR) {
        return;
      }
    }
  }

private:
  ExactReadStatus read_exact(std::span<std::byte> output) noexcept {
    std::size_t offset = 0;
    while (offset < output.size()) {
      std::array<pollfd, 2> descriptors{{
          {.fd = input_descriptor_, .events = POLLIN, .revents = 0},
          {.fd = cancel_read_descriptor_, .events = POLLIN, .revents = 0},
      }};
      const auto polled = ::poll(descriptors.data(), descriptors.size(), -1);
      if (polled < 0 && errno == EINTR) {
        continue;
      }
      if (polled < 0) {
        return ExactReadStatus::io_error;
      }
      if ((descriptors[1].revents &
           (POLLIN | POLLHUP | POLLERR | POLLNVAL)) != 0) {
        return ExactReadStatus::canceled;
      }
      if ((descriptors[0].revents & POLLNVAL) != 0) {
        return ExactReadStatus::io_error;
      }
      if ((descriptors[0].revents & (POLLIN | POLLHUP | POLLERR)) == 0) {
        continue;
      }
      const auto received = ::read(input_descriptor_, output.data() + offset,
                                   output.size() - offset);
      if (received < 0 && errno == EINTR) {
        continue;
      }
      if (received < 0) {
        return ExactReadStatus::io_error;
      }
      if (received == 0) {
        return offset == 0 ? ExactReadStatus::end_of_stream
                           : ExactReadStatus::truncated;
      }
      offset += static_cast<std::size_t>(received);
    }
    return ExactReadStatus::success;
  }

  int input_descriptor_{-1};
  int output_descriptor_{-1};
  int cancel_read_descriptor_{-1};
  int cancel_write_descriptor_{-1};
  std::atomic_bool read_canceled_{};
};

HostedBenchmarkWorkerChannel::HostedBenchmarkWorkerChannel(
    int input_descriptor, int output_descriptor)
    : impl_(std::make_unique<Impl>(input_descriptor, output_descriptor)) {}

HostedBenchmarkWorkerChannel::~HostedBenchmarkWorkerChannel() = default;

HostedWorkerChannelReadStatus HostedBenchmarkWorkerChannel::read(
    worker::v1::WorkerIpcEnvelope &envelope) noexcept {
  return impl_->read(envelope);
}

bool HostedBenchmarkWorkerChannel::write(
    const worker::v1::WorkerIpcEnvelope &envelope) noexcept {
  return impl_->write(envelope);
}

void HostedBenchmarkWorkerChannel::cancel_read() noexcept {
  impl_->cancel_read();
}

HostedWorkerControlResult run_hosted_worker_control(
    HostedBenchmarkWorkerChannel &channel, worker::WorkerHost &host,
    worker::WorkerOutboundQueue &outbound) noexcept {
  try {
    if (outbound.enqueue({host.hello(), host.capabilities(), host.ready()}) !=
        worker::WorkerOutboundEnqueueResult::accepted) {
      return HostedWorkerControlResult::outbound_failure;
    }

    std::atomic_int result{kControlRunning};
    std::thread reader([&] {
      try {
        for (;;) {
          worker::v1::WorkerIpcEnvelope request;
          const auto status = channel.read(request);
          if (status == HostedWorkerChannelReadStatus::end_of_stream) {
            replace_running(result, HostedWorkerControlResult::input_closed);
            outbound.close();
            return;
          }
          if (status == HostedWorkerChannelReadStatus::canceled) {
            outbound.close();
            return;
          }
          if (status != HostedWorkerChannelReadStatus::success) {
            replace_running(result, HostedWorkerControlResult::read_failure);
            outbound.close();
            return;
          }

          auto responses = host.dispatch(request);
          const bool terminal = host.shutdown_requested();
          const auto enqueue_result =
              terminal ? outbound.enqueue_terminal(std::move(responses))
                       : outbound.enqueue(std::move(responses));
          if (enqueue_result != worker::WorkerOutboundEnqueueResult::accepted) {
            replace_running(result,
                            HostedWorkerControlResult::outbound_failure);
            outbound.close();
            channel.cancel_read();
            return;
          }
          if (terminal) {
            return;
          }
        }
      } catch (...) {
        replace_running(result, HostedWorkerControlResult::read_failure);
        outbound.close();
        channel.cancel_read();
      }
    });

    while (auto item = outbound.wait_pop()) {
      bool write_failed = false;
      for (const auto &envelope : item->batch) {
        if (!channel.write(envelope)) {
          write_failed = true;
          break;
        }
      }
      if (write_failed) {
        replace_running(result, HostedWorkerControlResult::write_failure);
        outbound.close();
        channel.cancel_read();
        break;
      }
      if (item->kind == worker::WorkerOutboundBatchKind::terminal) {
        replace_running(result, HostedWorkerControlResult::clean_shutdown);
        outbound.close();
      }
    }
    channel.cancel_read();
    reader.join();
    const auto final_result = result.load(std::memory_order_acquire);
    return final_result == kControlRunning
               ? HostedWorkerControlResult::outbound_failure
               : static_cast<HostedWorkerControlResult>(final_result);
  } catch (...) {
    channel.cancel_read();
    outbound.close();
    return HostedWorkerControlResult::outbound_failure;
  }
}

} // namespace beacon::testing
