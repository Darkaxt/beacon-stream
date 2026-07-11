#pragma once

#include <condition_variable>
#include <cstddef>
#include <memory>
#include <mutex>
#include <optional>

namespace beacon::android::streamcore {

class CallbackGate {
 public:
  class Lease {
   public:
    explicit Lease(CallbackGate *owner = nullptr) noexcept;
    Lease(Lease &&other) noexcept;
    Lease &operator=(Lease &&other) noexcept;
    Lease(const Lease &) = delete;
    Lease &operator=(const Lease &) = delete;
    ~Lease();

   private:
    CallbackGate *owner_{};
  };

  [[nodiscard]] std::optional<Lease> try_enter();
  void close_and_wait();

 private:
  friend class Lease;
  void leave();

  std::mutex mutex_;
  std::condition_variable changed_;
  std::size_t active_{};
  bool closing_{};
};

class CallbackBarrier : public std::enable_shared_from_this<CallbackBarrier> {
 public:
  class Scope {
   public:
    explicit Scope(std::shared_ptr<CallbackBarrier> owner = nullptr) noexcept;
    Scope(Scope &&other) noexcept;
    Scope &operator=(Scope &&other) noexcept;
    Scope(const Scope &) = delete;
    Scope &operator=(const Scope &) = delete;
    ~Scope();

   private:
    std::shared_ptr<CallbackBarrier> owner_;
  };

  [[nodiscard]] Scope enter();
  void wait_until_inactive();

 private:
  friend class Scope;
  void leave();

  std::mutex mutex_;
  std::condition_variable changed_;
  std::size_t active_{};
};

}  // namespace beacon::android::streamcore
