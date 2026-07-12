#pragma once

#include "beacon/stream/transport.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <string_view>
#include <vector>

namespace beacon::worker {

inline constexpr std::string_view synthetic_access_unit_diagnostic_name{
    "gate3-non-decodable-access-unit-marker"};
inline constexpr std::array synthetic_access_unit_marker_bytes{
    std::byte{'B'}, std::byte{'E'}, std::byte{'A'}, std::byte{'C'},
    std::byte{'O'}, std::byte{'N'}, std::byte{'-'}, std::byte{'G'},
    std::byte{'3'}, std::byte{'-'}, std::byte{'M'}, std::byte{'A'},
    std::byte{'R'}, std::byte{'K'}, std::byte{'E'}, std::byte{'R'}};
static_assert(!(synthetic_access_unit_marker_bytes[0] == std::byte{0x00} &&
                synthetic_access_unit_marker_bytes[1] == std::byte{0x00} &&
                synthetic_access_unit_marker_bytes[2] == std::byte{0x01}),
              "Gate 3 marker bytes must not resemble an Annex-B H.264 NAL.");

class SyntheticMediaSource final {
public:
  [[nodiscard]] std::vector<stream::TransportPacket>
  emit_access_unit_marker(std::uint64_t sequence,
                          std::uint64_t presentation_time_us,
                          std::uint16_t maximum_datagram_bytes) const;
};

} // namespace beacon::worker
