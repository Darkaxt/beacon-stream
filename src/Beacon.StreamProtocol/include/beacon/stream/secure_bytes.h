#pragma once

#include <cstddef>
#include <span>
#include <vector>

namespace beacon::stream {

using SecureClearObserver =
    void (*)(std::span<const std::byte> bytes, void *context) noexcept;

void secure_wipe_bytes(std::span<std::byte> bytes,
                       SecureClearObserver observer = nullptr,
                       void *context = nullptr) noexcept;

void secure_clear_bytes(std::vector<std::byte> &bytes,
                        SecureClearObserver observer = nullptr,
                        void *context = nullptr) noexcept;

} // namespace beacon::stream
