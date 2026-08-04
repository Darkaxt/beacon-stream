#include "access_unit_vector.h"

#include <array>
#include <fstream>
#include <limits>
#include <new>
#include <string_view>

namespace beacon::stream::testing {
namespace {

constexpr std::string_view access_unit_vector_magic{"BEACONAU1\n"};

std::uint32_t read_u32(std::span<const std::byte, 4> bytes) noexcept {
  return std::to_integer<std::uint32_t>(bytes[0]) |
         (std::to_integer<std::uint32_t>(bytes[1]) << 8U) |
         (std::to_integer<std::uint32_t>(bytes[2]) << 16U) |
         (std::to_integer<std::uint32_t>(bytes[3]) << 24U);
}

AccessUnitVectorResult failure(AccessUnitVectorError error) noexcept {
  return {.error = error, .access_units = {}};
}

} // namespace

AccessUnitVectorResult
parse_access_unit_vector(std::span<const std::byte> bytes) noexcept {
  constexpr std::size_t count_bytes = sizeof(std::uint32_t);
  if (bytes.size() < access_unit_vector_magic.size() + count_bytes) {
    return failure(AccessUnitVectorError::truncated);
  }
  for (std::size_t index = 0; index < access_unit_vector_magic.size();
       ++index) {
    if (bytes[index] !=
        static_cast<std::byte>(access_unit_vector_magic[index])) {
      return failure(AccessUnitVectorError::invalid_magic);
    }
  }

  std::size_t offset = access_unit_vector_magic.size();
  const auto count = read_u32(std::span<const std::byte, count_bytes>{
      bytes.data() + offset, count_bytes});
  offset += count_bytes;
  if (count == 0) {
    return failure(AccessUnitVectorError::invalid_count);
  }
  if (count > (bytes.size() - offset) / count_bytes) {
    return failure(AccessUnitVectorError::truncated);
  }

  try {
    AccessUnitVectorResult result;
    result.access_units.reserve(count);
    for (std::uint32_t index = 0; index < count; ++index) {
      if (bytes.size() - offset < count_bytes) {
        return failure(AccessUnitVectorError::truncated);
      }
      const auto length = read_u32(std::span<const std::byte, count_bytes>{
          bytes.data() + offset, count_bytes});
      offset += count_bytes;
      if (length == 0) {
        return failure(AccessUnitVectorError::empty_access_unit);
      }
      if (length > bytes.size() - offset) {
        return failure(AccessUnitVectorError::truncated);
      }
      std::vector<std::uint8_t> unit(length);
      for (std::size_t byte_index = 0; byte_index < length; ++byte_index) {
        unit[byte_index] =
            std::to_integer<std::uint8_t>(bytes[offset + byte_index]);
      }
      offset += length;
      result.access_units.push_back(std::move(unit));
    }
    if (offset != bytes.size()) {
      return failure(AccessUnitVectorError::trailing_bytes);
    }
    return result;
  } catch (const std::bad_alloc &) {
    return failure(AccessUnitVectorError::resource_exhausted);
  } catch (...) {
    return failure(AccessUnitVectorError::resource_exhausted);
  }
}

AccessUnitVectorResult
load_access_unit_vector(const std::filesystem::path &path) noexcept {
  try {
    std::ifstream input{path, std::ios::binary | std::ios::ate};
    if (!input) {
      return failure(AccessUnitVectorError::io_failure);
    }
    const auto length = input.tellg();
    if (length <= 0 || static_cast<std::uintmax_t>(length) >
                           std::numeric_limits<std::size_t>::max()) {
      return failure(AccessUnitVectorError::io_failure);
    }
    std::vector<std::byte> bytes(static_cast<std::size_t>(length));
    input.seekg(0);
    input.read(reinterpret_cast<char *>(bytes.data()),
               static_cast<std::streamsize>(bytes.size()));
    if (!input) {
      return failure(AccessUnitVectorError::io_failure);
    }
    return parse_access_unit_vector(bytes);
  } catch (const std::bad_alloc &) {
    return failure(AccessUnitVectorError::resource_exhausted);
  } catch (...) {
    return failure(AccessUnitVectorError::io_failure);
  }
}

} // namespace beacon::stream::testing
