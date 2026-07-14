#pragma once

#include "beacon/worker/outbound_queue.h"
#include "beacon/worker/worker_host.h"
#include "worker_ipc.pb.h"

#include <memory>

namespace beacon::testing {

enum class HostedWorkerChannelReadStatus {
  success,
  end_of_stream,
  canceled,
  truncated_frame,
  message_too_large,
  invalid_protobuf,
  io_error,
};

class HostedBenchmarkWorkerChannel final {
public:
  HostedBenchmarkWorkerChannel(int input_descriptor, int output_descriptor);
  ~HostedBenchmarkWorkerChannel();

  HostedBenchmarkWorkerChannel(const HostedBenchmarkWorkerChannel &) = delete;
  HostedBenchmarkWorkerChannel &
  operator=(const HostedBenchmarkWorkerChannel &) = delete;

  [[nodiscard]] HostedWorkerChannelReadStatus
  read(worker::v1::WorkerIpcEnvelope &envelope) noexcept;
  [[nodiscard]] bool
  write(const worker::v1::WorkerIpcEnvelope &envelope) noexcept;
  void cancel_read() noexcept;

private:
  class Impl;
  std::unique_ptr<Impl> impl_;
};

enum class HostedWorkerControlResult {
  clean_shutdown,
  input_closed,
  read_failure,
  write_failure,
  outbound_failure,
};

[[nodiscard]] HostedWorkerControlResult run_hosted_worker_control(
    HostedBenchmarkWorkerChannel &channel, worker::WorkerHost &host,
    worker::WorkerOutboundQueue &outbound) noexcept;

} // namespace beacon::testing
