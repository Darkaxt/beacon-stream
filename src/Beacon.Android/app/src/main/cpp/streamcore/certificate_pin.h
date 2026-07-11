#pragma once

#include <array>
#include <cstddef>
#include <span>
#include <string_view>

namespace beacon::android::streamcore {

[[nodiscard]] bool decode_sha256_pin(
    std::string_view text, std::array<std::byte, 32> &pin) noexcept;
[[nodiscard]] bool validate_der_spki_pin(
    std::span<const std::byte> certificate_der,
    const std::array<std::byte, 32> &expected_pin) noexcept;

}  // namespace beacon::android::streamcore
