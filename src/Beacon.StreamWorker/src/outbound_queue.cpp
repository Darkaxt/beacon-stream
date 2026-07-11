#include "beacon/worker/outbound_queue.h"

#include <utility>

namespace beacon::worker {

bool WorkerOutboundQueue::enqueue(WorkerOutboundBatch batch) {
  if (batch.empty()) {
    return true;
  }
  {
    std::lock_guard lock{mutex_};
    if (closed_) {
      return false;
    }
    batches_.push_back(std::move(batch));
  }
  changed_.notify_one();
  return true;
}

std::optional<WorkerOutboundBatch> WorkerOutboundQueue::wait_pop() {
  std::unique_lock lock{mutex_};
  changed_.wait(lock, [this] { return closed_ || !batches_.empty(); });
  if (batches_.empty()) {
    return std::nullopt;
  }
  auto batch = std::move(batches_.front());
  batches_.pop_front();
  return batch;
}

void WorkerOutboundQueue::close() noexcept {
  {
    std::lock_guard lock{mutex_};
    closed_ = true;
  }
  changed_.notify_all();
}

} // namespace beacon::worker
