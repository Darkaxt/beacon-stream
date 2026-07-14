#include "beacon/worker/video/media_rate_controller.h"

#include <algorithm>
#include <utility>

namespace beacon::worker::video {
namespace {

constexpr std::uint32_t queue_pressure_threshold = 3;
constexpr std::uint32_t queue_recovery_threshold = 1;
constexpr std::uint32_t reduction_divisor = 8;
constexpr std::size_t remembered_lost_frames = 256;

bool valid_plan(const MediaRatePlan &plan) noexcept {
  return plan.minimum_bitrate_bps != 0 &&
         plan.minimum_bitrate_bps <= plan.initial_bitrate_bps &&
         plan.initial_bitrate_bps <= plan.maximum_bitrate_bps;
}

} // namespace

MediaRateController::MediaRateController(MediaRatePlan plan) noexcept
    : plan_(plan), current_bitrate_bps_(plan.initial_bitrate_bps),
      valid_(valid_plan(plan)) {
  if (!valid_) {
    current_bitrate_bps_ = 0;
  }
}

bool MediaRateController::valid() const noexcept { return valid_; }

std::uint32_t MediaRateController::current_bitrate_bps() const noexcept {
  return current_bitrate_bps_;
}

bool MediaRateController::begin_transport_generation(
    std::uint64_t session_generation) noexcept {
  if (!valid_ || session_generation == 0) {
    return false;
  }
  active_generation_ = session_generation;
  last_dropped_access_units_ = 0;
  queue_pressure_active_ = false;
  lost_frames_.clear();
  return true;
}

MediaRateDecision
MediaRateController::base_decision(MediaRateEvidence evidence,
                                   MediaRateDecisionReason reason) noexcept {
  return {
      .decision_sequence = next_decision_sequence_++,
      .evidence = evidence,
      .reason = reason,
      .previous_bitrate_bps = current_bitrate_bps_,
      .target_bitrate_bps = current_bitrate_bps_,
  };
}

void MediaRateController::reduce(MediaRateDecision &decision) noexcept {
  if (current_bitrate_bps_ <= plan_.minimum_bitrate_bps) {
    decision.reason = MediaRateDecisionReason::minimum_held;
    return;
  }
  const auto reduction =
      std::max<std::uint32_t>(1, current_bitrate_bps_ / reduction_divisor);
  const auto candidate = current_bitrate_bps_ > reduction
                             ? current_bitrate_bps_ - reduction
                             : plan_.minimum_bitrate_bps;
  decision.target_bitrate_bps = std::max(plan_.minimum_bitrate_bps, candidate);
  decision.reconfigure_bitrate =
      decision.target_bitrate_bps != current_bitrate_bps_;
  current_bitrate_bps_ = decision.target_bitrate_bps;
}

bool MediaRateController::remember_lost_frame(std::uint64_t frame_sequence) {
  if (std::ranges::find(lost_frames_, frame_sequence) != lost_frames_.end()) {
    return false;
  }
  if (lost_frames_.size() == remembered_lost_frames) {
    lost_frames_.pop_front();
  }
  lost_frames_.push_back(frame_sequence);
  return true;
}

MediaRateDecision MediaRateController::observe(MediaRateEvidence evidence) {
  if (!valid_) {
    auto decision =
        base_decision(evidence, MediaRateDecisionReason::invalid_plan);
    decisions_.push_back(decision);
    return decision;
  }
  if (evidence.kind == MediaRateEvidenceKind::unspecified ||
      evidence.evidence_sequence == 0) {
    auto decision =
        base_decision(evidence, MediaRateDecisionReason::invalid_evidence);
    decisions_.push_back(decision);
    return decision;
  }
  if (active_generation_ == 0 ||
      evidence.session_generation != active_generation_) {
    auto decision =
        base_decision(evidence, MediaRateDecisionReason::stale_generation);
    decisions_.push_back(decision);
    return decision;
  }

  MediaRateDecision decision;
  switch (evidence.kind) {
  case MediaRateEvidenceKind::transport_datagram_acknowledged:
    decision = base_decision(evidence,
                             MediaRateDecisionReason::transport_acknowledged);
    break;
  case MediaRateEvidenceKind::transport_datagram_lost:
    decision = base_decision(evidence, MediaRateDecisionReason::transport_loss);
    decision.force_idr = true;
    if (evidence.frame_sequence == 0) {
      decision.reason = MediaRateDecisionReason::invalid_evidence;
    } else if (!remember_lost_frame(evidence.frame_sequence)) {
      decision.reason = MediaRateDecisionReason::duplicate_frame_loss;
    } else {
      reduce(decision);
    }
    break;
  case MediaRateEvidenceKind::transport_datagram_canceled:
    decision =
        base_decision(evidence, MediaRateDecisionReason::transport_canceled);
    break;
  case MediaRateEvidenceKind::client_queue: {
    if (evidence.dropped_access_units < last_dropped_access_units_) {
      decision =
          base_decision(evidence, MediaRateDecisionReason::invalid_evidence);
      break;
    }
    const bool new_drop =
        evidence.dropped_access_units > last_dropped_access_units_;
    last_dropped_access_units_ = evidence.dropped_access_units;
    const bool pressure =
        evidence.queued_access_units >= queue_pressure_threshold;
    const bool recovered =
        queue_pressure_active_ &&
        evidence.queued_access_units <= queue_recovery_threshold;
    if (recovered) {
      queue_pressure_active_ = false;
    }
    if (new_drop) {
      queue_pressure_active_ = queue_pressure_active_ || pressure;
      decision = base_decision(evidence, MediaRateDecisionReason::client_drop);
      decision.force_idr = true;
      reduce(decision);
    } else if (pressure && !queue_pressure_active_) {
      queue_pressure_active_ = true;
      decision =
          base_decision(evidence, MediaRateDecisionReason::queue_pressure);
      reduce(decision);
    } else {
      decision = base_decision(
          evidence, recovered ? MediaRateDecisionReason::queue_recovered
                              : MediaRateDecisionReason::queue_stable);
    }
    break;
  }
  case MediaRateEvidenceKind::client_datagram_loss:
    decision =
        base_decision(evidence, MediaRateDecisionReason::client_datagram_loss);
    decision.force_idr = true;
    if (evidence.frame_sequence == 0 || evidence.missing_chunk_count == 0) {
      decision.reason = MediaRateDecisionReason::invalid_evidence;
    } else if (!remember_lost_frame(evidence.frame_sequence)) {
      decision.reason = MediaRateDecisionReason::duplicate_frame_loss;
    } else {
      reduce(decision);
    }
    break;
  case MediaRateEvidenceKind::client_frame_rendered:
    decision = base_decision(evidence, MediaRateDecisionReason::frame_rendered);
    break;
  case MediaRateEvidenceKind::decoder_awaiting_idr:
    decision =
        base_decision(evidence, MediaRateDecisionReason::decoder_awaiting_idr);
    decision.force_idr = true;
    break;
  case MediaRateEvidenceKind::reliable_idr_request:
    decision =
        base_decision(evidence, MediaRateDecisionReason::reliable_idr_request);
    decision.force_idr = true;
    break;
  case MediaRateEvidenceKind::unspecified:
    decision =
        base_decision(evidence, MediaRateDecisionReason::invalid_evidence);
    break;
  }
  decisions_.push_back(decision);
  return decision;
}

std::vector<MediaRateDecision> MediaRateController::take_decisions() noexcept {
  auto result = std::move(decisions_);
  decisions_.clear();
  return result;
}

} // namespace beacon::worker::video
