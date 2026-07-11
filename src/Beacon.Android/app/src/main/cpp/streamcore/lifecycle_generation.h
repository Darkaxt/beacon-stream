#pragma once

#include <cstdint>

namespace beacon::android::streamcore {

class LifecycleGeneration {
 public:
  [[nodiscard]] bool can_activate(std::uint64_t generation) const noexcept {
    return generation != 0 && generation > current_;
  }

  bool activate(std::uint64_t generation) noexcept {
    if (!can_activate(generation)) return false;
    current_ = generation;
    return true;
  }

  [[nodiscard]] bool is_current(std::uint64_t generation) const noexcept {
    return generation != 0 && generation == current_;
  }

  [[nodiscard]] std::uint64_t current() const noexcept { return current_; }

 private:
  std::uint64_t current_{};
};

}  // namespace beacon::android::streamcore
