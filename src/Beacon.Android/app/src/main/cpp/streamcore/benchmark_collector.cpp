#include "benchmark_collector.h"

#include <algorithm>
#include <cmath>
#include <limits>

namespace beacon::android::streamcore {

namespace {

constexpr std::uint32_t maximum_benchmark_packets = 100'000;
constexpr std::uint32_t maximum_reliable_payload_bytes = 1024U * 1024U;
constexpr std::uint32_t maximum_datagram_payload_bytes =
    65'507U - static_cast<std::uint32_t>(
                  stream::benchmark_datagram_header_bytes);

}  // namespace

bool BenchmarkCollector::start(BenchmarkCollectorPlan plan) {
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
  reliable_received_.assign(plan.reliable_packet_count, false);
  datagrams_.assign(plan.datagram_packet_count, std::nullopt);
  arrival_order_.clear();
  first_reliable_arrival_us_.reset();
  reliable_bytes_ = 0;
  highest_arrival_sequence_ = 0;
  has_arrival_sequence_ = false;
  active_ = true;
  canceled_ = false;
  return true;
}

bool BenchmarkCollector::observe_reliable(std::uint64_t sequence,
                                           std::uint32_t payload_bytes,
                                           std::uint64_t arrived_at_us) {
  if (!active_ || canceled_ || sequence >= reliable_received_.size() ||
      payload_bytes != plan_.reliable_payload_bytes ||
      reliable_received_[sequence]) {
    return false;
  }

  reliable_received_[sequence] = true;
  reliable_bytes_ += payload_bytes;
  if (!first_reliable_arrival_us_.has_value()) {
    first_reliable_arrival_us_ = arrived_at_us;
  }
  return true;
}

bool BenchmarkCollector::observe_datagram(std::span<const std::byte> bytes,
                                           std::uint64_t arrived_at_us) {
  if (!active_ || canceled_) {
    return false;
  }
  auto parsed = stream::parse_benchmark_datagram(bytes);
  if (!parsed.has_value() || parsed->header.run_token != plan_.run_token ||
      parsed->header.round_id != 2 ||
      parsed->header.payload_bytes != plan_.datagram_payload_bytes ||
      parsed->header.sequence >= datagrams_.size() ||
      datagrams_[parsed->header.sequence].has_value()) {
    return false;
  }

  std::uint32_t reorder_distance = 0;
  if (has_arrival_sequence_ &&
      parsed->header.sequence < highest_arrival_sequence_) {
    const std::uint64_t distance =
        highest_arrival_sequence_ - parsed->header.sequence;
    reorder_distance = static_cast<std::uint32_t>(std::min<std::uint64_t>(
        distance, std::numeric_limits<std::uint32_t>::max()));
  }
  highest_arrival_sequence_ =
      std::max(highest_arrival_sequence_, parsed->header.sequence);
  has_arrival_sequence_ = true;
  datagrams_[parsed->header.sequence] = DatagramObservation{
      .sent_at_us = parsed->header.sent_at_us,
      .arrived_at_us = arrived_at_us,
      .reorder_distance = reorder_distance};
  arrival_order_.push_back(parsed->header.sequence);
  return true;
}

std::optional<BenchmarkCollectionResult> BenchmarkCollector::complete(
    std::uint64_t completed_at_us,
    std::span<const BenchmarkRttObservation> rtt_observations) {
  if (!active_ || canceled_ || !first_reliable_arrival_us_.has_value() ||
      completed_at_us <= *first_reliable_arrival_us_ ||
      !std::ranges::all_of(reliable_received_, [](bool received) {
        return received;
      })) {
    return std::nullopt;
  }

  const std::uint64_t elapsed_us = completed_at_us - *first_reliable_arrival_us_;
  const double throughput_mbps =
      static_cast<double>(reliable_bytes_) * 8.0 / elapsed_us;
  std::vector<std::uint64_t> rtt_by_sequence(plan_.datagram_packet_count, 0);
  std::vector<bool> has_rtt(plan_.datagram_packet_count, false);
  for (const BenchmarkRttObservation &observation : rtt_observations) {
    if (observation.sequence >= rtt_by_sequence.size() ||
        has_rtt[observation.sequence]) {
      return std::nullopt;
    }
    has_rtt[observation.sequence] = true;
    rtt_by_sequence[observation.sequence] = observation.rtt_us;
  }

  std::vector<std::uint64_t> jitter_by_sequence(plan_.datagram_packet_count, 0);
  for (std::size_t index = 1; index < arrival_order_.size(); ++index) {
    const auto previous = *datagrams_[arrival_order_[index - 1]];
    const auto current = *datagrams_[arrival_order_[index]];
    const std::int64_t arrival_delta = static_cast<std::int64_t>(
        current.arrived_at_us - previous.arrived_at_us);
    const std::int64_t send_delta = static_cast<std::int64_t>(
        current.sent_at_us) - static_cast<std::int64_t>(previous.sent_at_us);
    jitter_by_sequence[arrival_order_[index]] = static_cast<std::uint64_t>(
        std::abs(arrival_delta - send_delta));
  }

  BenchmarkCollectionResult result{
      .sustainable_throughput_mbps = throughput_mbps,
      .received_datagrams = 0,
      .samples = {}};
  result.samples.reserve(plan_.datagram_packet_count);
  for (std::uint64_t sequence = 0; sequence < plan_.datagram_packet_count;
       ++sequence) {
    const bool received = datagrams_[sequence].has_value();
    if (received) {
      ++result.received_datagrams;
    }
    result.samples.push_back({
        .sequence = sequence,
        .payload_bytes = plan_.datagram_payload_bytes,
        .rtt_us = received ? rtt_by_sequence[sequence] : 0,
        .jitter_us = received ? jitter_by_sequence[sequence] : 0,
        .reorder_distance =
            received ? datagrams_[sequence]->reorder_distance : 0,
        .received = received});
  }

  active_ = false;
  return result;
}

void BenchmarkCollector::cancel() noexcept {
  if (active_) {
    canceled_ = true;
    active_ = false;
  }
}

}  // namespace beacon::android::streamcore
