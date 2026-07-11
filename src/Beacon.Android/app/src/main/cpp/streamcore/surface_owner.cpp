#include "surface_owner.h"

#include <utility>

namespace beacon::android::streamcore {

SurfaceOwner::SurfaceOwner(std::function<void(void *)> release)
    : release_(std::move(release)) {}
SurfaceOwner::~SurfaceOwner() { close(); }

void SurfaceOwner::replace(void *acquired_surface) {
  if (closed_) {
    if (acquired_surface != nullptr) release_(acquired_surface);
    return;
  }
  void *previous = surface_;
  surface_ = acquired_surface;
  if (previous != nullptr) release_(previous);
}

void SurfaceOwner::close() noexcept {
  if (closed_) return;
  closed_ = true;
  void *surface = std::exchange(surface_, nullptr);
  if (surface != nullptr) {
    try {
      release_(surface);
    } catch (...) {
    }
  }
}

void *SurfaceOwner::get() const noexcept { return surface_; }

}  // namespace beacon::android::streamcore
