#include "access_unit_vector.h"

#include "test_failure.h"

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <string_view>
#include <vector>

namespace {

using beacon::stream::testing::AccessUnitVectorError;
using beacon::stream::testing::load_access_unit_vector;
using beacon::stream::testing::parse_access_unit_vector;

void append_u32(std::vector<std::byte> &bytes, std::uint32_t value) {
  bytes.push_back(static_cast<std::byte>(value & 0xffU));
  bytes.push_back(static_cast<std::byte>((value >> 8U) & 0xffU));
  bytes.push_back(static_cast<std::byte>((value >> 16U) & 0xffU));
  bytes.push_back(static_cast<std::byte>((value >> 24U) & 0xffU));
}

std::vector<std::byte>
container(const std::vector<std::vector<std::uint8_t>> &units) {
  constexpr std::string_view magic{"BEACONAU1\n"};
  std::vector<std::byte> bytes;
  for (const char value : magic) {
    bytes.push_back(static_cast<std::byte>(value));
  }
  append_u32(bytes, static_cast<std::uint32_t>(units.size()));
  for (const auto &unit : units) {
    append_u32(bytes, static_cast<std::uint32_t>(unit.size()));
    for (const auto value : unit) {
      bytes.push_back(static_cast<std::byte>(value));
    }
  }
  return bytes;
}

void complete_units_parse_with_little_endian_lengths() {
  const auto bytes =
      container({{0x00, 0x00, 0x00, 0x01, 0x67}, {0x01, 0x02, 0x03}});

  const auto parsed = parse_access_unit_vector(bytes);

  BEACON_TEST_REQUIRE(parsed.error == AccessUnitVectorError::none);
  BEACON_TEST_REQUIRE(parsed.access_units.size() == 2);
  BEACON_TEST_REQUIRE(parsed.access_units[0].size() == 5);
  BEACON_TEST_REQUIRE(parsed.access_units[0][4] == 0x67);
  BEACON_TEST_REQUIRE(parsed.access_units[1] ==
                      std::vector<std::uint8_t>({0x01, 0x02, 0x03}));
}

void malformed_containers_fail_with_typed_errors() {
  auto wrong_magic = container({{0x01}});
  wrong_magic[0] = std::byte{'X'};
  BEACON_TEST_REQUIRE(parse_access_unit_vector(wrong_magic).error ==
                      AccessUnitVectorError::invalid_magic);

  auto zero_count = container({{0x01}});
  zero_count.resize(10);
  append_u32(zero_count, 0);
  BEACON_TEST_REQUIRE(parse_access_unit_vector(zero_count).error ==
                      AccessUnitVectorError::invalid_count);

  auto missing_length = container({{0x01}});
  missing_length.resize(14);
  BEACON_TEST_REQUIRE(parse_access_unit_vector(missing_length).error ==
                      AccessUnitVectorError::truncated);

  auto truncated_unit = container({{0x01, 0x02}});
  truncated_unit.pop_back();
  BEACON_TEST_REQUIRE(parse_access_unit_vector(truncated_unit).error ==
                      AccessUnitVectorError::truncated);

  const auto empty_unit = container({{}});
  BEACON_TEST_REQUIRE(parse_access_unit_vector(empty_unit).error ==
                      AccessUnitVectorError::empty_access_unit);

  auto trailing = container({{0x01}});
  trailing.push_back(std::byte{0x7f});
  BEACON_TEST_REQUIRE(parse_access_unit_vector(trailing).error ==
                      AccessUnitVectorError::trailing_bytes);
}

void checked_in_640x360_vector_contains_thirty_complete_units() {
  const auto path = std::filesystem::path{BEACON_REPOSITORY_ROOT} /
                    "src/Beacon.Android/app/src/main/assets/benchmark-vectors/"
                    "beacon-h264-high-8-640x360-30-v1.bau";

  const auto loaded = load_access_unit_vector(path);

  BEACON_TEST_REQUIRE(loaded.error == AccessUnitVectorError::none);
  BEACON_TEST_REQUIRE(loaded.access_units.size() == 30);
  BEACON_TEST_REQUIRE(!loaded.access_units.front().empty());
  BEACON_TEST_REQUIRE(!loaded.access_units.back().empty());
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    complete_units_parse_with_little_endian_lengths();
    malformed_containers_fail_with_typed_errors();
    checked_in_640x360_vector_contains_thirty_complete_units();
  });
}
