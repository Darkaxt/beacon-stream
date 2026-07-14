#pragma once

#include <cstdint>
#include <deque>
#include <vector>

namespace beacon::worker::video {

class IVideoBitrateControl {
public:
  virtual ~IVideoBitrateControl() = default;
  [[nodiscard]] virtual bool
  reconfigure_bitrate(std::uint32_t bitrate_bps) noexcept = 0;
  [[nodiscard]] virtual std::uint32_t
  configured_bitrate_bps() const noexcept = 0;
};

struct MediaRatePlan {
  std::uint32_t minimum_bitrate_bps{};
  std::uint32_t initial_bitrate_bps{};
  std::uint32_t maximum_bitrate_bps{};
};

enum class MediaRateEvidenceKind {
  unspecified,
  transport_datagram_acknowledged,
  transport_datagram_lost,
  transport_datagram_canceled,
  client_queue,
  client_datagram_loss,
  client_frame_rendered,
  decoder_awaiting_idr,
  reliable_idr_request,
};

struct MediaRateEvidence {
  MediaRateEvidenceKind kind{MediaRateEvidenceKind::unspecified};
  std::uint64_t session_generation{};
  std::uint64_t evidence_sequence{};
  std::uint64_t frame_sequence{};
  std::uint64_t smoothed_rtt_us{};
  std::uint64_t congestion_window_bytes{};
  std::uint32_t queued_access_units{};
  std::uint64_t dropped_access_units{};
  std::uint32_t missing_chunk_count{};
  std::uint64_t presentation_time_us{};
  std::uint64_t rendered_at_us{};
};

enum class MediaRateDecisionReason {
  invalid_plan,
  invalid_evidence,
  stale_generation,
  transport_acknowledged,
  transport_loss,
  transport_canceled,
  queue_pressure,
  queue_stable,
  queue_recovered,
  client_drop,
  client_datagram_loss,
  frame_rendered,
  decoder_awaiting_idr,
  reliable_idr_request,
  duplicate_frame_loss,
  minimum_held,
};

struct MediaRateDecision {
  std::uint64_t decision_sequence{};
  MediaRateEvidence evidence;
  MediaRateDecisionReason reason{MediaRateDecisionReason::invalid_evidence};
  std::uint32_t previous_bitrate_bps{};
  std::uint32_t target_bitrate_bps{};
  bool reconfigure_bitrate{};
  bool force_idr{};
};

class MediaRateController final {
public:
  explicit MediaRateController(MediaRatePlan plan) noexcept;

  [[nodiscard]] bool valid() const noexcept;
  [[nodiscard]] std::uint32_t current_bitrate_bps() const noexcept;
  [[nodiscard]] bool
  begin_transport_generation(std::uint64_t session_generation) noexcept;
  [[nodiscard]] MediaRateDecision observe(MediaRateEvidence evidence);
  [[nodiscard]] std::vector<MediaRateDecision> take_decisions() noexcept;

private:
  [[nodiscard]] MediaRateDecision
  base_decision(MediaRateEvidence evidence,
                MediaRateDecisionReason reason) noexcept;
  void reduce(MediaRateDecision &decision) noexcept;
  [[nodiscard]] bool remember_lost_frame(std::uint64_t frame_sequence);

  MediaRatePlan plan_;
  std::uint32_t current_bitrate_bps_{};
  std::uint64_t next_decision_sequence_{1};
  std::uint64_t last_dropped_access_units_{};
  std::uint64_t active_generation_{};
  bool valid_{};
  bool queue_pressure_active_{};
  std::deque<std::uint64_t> lost_frames_;
  std::vector<MediaRateDecision> decisions_;
};

} // namespace beacon::worker::video
