#pragma once

#include <functional>

namespace beacon::android::streamcore {

class SurfaceOwner {
 public:
  explicit SurfaceOwner(std::function<void(void *)> release);
  ~SurfaceOwner();
  void replace(void *acquired_surface);
  void close() noexcept;
  [[nodiscard]] void *get() const noexcept;

 private:
  std::function<void(void *)> release_;
  void *surface_{};
  bool closed_{};
};

}  // namespace beacon::android::streamcore
