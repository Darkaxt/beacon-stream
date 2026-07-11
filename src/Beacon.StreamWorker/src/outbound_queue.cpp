#include "beacon/worker/outbound_queue.h"

#include <limits>
#include <utility>

namespace beacon::worker {

namespace {

constexpr std::size_t worker_frame_prefix_bytes = 4;

std::optional<std::size_t>
serialized_batch_bytes(const WorkerOutboundBatch &batch) {
  std::size_t result = 0;
  for (const auto &envelope : batch) {
    const auto message_bytes = envelope.ByteSizeLong();
    if (message_bytes >
        std::numeric_limits<std::size_t>::max() - worker_frame_prefix_bytes) {
      return std::nullopt;
    }
    const auto frame_bytes = message_bytes + worker_frame_prefix_bytes;
    if (result > std::numeric_limits<std::size_t>::max() - frame_bytes) {
      return std::nullopt;
    }
    result += frame_bytes;
  }
  return result;
}

} // namespace

WorkerOutboundQueue::WorkerOutboundQueue(WorkerOutboundQueueLimits limits) noexcept
    : limits_(limits) {}

WorkerOutboundEnqueueResult
WorkerOutboundQueue::enqueue(WorkerOutboundBatch batch) noexcept {
  try {
    const auto bytes = serialized_batch_bytes(batch);
    if (!bytes) {
      return WorkerOutboundEnqueueResult::byte_capacity_exceeded;
    }
    if (batch.empty()) {
      return WorkerOutboundEnqueueResult::accepted;
    }
    return enqueue_item({.kind = WorkerOutboundBatchKind::regular,
                         .batch = std::move(batch),
                         .serialized_bytes = *bytes});
  } catch (...) {
    return WorkerOutboundEnqueueResult::serialization_failure;
  }
}

WorkerOutboundEnqueueResult
WorkerOutboundQueue::enqueue_terminal(WorkerOutboundBatch batch) noexcept {
  try {
    const auto bytes = serialized_batch_bytes(batch);
    if (!bytes) {
      return WorkerOutboundEnqueueResult::byte_capacity_exceeded;
    }
    return enqueue_item({.kind = WorkerOutboundBatchKind::terminal,
                         .batch = std::move(batch),
                         .serialized_bytes = *bytes});
  } catch (...) {
    return WorkerOutboundEnqueueResult::serialization_failure;
  }
}

WorkerOutboundEnqueueResult
WorkerOutboundQueue::enqueue_item(WorkerOutboundItem item) noexcept {
  try {
    std::lock_guard lock{mutex_};
    if (closed_) {
      return WorkerOutboundEnqueueResult::closed;
    }
    if (terminal_enqueued_) {
      return WorkerOutboundEnqueueResult::terminal_pending;
    }
    const bool terminal = item.kind == WorkerOutboundBatchKind::terminal;
    const auto item_limit =
        terminal ? limits_.maximum_items
                 : (limits_.maximum_items == 0 ? 0
                                               : limits_.maximum_items - 1);
    if (items_.size() >= item_limit) {
      return WorkerOutboundEnqueueResult::count_capacity_exceeded;
    }
    const auto byte_limit =
        terminal
            ? limits_.maximum_serialized_bytes
            : (limits_.maximum_serialized_bytes >=
                       limits_.terminal_reserved_bytes
                   ? limits_.maximum_serialized_bytes -
                         limits_.terminal_reserved_bytes
                   : 0);
    if (item.serialized_bytes > byte_limit ||
        queued_serialized_bytes_ > byte_limit - item.serialized_bytes) {
      return WorkerOutboundEnqueueResult::byte_capacity_exceeded;
    }
    items_.push_back(std::move(item));
    queued_serialized_bytes_ += items_.back().serialized_bytes;
    terminal_enqueued_ = terminal;
  } catch (...) {
    return WorkerOutboundEnqueueResult::allocation_failure;
  }
  changed_.notify_one();
  return WorkerOutboundEnqueueResult::accepted;
}

std::optional<WorkerOutboundItem> WorkerOutboundQueue::wait_pop() {
  std::unique_lock lock{mutex_};
  changed_.wait(lock, [this] { return closed_ || !items_.empty(); });
  if (items_.empty()) {
    return std::nullopt;
  }
  auto item = std::move(items_.front());
  items_.pop_front();
  queued_serialized_bytes_ -= item.serialized_bytes;
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
