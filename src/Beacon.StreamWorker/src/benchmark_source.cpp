#include "beacon/worker/benchmark_source.h"

#include <algorithm>

namespace beacon::worker {
namespace {

constexpr std::uint32_t maximum_benchmark_packets = 100'000;
constexpr std::uint32_t maximum_reliable_payload_bytes = 1024U * 1024U;
constexpr std::uint32_t maximum_datagram_payload_bytes =
    65'507U - static_cast<std::uint32_t>(
                  stream::benchmark_datagram_header_bytes);

void fill_payload(std::span<std::byte> payload, std::uint64_t sequence) {
  for (std::size_t index = 0; index < payload.size(); ++index) {
    payload[index] = static_cast<std::byte>((sequence + index) & 0xffU);
  }
}

}  // namespace

bool BenchmarkSource::start(BenchmarkSourcePlan plan) {
  if (active_ || plan.reliable_packet_count == 0 ||
      plan.reliable_payload_bytes == 0 || plan.datagram_packet_count == 0 ||
      plan.datagram_payload_bytes == 0 ||
      plan.reliable_packet_count > maximum_benchmark_packets ||
      plan.datagram_packet_count > maximum_benchmark_packets ||
      plan.reliable_payload_bytes > maximum_reliable_payload_bytes ||
      plan.datagram_payload_bytes > maximum_datagram_payload_bytes) {
    return false;
  }

  plan_ = plan;
  next_reliable_sequence_ = 0;
  next_datagram_sequence_ = 0;
  active_ = true;
  canceled_ = false;
  return true;
}

std::optional<BenchmarkReliablePacket>
BenchmarkSource::next_reliable(std::uint64_t sent_at_us) {
  if (!active_ || canceled_ ||
      next_reliable_sequence_ >= plan_.reliable_packet_count) {
    return std::nullopt;
  }

  BenchmarkReliablePacket packet{
      .sequence = next_reliable_sequence_++,
      .sent_at_us = sent_at_us,
      .payload = std::vector<std::byte>(plan_.reliable_payload_bytes)};
  fill_payload(packet.payload, packet.sequence);
  return packet;
}

std::optional<BenchmarkDatagramPacket>
BenchmarkSource::next_datagram(std::uint64_t sent_at_us) {
  if (!active_ || canceled_ ||
      next_datagram_sequence_ >= plan_.datagram_packet_count) {
    return std::nullopt;
  }

  const std::uint64_t sequence = next_datagram_sequence_++;
  std::vector<std::byte> bytes(
      stream::benchmark_datagram_header_bytes + plan_.datagram_payload_bytes);
  const stream::BenchmarkDatagramHeader header{
      .run_token = plan_.run_token,
      .round_id = 2,
      .sequence = sequence,
      .sent_at_us = sent_at_us,
      .payload_bytes = plan_.datagram_payload_bytes};
  if (!stream::serialize_benchmark_datagram_header(
          header,
          std::span<std::byte, stream::benchmark_datagram_header_bytes>{
              bytes.data(), stream::benchmark_datagram_header_bytes})) {
    return std::nullopt;
  }
  fill_payload(std::span{bytes}.subspan(stream::benchmark_datagram_header_bytes),
               sequence);
  return BenchmarkDatagramPacket{.sequence = sequence,
                                 .bytes = std::move(bytes)};
}

void BenchmarkSource::cancel() noexcept {
  if (active_) {
    canceled_ = true;
    active_ = false;
  }
}

bool BenchmarkSource::complete() const noexcept {
  return active_ && !canceled_ &&
         next_reliable_sequence_ == plan_.reliable_packet_count &&
         next_datagram_sequence_ == plan_.datagram_packet_count;
}

bool BenchmarkSource::canceled() const noexcept { return canceled_; }

}  // namespace beacon::worker
