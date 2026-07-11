#include "deferred_action_queue.h"

#include <utility>

namespace beacon::android::streamcore {

DeferredActionQueue &DeferredActionQueue::instance() {
  static DeferredActionQueue queue;
  return queue;
}

DeferredActionQueue::DeferredActionQueue() : worker_([this] { run(); }) {}

DeferredActionQueue::~DeferredActionQueue() {
  {
    std::lock_guard lock(mutex_);
    stopping_ = true;
    changed_.notify_all();
  }
  worker_.join();
}

void DeferredActionQueue::enqueue(std::shared_ptr<CallbackBarrier> barrier,
                                  std::function<void()> action) {
  std::lock_guard lock(mutex_);
  actions_.push({std::move(barrier), std::move(action)});
  changed_.notify_one();
}

void DeferredActionQueue::run() {
  while (true) {
    Entry entry;
    {
      std::unique_lock lock(mutex_);
      changed_.wait(lock, [this] { return stopping_ || !actions_.empty(); });
      if (stopping_ && actions_.empty()) return;
      entry = std::move(actions_.front());
      actions_.pop();
    }
    entry.barrier->wait_until_inactive();
    entry.action();
  }
}

}  // namespace beacon::android::streamcore
