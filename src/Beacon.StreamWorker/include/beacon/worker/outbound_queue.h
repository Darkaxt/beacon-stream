#pragma once

#include "worker_ipc.pb.h"

#include <condition_variable>
#include <deque>
#include <mutex>
#include <optional>
#include <vector>

namespace beacon::worker {

using WorkerOutboundBatch = std::vector<v1::WorkerIpcEnvelope>;

class WorkerOutboundQueue final {
public:
  [[nodiscard]] bool enqueue(WorkerOutboundBatch batch);
  [[nodiscard]] std::optional<WorkerOutboundBatch> wait_pop();
  void close() noexcept;

private:
  std::mutex mutex_;
  std::condition_variable changed_;
  std::deque<WorkerOutboundBatch> batches_;
  bool closed_{};
};

} // namespace beacon::worker
