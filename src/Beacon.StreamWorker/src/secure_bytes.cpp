#include "beacon/worker/secure_bytes.h"

#include <Windows.h>

namespace beacon::worker {

void secure_clear_bytes(std::vector<std::byte> &bytes,
                        SecureClearObserver observer,
                        void *context) noexcept {
  if (!bytes.empty()) {
    SecureZeroMemory(bytes.data(), bytes.size());
  }
  if (observer != nullptr) {
    observer(bytes, context);
  }
  bytes.clear();
}

} // namespace beacon::worker
