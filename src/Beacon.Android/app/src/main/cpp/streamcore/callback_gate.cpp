#include "callback_gate.h"

#include <utility>

namespace beacon::android::streamcore {

CallbackGate::Lease::Lease(CallbackGate *owner) noexcept : owner_(owner) {}
CallbackGate::Lease::Lease(Lease &&other) noexcept
    : owner_(std::exchange(other.owner_, nullptr)) {}
CallbackGate::Lease &CallbackGate::Lease::operator=(Lease &&other) noexcept {
  if (this != &other) {
    if (owner_ != nullptr) owner_->leave();
    owner_ = std::exchange(other.owner_, nullptr);
  }
  return *this;
}
CallbackGate::Lease::~Lease() {
  if (owner_ != nullptr) owner_->leave();
}

std::optional<CallbackGate::Lease> CallbackGate::try_enter() {
  std::lock_guard lock(mutex_);
  if (closing_) return std::nullopt;
  ++active_;
  return Lease(this);
}

void CallbackGate::close_and_wait() {
  std::unique_lock lock(mutex_);
  closing_ = true;
  changed_.wait(lock, [this] { return active_ == 0; });
}

void CallbackGate::leave() {
  std::lock_guard lock(mutex_);
  --active_;
  if (active_ == 0) changed_.notify_all();
}

CallbackBarrier::Scope::Scope(std::shared_ptr<CallbackBarrier> owner) noexcept
    : owner_(std::move(owner)) {}
CallbackBarrier::Scope::Scope(Scope &&other) noexcept
    : owner_(std::move(other.owner_)) {}
CallbackBarrier::Scope &CallbackBarrier::Scope::operator=(Scope &&other) noexcept {
  if (this != &other) {
    if (owner_) owner_->leave();
    owner_ = std::move(other.owner_);
  }
  return *this;
}
CallbackBarrier::Scope::~Scope() {
  if (owner_) owner_->leave();
}

CallbackBarrier::Scope CallbackBarrier::enter() {
  {
    std::lock_guard lock(mutex_);
    ++active_;
  }
  return Scope(shared_from_this());
}

void CallbackBarrier::wait_until_inactive() {
  std::unique_lock lock(mutex_);
  changed_.wait(lock, [this] { return active_ == 0; });
}

void CallbackBarrier::leave() {
  std::lock_guard lock(mutex_);
  --active_;
  if (active_ == 0) changed_.notify_all();
}

}  // namespace beacon::android::streamcore
