#pragma once

#include "worker_ipc.pb.h"

#include <condition_variable>
#include <cstddef>
#include <deque>
#include <mutex>
#include <optional>
#include <vector>

namespace beacon::worker {

using WorkerOutboundBatch = std::vector<v1::WorkerIpcEnvelope>;

enum class WorkerOutboundBatchKind {
  regular,
  terminal,
};

struct WorkerOutboundItem {
  WorkerOutboundBatchKind kind{WorkerOutboundBatchKind::regular};
  WorkerOutboundBatch batch;
  std::size_t serialized_bytes{};
};

struct WorkerOutboundQueueLimits {
  std::size_t maximum_items{256};
  std::size_t maximum_serialized_bytes{8U * 1024U * 1024U};
  std::size_t terminal_reserved_bytes{1024U * 1024U};
};

enum class WorkerOutboundEnqueueResult {
  accepted,
  closed,
  terminal_pending,
  count_capacity_exceeded,
  byte_capacity_exceeded,
  serialization_failure,
  allocation_failure,
};

class WorkerOutboundQueue final {
public:
  explicit WorkerOutboundQueue(WorkerOutboundQueueLimits limits = {}) noexcept;

  [[nodiscard]] WorkerOutboundEnqueueResult
  enqueue(WorkerOutboundBatch batch) noexcept;
  [[nodiscard]] WorkerOutboundEnqueueResult
  enqueue_terminal(WorkerOutboundBatch batch) noexcept;
  [[nodiscard]] std::optional<WorkerOutboundItem> wait_pop();
  void close() noexcept;
  [[nodiscard]] bool closed() const noexcept;

private:
  [[nodiscard]] WorkerOutboundEnqueueResult
  enqueue_item(WorkerOutboundItem item) noexcept;

  WorkerOutboundQueueLimits limits_;
  mutable std::mutex mutex_;
  std::condition_variable changed_;
  std::deque<WorkerOutboundItem> items_;
  std::size_t queued_serialized_bytes_{};
  bool closed_{};
  bool terminal_enqueued_{};
};

} // namespace beacon::worker
