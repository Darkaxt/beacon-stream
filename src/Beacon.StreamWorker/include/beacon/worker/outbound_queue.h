#pragma once

#include "worker_ipc.pb.h"

#include <condition_variable>
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
};

class WorkerOutboundQueue final {
public:
  [[nodiscard]] bool enqueue(WorkerOutboundBatch batch);
  [[nodiscard]] bool enqueue_terminal(WorkerOutboundBatch batch);
  [[nodiscard]] std::optional<WorkerOutboundItem> wait_pop();
  void close() noexcept;
  [[nodiscard]] bool closed() const noexcept;

private:
  [[nodiscard]] bool enqueue(WorkerOutboundItem item);

  mutable std::mutex mutex_;
  std::condition_variable changed_;
  std::deque<WorkerOutboundItem> items_;
  bool closed_{};
  bool terminal_enqueued_{};
};

} // namespace beacon::worker
