#pragma once

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <span>
#include <vector>

namespace beacon::stream::testing {

enum class AccessUnitVectorError {
  none,
  io_failure,
  invalid_magic,
  invalid_count,
  truncated,
  empty_access_unit,
  trailing_bytes,
  resource_exhausted,
};

struct AccessUnitVectorResult {
  AccessUnitVectorError error{AccessUnitVectorError::none};
  std::vector<std::vector<std::uint8_t>> access_units;
};

[[nodiscard]] AccessUnitVectorResult
parse_access_unit_vector(std::span<const std::byte> bytes) noexcept;

[[nodiscard]] AccessUnitVectorResult
load_access_unit_vector(const std::filesystem::path &path) noexcept;

} // namespace beacon::stream::testing
