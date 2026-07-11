#pragma once

#include <atomic>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <utility>

namespace beacon::android::streamcore {

template <typename Session>
class SessionRegistry {
 public:
  std::uint64_t add(std::shared_ptr<Session> session) {
    const std::uint64_t handle = next_.fetch_add(1, std::memory_order_relaxed);
    std::lock_guard lock(mutex_);
    entries_.emplace(handle, Entry{.session = std::move(session)});
    return handle;
  }

  std::shared_ptr<Session> find_active(std::uint64_t handle) {
    std::lock_guard lock(mutex_);
    const auto found = entries_.find(handle);
    if (found == entries_.end() || found->second.closing) return nullptr;
    return found->second.session;
  }

  std::shared_ptr<Session> retain(std::uint64_t handle) {
    std::lock_guard lock(mutex_);
    const auto found = entries_.find(handle);
    return found == entries_.end() ? nullptr : found->second.session;
  }

  std::shared_ptr<Session> begin_close(std::uint64_t handle) {
    std::lock_guard lock(mutex_);
    const auto found = entries_.find(handle);
    if (found == entries_.end() || found->second.closing) return nullptr;
    found->second.closing = true;
    return found->second.session;
  }

  void finish_close(std::uint64_t handle) {
    {
      std::lock_guard lock(mutex_);
      const auto found = entries_.find(handle);
      if (found != entries_.end() && found->second.closing) {
        entries_.erase(found);
      }
    }
    changed_.notify_all();
  }

  void wait_until_empty() {
    std::unique_lock lock(mutex_);
    changed_.wait(lock, [this] { return entries_.empty(); });
  }

  std::size_t size() {
    std::lock_guard lock(mutex_);
    return entries_.size();
  }

 private:
  struct Entry {
    std::shared_ptr<Session> session;
    bool closing{};
  };

  std::mutex mutex_;
  std::condition_variable changed_;
  std::atomic<std::uint64_t> next_{1};
  std::unordered_map<std::uint64_t, Entry> entries_;
};

}  // namespace beacon::android::streamcore
