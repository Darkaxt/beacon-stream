#include "beacon/worker/secure_bytes.h"

#include <Windows.h>

namespace beacon::worker {

void secure_wipe_bytes(std::span<std::byte> bytes,
                       SecureClearObserver observer,
                       void *context) noexcept {
  if (!bytes.empty()) {
    SecureZeroMemory(bytes.data(), bytes.size());
  }
  if (observer != nullptr) {
    observer(bytes, context);
  }
}

void secure_clear_bytes(std::vector<std::byte> &bytes,
                        SecureClearObserver observer,
                        void *context) noexcept {
  secure_wipe_bytes(bytes, observer, context);
  bytes.clear();
}

} // namespace beacon::worker
