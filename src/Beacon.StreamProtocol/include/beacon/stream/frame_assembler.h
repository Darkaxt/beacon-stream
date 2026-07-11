#pragma once

#include "beacon/stream/media_datagram.h"

#include <cstddef>
#include <cstdint>
#include <deque>
#include <optional>
#include <span>
#include <unordered_map>
#include <vector>

namespace beacon::stream {

inline constexpr std::size_t maximum_incomplete_video_frames = 4;

enum class FramePushStatus {
  accepted_incomplete,
  completed,
  rejected_malformed,
  rejected_frame_size,
  rejected_duplicate,
  rejected_overlap,
  rejected_inconsistent,
  suppressed_awaiting_idr,
};

enum class FrameAssemblerEventKind {
  chunk_rejected,
  frame_completed,
  frame_evicted,
  idr_requested,
  non_idr_suppressed,
  idr_recovered,
};

struct FrameAssemblerEvent {
  FrameAssemblerEventKind kind{};
  std::uint64_t sequence{};
  FramePushStatus status{};
};

struct FrameAssemblerMetrics {
  std::uint64_t completed_frames{};
  std::uint64_t rejected_chunks{};
  std::uint64_t evicted_frames{};
  std::uint64_t suppressed_frames{};
  std::uint64_t idr_requests{};
};

struct CompletedFrame {
  std::uint64_t sequence{};
  std::uint64_t presentation_time_us{};
  MediaDatagramFlags flags{MediaDatagramFlags::none};
  std::vector<std::byte> bytes;
};

struct FramePushResult {
  FramePushStatus status{FramePushStatus::rejected_malformed};
  std::optional<CompletedFrame> frame;
};

class FrameAssembler {
 public:
  explicit FrameAssembler(std::uint32_t planned_maximum_frame_bytes);

  [[nodiscard]] FramePushResult push(std::span<const std::byte> datagram);
  [[nodiscard]] bool awaiting_idr() const noexcept;
  [[nodiscard]] std::size_t incomplete_frame_count() const noexcept;
  [[nodiscard]] const FrameAssemblerMetrics& metrics() const noexcept;
  [[nodiscard]] std::vector<FrameAssemblerEvent> take_events();

 private:
  struct ChunkRange {
    std::uint32_t offset{};
    std::uint16_t size{};
    std::uint16_t index{};
  };

  struct IncompleteFrame {
    std::uint64_t presentation_time_us{};
    std::uint32_t frame_bytes{};
    std::uint16_t chunk_count{};
    MediaDatagramFlags stable_flags{MediaDatagramFlags::none};
    MediaDatagramFlags aggregate_flags{MediaDatagramFlags::none};
    std::uint32_t received_bytes{};
    std::vector<std::byte> bytes;
    std::vector<ChunkRange> chunks;
  };

  [[nodiscard]] FramePushResult reject(std::uint64_t sequence, FramePushStatus status);
  [[nodiscard]] FramePushResult suppress_non_idr(std::uint64_t sequence);
  void enter_recovery();
  void remove_from_order(std::uint64_t sequence);

  std::uint32_t planned_maximum_frame_bytes_{};
  bool awaiting_idr_{};
  std::optional<std::uint64_t> highest_completed_sequence_;
  FrameAssemblerMetrics metrics_{};
  std::unordered_map<std::uint64_t, IncompleteFrame> incomplete_frames_;
  std::deque<std::uint64_t> insertion_order_;
  std::vector<FrameAssemblerEvent> events_;
};

}  // namespace beacon::stream
