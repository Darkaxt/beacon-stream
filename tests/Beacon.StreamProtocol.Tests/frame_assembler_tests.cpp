#include "beacon/stream/frame_assembler.h"

#include "beacon/stream/media_datagram.h"

#include <algorithm>
#include <array>
#include "test_failure.h"
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace {

using beacon::stream::FrameAssembler;
using beacon::stream::FrameAssemblerEvent;
using beacon::stream::FrameAssemblerEventKind;
using beacon::stream::FramePushStatus;
using beacon::stream::MediaDatagramFlags;
using beacon::stream::MediaDatagramHeader;
using beacon::stream::MediaKind;

std::vector<std::byte> datagram(std::uint64_t sequence,
                                std::uint32_t frame_bytes,
                                std::uint16_t chunk_index,
                                std::uint16_t chunk_count,
                                std::uint32_t payload_offset,
                                std::span<const std::byte> payload,
                                MediaDatagramFlags flags = MediaDatagramFlags::none) {
  MediaDatagramHeader header{
      .version = beacon::stream::media_datagram_version,
      .media_kind = MediaKind::video,
      .flags = flags,
      .sequence = sequence,
      .presentation_time_us = sequence * 1000,
      .frame_bytes = frame_bytes,
      .chunk_index = chunk_index,
      .chunk_count = chunk_count,
      .payload_offset = payload_offset,
      .payload_bytes = static_cast<std::uint16_t>(payload.size()),
  };
  std::vector<std::byte> result(beacon::stream::media_datagram_header_bytes + payload.size());
  const auto header_output =
      std::span<std::byte, beacon::stream::media_datagram_header_bytes>{result.data(), 40};
  BEACON_TEST_REQUIRE(beacon::stream::serialize_media_datagram_header(header, header_output));
  std::ranges::copy(payload, result.begin() + 40);
  return result;
}

void reordered_chunks_complete_one_access_unit() {
  constexpr std::array second{std::byte{'d'}, std::byte{'e'}, std::byte{'f'}};
  constexpr std::array first{std::byte{'a'}, std::byte{'b'}, std::byte{'c'}};
  FrameAssembler assembler(1024);

  BEACON_TEST_REQUIRE(assembler.push(datagram(1, 6, 1, 2, 3, second)).status ==
         FramePushStatus::accepted_incomplete);
  const auto completed = assembler.push(datagram(
      1, 6, 0, 2, 0, first, MediaDatagramFlags::end_of_access_unit));

  BEACON_TEST_REQUIRE(completed.status == FramePushStatus::completed);
  BEACON_TEST_REQUIRE(completed.frame.has_value());
  BEACON_TEST_REQUIRE(completed.frame->bytes ==
         std::vector<std::byte>({std::byte{'a'}, std::byte{'b'}, std::byte{'c'},
                                 std::byte{'d'}, std::byte{'e'}, std::byte{'f'}}));
  BEACON_TEST_REQUIRE(assembler.push(datagram(1, 6, 1, 2, 3, second)).status ==
                      FramePushStatus::rejected_duplicate);
}

void duplicate_overlap_and_inconsistent_chunks_are_rejected() {
  constexpr std::array four{std::byte{1}, std::byte{2}, std::byte{3}, std::byte{4}};
  constexpr std::array three{std::byte{5}, std::byte{6}, std::byte{7}};
  FrameAssembler assembler(1024);

  const auto first = datagram(2, 7, 0, 2, 0, four);
  BEACON_TEST_REQUIRE(assembler.push(first).status == FramePushStatus::accepted_incomplete);
  BEACON_TEST_REQUIRE(assembler.push(first).status == FramePushStatus::rejected_duplicate);
  BEACON_TEST_REQUIRE(assembler.push(datagram(2, 7, 1, 2, 3, three)).status ==
         FramePushStatus::rejected_overlap);
  BEACON_TEST_REQUIRE(assembler.push(datagram(2, 7, 1, 3, 4, three)).status ==
         FramePushStatus::rejected_inconsistent);
}

void malformed_offsets_and_planned_size_are_rejected_before_allocation() {
  constexpr std::array payload{std::byte{1}, std::byte{2}, std::byte{3}};
  FrameAssembler assembler(32);

  auto malformed = datagram(3, 8, 0, 1, 0, payload);
  malformed[34] = std::byte{0};
  malformed[35] = std::byte{7};
  BEACON_TEST_REQUIRE(assembler.push(malformed).status == FramePushStatus::rejected_malformed);

  BEACON_TEST_REQUIRE(assembler.push(datagram(4, 33, 0, 2, 0, payload)).status ==
         FramePushStatus::rejected_frame_size);
  BEACON_TEST_REQUIRE(assembler.incomplete_frame_count() == 0);
}

void capacity_eviction_requests_idr_and_complete_idr_recovers() {
  constexpr std::array payload{std::byte{1}};
  FrameAssembler assembler(1024);

  for (std::uint64_t sequence = 10; sequence < 14; ++sequence) {
    BEACON_TEST_REQUIRE(assembler.push(datagram(sequence, 2, 0, 2, 0, payload)).status ==
           FramePushStatus::accepted_incomplete);
  }
  BEACON_TEST_REQUIRE(assembler.push(datagram(14, 2, 0, 2, 0, payload)).status ==
         FramePushStatus::suppressed_awaiting_idr);

  BEACON_TEST_REQUIRE(assembler.incomplete_frame_count() == 0);
  BEACON_TEST_REQUIRE(assembler.awaiting_idr());
  const auto events = assembler.take_events();
  BEACON_TEST_REQUIRE(std::ranges::any_of(events, [](const FrameAssemblerEvent& event) {
    return event.kind == FrameAssemblerEventKind::frame_evicted && event.sequence == 10;
  }));
  BEACON_TEST_REQUIRE(std::ranges::count(events, FrameAssemblerEventKind::idr_requested,
                            &FrameAssemblerEvent::kind) == 1);

  BEACON_TEST_REQUIRE(assembler.push(datagram(20, 1, 0, 1, 0, payload)).status ==
         FramePushStatus::suppressed_awaiting_idr);
  const auto recovered = assembler.push(datagram(
      21, 1, 0, 1, 0, payload,
      MediaDatagramFlags::idr | MediaDatagramFlags::end_of_access_unit));
  BEACON_TEST_REQUIRE(recovered.status == FramePushStatus::completed);
  BEACON_TEST_REQUIRE(!assembler.awaiting_idr());
  BEACON_TEST_REQUIRE(std::ranges::any_of(assembler.take_events(), [](const FrameAssemblerEvent& event) {
    return event.kind == FrameAssemblerEventKind::idr_recovered && event.sequence == 21;
  }));
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    reordered_chunks_complete_one_access_unit();
    duplicate_overlap_and_inconsistent_chunks_are_rejected();
    malformed_offsets_and_planned_size_are_rejected_before_allocation();
    capacity_eviction_requests_idr_and_complete_idr_recovers();
  });
}
