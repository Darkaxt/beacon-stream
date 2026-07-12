#include "beacon/stream/benchmark_datagram.h"

#include <algorithm>
#include <limits>

namespace beacon::stream {
namespace {

constexpr std::array magic{std::byte{'B'}, std::byte{'C'}, std::byte{'B'},
                           std::byte{'M'}};

template <typename Value>
void write_big_endian(std::span<std::byte> output, Value value) noexcept {
  for (std::size_t index = 0; index < output.size(); ++index) {
    const auto shift = static_cast<unsigned>((output.size() - index - 1U) * 8U);
    output[index] = static_cast<std::byte>((value >> shift) & 0xffU);
  }
}

template <typename Value>
Value read_big_endian(std::span<const std::byte> input) noexcept {
  Value value{};
  for (std::byte byte : input) {
    value = static_cast<Value>((value << 8U) |
                               std::to_integer<unsigned>(byte));
  }
  return value;
}

}  // namespace

bool serialize_benchmark_datagram_header(
    const BenchmarkDatagramHeader &header,
    std::span<std::byte, benchmark_datagram_header_bytes> output) noexcept {
  if (header.round_id == 0 || header.payload_bytes == 0) {
    return false;
  }

  std::copy(magic.begin(), magic.end(), output.begin());
  output[4] = static_cast<std::byte>(benchmark_datagram_version);
  std::copy(header.run_token.begin(), header.run_token.end(), output.begin() + 5);
  write_big_endian<std::uint32_t>(output.subspan<21, 4>(), header.round_id);
  write_big_endian<std::uint64_t>(output.subspan<25, 8>(), header.sequence);
  write_big_endian<std::uint64_t>(output.subspan<33, 8>(), header.sent_at_us);
  write_big_endian<std::uint32_t>(output.subspan<41, 4>(),
                                  header.payload_bytes);
  return true;
}

std::optional<ParsedBenchmarkDatagram>
parse_benchmark_datagram(std::span<const std::byte> bytes) noexcept {
  if (bytes.size() < benchmark_datagram_header_bytes ||
      !std::equal(magic.begin(), magic.end(), bytes.begin()) ||
      bytes[4] != static_cast<std::byte>(benchmark_datagram_version)) {
    return std::nullopt;
  }

  BenchmarkDatagramHeader header;
  std::copy_n(bytes.begin() + 5, header.run_token.size(),
              header.run_token.begin());
  header.round_id = read_big_endian<std::uint32_t>(bytes.subspan<21, 4>());
  header.sequence = read_big_endian<std::uint64_t>(bytes.subspan<25, 8>());
  header.sent_at_us = read_big_endian<std::uint64_t>(bytes.subspan<33, 8>());
  header.payload_bytes =
      read_big_endian<std::uint32_t>(bytes.subspan<41, 4>());
  if (header.round_id == 0 || header.payload_bytes == 0 ||
      bytes.size() != benchmark_datagram_header_bytes +
                          static_cast<std::size_t>(header.payload_bytes)) {
    return std::nullopt;
  }

  return ParsedBenchmarkDatagram{
      .header = header,
      .payload = bytes.subspan(benchmark_datagram_header_bytes)};
}

}  // namespace beacon::stream
