#pragma once

#include <cstdint>

namespace beacon::stream {

inline constexpr std::uint32_t protocol_version = 1;
inline constexpr char alpn[] = "beacon-stream/1";

}  // namespace beacon::stream
