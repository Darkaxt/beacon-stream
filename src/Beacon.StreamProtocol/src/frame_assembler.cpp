#include "beacon/stream/frame_assembler.h"

#include <algorithm>
#include <utility>

namespace beacon::stream {
namespace {

constexpr auto stable_flag_mask = static_cast<std::uint16_t>(MediaDatagramFlags::idr) |
                                  static_cast<std::uint16_t>(
                                      MediaDatagramFlags::codec_configuration);

MediaDatagramFlags stable_flags(MediaDatagramFlags flags) noexcept {
  return static_cast<MediaDatagramFlags>(static_cast<std::uint16_t>(flags) & stable_flag_mask);
}

bool has_flag(MediaDatagramFlags value, MediaDatagramFlags flag) noexcept {
  return (static_cast<std::uint16_t>(value) & static_cast<std::uint16_t>(flag)) != 0;
}

bool ranges_overlap(std::uint32_t left_offset,
                    std::uint16_t left_size,
                    std::uint32_t right_offset,
                    std::uint16_t right_size) noexcept {
  const auto left_end = left_offset + left_size;
  const auto right_end = right_offset + right_size;
  return left_offset < right_end && right_offset < left_end;
}

}  // namespace

FrameAssembler::FrameAssembler(std::uint32_t planned_maximum_frame_bytes)
    : planned_maximum_frame_bytes_(
          std::min(planned_maximum_frame_bytes, maximum_media_frame_bytes)) {}

FramePushResult FrameAssembler::push(std::span<const std::byte> datagram) {
  const auto parsed = parse_media_datagram(datagram);
  if (parsed.error != MediaDatagramError::none || parsed.header.media_kind != MediaKind::video) {
    return reject(parsed.header.sequence, FramePushStatus::rejected_malformed);
  }
  if (parsed.header.frame_bytes > planned_maximum_frame_bytes_) {
    return reject(parsed.header.sequence, FramePushStatus::rejected_frame_size);
  }
  if (highest_completed_sequence_.has_value() &&
      parsed.header.sequence <= *highest_completed_sequence_) {
    return reject(parsed.header.sequence, FramePushStatus::rejected_duplicate);
  }

  const auto packet_stable_flags = stable_flags(parsed.header.flags);
  const bool packet_is_idr = has_flag(packet_stable_flags, MediaDatagramFlags::idr);
  if (awaiting_idr_ && !packet_is_idr) {
    return suppress_non_idr(parsed.header.sequence);
  }

  auto frame = incomplete_frames_.find(parsed.header.sequence);
  if (frame == incomplete_frames_.end()) {
    if (incomplete_frames_.size() == maximum_incomplete_video_frames) {
      enter_recovery();
      if (!packet_is_idr) {
        return suppress_non_idr(parsed.header.sequence);
      }
    }

    IncompleteFrame incomplete{
        .presentation_time_us = parsed.header.presentation_time_us,
        .frame_bytes = parsed.header.frame_bytes,
        .chunk_count = parsed.header.chunk_count,
        .stable_flags = packet_stable_flags,
        .aggregate_flags = parsed.header.flags,
        .received_bytes = 0,
        .bytes = std::vector<std::byte>(parsed.header.frame_bytes),
        .chunks = {},
    };
    frame = incomplete_frames_.emplace(parsed.header.sequence, std::move(incomplete)).first;
    insertion_order_.push_back(parsed.header.sequence);
  } else if (frame->second.presentation_time_us != parsed.header.presentation_time_us ||
             frame->second.frame_bytes != parsed.header.frame_bytes ||
             frame->second.chunk_count != parsed.header.chunk_count ||
             frame->second.stable_flags != packet_stable_flags) {
    return reject(parsed.header.sequence, FramePushStatus::rejected_inconsistent);
  }

  for (const auto& chunk : frame->second.chunks) {
    if (chunk.index == parsed.header.chunk_index) {
      return reject(parsed.header.sequence, FramePushStatus::rejected_duplicate);
    }
    if (ranges_overlap(chunk.offset, chunk.size, parsed.header.payload_offset,
                       parsed.header.payload_bytes)) {
      return reject(parsed.header.sequence, FramePushStatus::rejected_overlap);
    }
  }

  std::ranges::copy(parsed.payload,
                    frame->second.bytes.begin() + parsed.header.payload_offset);
  frame->second.chunks.push_back({.offset = parsed.header.payload_offset,
                                  .size = parsed.header.payload_bytes,
                                  .index = parsed.header.chunk_index});
  frame->second.received_bytes += parsed.header.payload_bytes;
  frame->second.aggregate_flags = frame->second.aggregate_flags | parsed.header.flags;

  if (frame->second.chunks.size() != frame->second.chunk_count ||
      frame->second.received_bytes != frame->second.frame_bytes) {
    return {.status = FramePushStatus::accepted_incomplete, .frame = std::nullopt};
  }

  CompletedFrame completed{
      .sequence = parsed.header.sequence,
      .presentation_time_us = frame->second.presentation_time_us,
      .flags = frame->second.aggregate_flags,
      .bytes = std::move(frame->second.bytes),
  };
  incomplete_frames_.erase(frame);
  remove_from_order(parsed.header.sequence);
  highest_completed_sequence_ = parsed.header.sequence;
  ++metrics_.completed_frames;
  events_.push_back({.kind = FrameAssemblerEventKind::frame_completed,
                     .sequence = parsed.header.sequence,
                     .status = FramePushStatus::completed});

  if (awaiting_idr_ && packet_is_idr) {
    awaiting_idr_ = false;
    events_.push_back({.kind = FrameAssemblerEventKind::idr_recovered,
                       .sequence = parsed.header.sequence,
                       .status = FramePushStatus::completed});
  }
  return {.status = FramePushStatus::completed, .frame = std::move(completed)};
}

bool FrameAssembler::awaiting_idr() const noexcept { return awaiting_idr_; }

std::size_t FrameAssembler::incomplete_frame_count() const noexcept {
  return incomplete_frames_.size();
}

const FrameAssemblerMetrics& FrameAssembler::metrics() const noexcept { return metrics_; }

std::vector<FrameAssemblerEvent> FrameAssembler::take_events() {
  auto result = std::move(events_);
  events_.clear();
  return result;
}

FramePushResult FrameAssembler::reject(std::uint64_t sequence, FramePushStatus status) {
  ++metrics_.rejected_chunks;
  events_.push_back(
      {.kind = FrameAssemblerEventKind::chunk_rejected, .sequence = sequence, .status = status});
  return {.status = status, .frame = std::nullopt};
}

FramePushResult FrameAssembler::suppress_non_idr(std::uint64_t sequence) {
  ++metrics_.suppressed_frames;
  events_.push_back({.kind = FrameAssemblerEventKind::non_idr_suppressed,
                     .sequence = sequence,
                     .status = FramePushStatus::suppressed_awaiting_idr});
  return {.status = FramePushStatus::suppressed_awaiting_idr, .frame = std::nullopt};
}

void FrameAssembler::enter_recovery() {
  const auto recovery_sequence = insertion_order_.front();
  while (!insertion_order_.empty()) {
    const auto sequence = insertion_order_.front();
    insertion_order_.pop_front();
    incomplete_frames_.erase(sequence);
    ++metrics_.evicted_frames;
    events_.push_back({.kind = FrameAssemblerEventKind::frame_evicted,
                       .sequence = sequence,
                       .status = FramePushStatus::accepted_incomplete});
  }
  if (!awaiting_idr_) {
    awaiting_idr_ = true;
    ++metrics_.idr_requests;
    events_.push_back({.kind = FrameAssemblerEventKind::idr_requested,
                       .sequence = recovery_sequence,
                       .status = FramePushStatus::accepted_incomplete});
  }
}

void FrameAssembler::remove_from_order(std::uint64_t sequence) {
  const auto position = std::ranges::find(insertion_order_, sequence);
  if (position != insertion_order_.end()) {
    insertion_order_.erase(position);
  }
}

}  // namespace beacon::stream
