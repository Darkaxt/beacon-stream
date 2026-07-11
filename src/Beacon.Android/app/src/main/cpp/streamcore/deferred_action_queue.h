#pragma once

#include "callback_gate.h"

#include <condition_variable>
#include <functional>
#include <memory>
#include <mutex>
#include <queue>
#include <thread>

namespace beacon::android::streamcore {

class DeferredActionQueue {
 public:
  static DeferredActionQueue &instance();
  void enqueue(std::shared_ptr<CallbackBarrier> barrier,
               std::function<void()> action);

 private:
  struct Entry {
    std::shared_ptr<CallbackBarrier> barrier;
    std::function<void()> action;
  };

  DeferredActionQueue();
  ~DeferredActionQueue();
  void run();

  std::mutex mutex_;
  std::condition_variable changed_;
  std::queue<Entry> actions_;
  bool stopping_{};
  std::thread worker_;
};

}  // namespace beacon::android::streamcore
