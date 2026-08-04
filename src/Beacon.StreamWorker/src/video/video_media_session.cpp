#include "beacon/worker/video/video_media_session.h"

#include "beacon/stream/media_datagram.h"

#include <utility>

namespace beacon::worker::video {

VideoMediaSession::VideoMediaSession(IMediaDatagramTransport &transport,
                                     IVideoBitrateControl &bitrate_control,
                                     MediaRatePlan rate_plan)
    : transport_(transport), bitrate_control_(bitrate_control),
      rate_controller_(rate_plan) {
  if (!rate_controller_.valid() || bitrate_control_.configured_bitrate_bps() !=
                                       rate_plan.initial_bitrate_bps) {
    failure_ = VideoMediaSessionFailure::invalid_rate_plan;
  }
}

bool VideoMediaSession::begin_transport_generation(
    std::uint64_t session_generation, std::uint16_t maximum_datagram_bytes) {
  if (session_generation == 0 ||
      maximum_datagram_bytes <= stream::media_datagram_header_bytes) {
    return false;
  }
  std::lock_guard send_lock{send_mutex_};
  std::lock_guard lock{mutex_};
  if (failure_ != VideoMediaSessionFailure::none) {
    return false;
  }
  if (!rate_controller_.begin_transport_generation(session_generation)) {
    return false;
  }
  session_generation_ = session_generation;
  maximum_datagram_bytes_ = maximum_datagram_bytes;
  force_idr_ = true;
  ++idr_request_revision_;
  return true;
}

VideoMediaSendResult
VideoMediaSession::send_access_unit(std::uint64_t session_generation,
                                    const EncodedVideoAccessUnit &access_unit,
                                    std::uint64_t presentation_time_us) {
  std::lock_guard send_lock{send_mutex_};
  std::uint16_t maximum_datagram_bytes = 0;
  std::uint64_t sequence = 0;
  std::uint64_t idr_request_revision = 0;
  {
    std::lock_guard lock{mutex_};
    if (failure_ != VideoMediaSessionFailure::none) {
      return {.failure = failure_};
    }
    if (maximum_datagram_bytes_ == 0 || session_generation_ == 0) {
      return {.failure = VideoMediaSessionFailure::transport_not_ready};
    }
    if (session_generation != session_generation_) {
      return {.failure = VideoMediaSessionFailure::stale_generation};
    }
    maximum_datagram_bytes = maximum_datagram_bytes_;
    idr_request_revision = idr_request_revision_;
    sequence = next_access_unit_sequence_++;
  }

  const bool complete_codec_configuration =
      access_unit.codec == NvencVideoCodec::hevc_main10
          ? access_unit.has_vps && access_unit.has_sps && access_unit.has_pps
          : access_unit.has_sps && access_unit.has_pps;
  const stream::EncodedVideoAccessUnitView packetizer_input{
      .bytes = access_unit.annex_b,
      .idr = access_unit.idr,
      .codec_configuration = complete_codec_configuration,
  };
  auto packetized = packetizer_.packetize(packetizer_input, sequence,
                                          presentation_time_us,
                                          maximum_datagram_bytes);
  if (packetized.failure != stream::VideoMediaPacketizerFailure::none) {
    arm_idr();
    return {
        .failure = VideoMediaSessionFailure::packetization_failed,
        .packetizer_failure = packetized.failure,
        .sequence = sequence,
    };
  }

  VideoMediaSendResult result{
      .failure = VideoMediaSessionFailure::none,
      .packetizer_failure = stream::VideoMediaPacketizerFailure::none,
      .sequence = sequence,
  };
  for (auto &packet : packetized.packets) {
    ++result.attempted_datagrams;
    stream::TransportSendResult send_result;
    try {
      send_result =
          transport_.send_for_generation(std::move(packet), session_generation);
    } catch (...) {
      send_result = stream::TransportSendResult::connection_closed;
    }
    if (send_result != stream::TransportSendResult::accepted) {
      result.failure = VideoMediaSessionFailure::transport_closed;
      arm_idr_and_close_transport_generation();
      return result;
    }
    ++result.accepted_datagrams;
  }
  if (access_unit.idr) {
    std::lock_guard lock{mutex_};
    if (session_generation_ == session_generation &&
        idr_request_revision_ == idr_request_revision) {
      force_idr_ = false;
    }
  }
  return result;
}

MediaRateDecision VideoMediaSession::observe(MediaRateEvidence evidence) {
  std::lock_guard lock{mutex_};
  auto decision = rate_controller_.observe(evidence);
  if (decision.force_idr) {
    force_idr_ = true;
    ++idr_request_revision_;
  }
  if (decision.reconfigure_bitrate) {
    if (pending_bitrate_.has_value()) {
      control_applications_.push_back({
          .decision_sequence = pending_bitrate_->decision_sequence,
          .bitrate_bps = pending_bitrate_->bitrate_bps,
          .outcome = MediaControlApplicationOutcome::superseded,
      });
    }
    pending_bitrate_ = PendingBitrate{
        .decision_sequence = decision.decision_sequence,
        .bitrate_bps = decision.target_bitrate_bps,
    };
  }
  return decision;
}

VideoEncoderControlResult VideoMediaSession::apply_pending_encoder_control() {
  std::optional<PendingBitrate> pending;
  bool force_idr = false;
  {
    std::lock_guard lock{mutex_};
    if (failure_ != VideoMediaSessionFailure::none) {
      return {
          .ready = false,
          .failure = failure_,
      };
    }
    pending = pending_bitrate_;
    pending_bitrate_.reset();
    force_idr = force_idr_;
  }

  if (!pending.has_value()) {
    return {
        .ready = true,
        .force_idr = force_idr,
    };
  }

  const bool applied =
      bitrate_control_.reconfigure_bitrate(pending->bitrate_bps);
  {
    std::lock_guard lock{mutex_};
    control_applications_.push_back({
        .decision_sequence = pending->decision_sequence,
        .bitrate_bps = pending->bitrate_bps,
        .outcome = applied ? MediaControlApplicationOutcome::applied
                           : MediaControlApplicationOutcome::failed,
    });
    if (!applied) {
      failure_ = VideoMediaSessionFailure::bitrate_reconfiguration_failed;
      force_idr_ = true;
      ++idr_request_revision_;
    }
  }
  return {
      .ready = applied,
      .force_idr = force_idr,
      .applied_bitrate_bps =
          applied ? std::optional{pending->bitrate_bps} : std::nullopt,
      .failure = applied
                     ? VideoMediaSessionFailure::none
                     : VideoMediaSessionFailure::bitrate_reconfiguration_failed,
  };
}

std::vector<MediaRateDecision> VideoMediaSession::take_rate_decisions() {
  std::lock_guard lock{mutex_};
  return rate_controller_.take_decisions();
}

std::vector<MediaControlApplication>
VideoMediaSession::take_control_applications() {
  std::lock_guard lock{mutex_};
  auto result = std::move(control_applications_);
  control_applications_.clear();
  return result;
}

VideoMediaSessionFailure VideoMediaSession::failure() const {
  std::lock_guard lock{mutex_};
  return failure_;
}

void VideoMediaSession::arm_idr_and_close_transport_generation() {
  std::lock_guard lock{mutex_};
  maximum_datagram_bytes_ = 0;
  session_generation_ = 0;
  force_idr_ = true;
  ++idr_request_revision_;
}

void VideoMediaSession::arm_idr() {
  std::lock_guard lock{mutex_};
  force_idr_ = true;
  ++idr_request_revision_;
}

} // namespace beacon::worker::video
