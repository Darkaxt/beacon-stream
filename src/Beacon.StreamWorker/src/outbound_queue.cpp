#include "beacon/worker/outbound_queue.h"

#include <utility>

namespace beacon::worker {

bool WorkerOutboundQueue::enqueue(WorkerOutboundBatch batch) {
  return enqueue({.kind = WorkerOutboundBatchKind::regular,
                  .batch = std::move(batch)});
}

bool WorkerOutboundQueue::enqueue_terminal(WorkerOutboundBatch batch) {
  return enqueue({.kind = WorkerOutboundBatchKind::terminal,
                  .batch = std::move(batch)});
}

bool WorkerOutboundQueue::enqueue(WorkerOutboundItem item) {
  if (item.batch.empty()) {
    return true;
  }
  {
    std::lock_guard lock{mutex_};
    if (closed_ || terminal_enqueued_) {
      return false;
    }
    terminal_enqueued_ =
        item.kind == WorkerOutboundBatchKind::terminal;
    items_.push_back(std::move(item));
  }
  changed_.notify_one();
  return true;
}

std::optional<WorkerOutboundItem> WorkerOutboundQueue::wait_pop() {
  std::unique_lock lock{mutex_};
  changed_.wait(lock, [this] { return closed_ || !items_.empty(); });
  if (items_.empty()) {
    return std::nullopt;
  }
  auto item = std::move(items_.front());
  items_.pop_front();
  return item;
}

bool WorkerOutboundQueue::closed() const noexcept {
  std::lock_guard lock{mutex_};
  return closed_;
}

void WorkerOutboundQueue::close() noexcept {
  {
    std::lock_guard lock{mutex_};
    closed_ = true;
  }
  changed_.notify_all();
}

} // namespace beacon::worker
