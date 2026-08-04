#include "beacon/stream/secure_bytes.h"

#if defined(_WIN32)
#include <Windows.h>
#else
#include <atomic>
#endif

namespace beacon::stream {

void secure_wipe_bytes(std::span<std::byte> bytes,
                       SecureClearObserver observer,
                       void *context) noexcept {
  if (!bytes.empty()) {
#if defined(_WIN32)
    SecureZeroMemory(bytes.data(), bytes.size());
#else
    auto *cursor = reinterpret_cast<volatile unsigned char *>(bytes.data());
    for (std::size_t index = 0; index < bytes.size(); ++index) {
      cursor[index] = 0;
    }
    std::atomic_signal_fence(std::memory_order_seq_cst);
#endif
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

} // namespace beacon::stream
