#pragma once

#include "beacon/worker/media_datagram_transport.h"
#include "beacon/worker/video/media_packetizer.h"
#include "beacon/worker/video/media_rate_controller.h"

#include <cstddef>
#include <cstdint>
#include <mutex>
#include <optional>
#include <vector>

namespace beacon::worker::video {

enum class VideoMediaSessionFailure {
  none,
  invalid_rate_plan,
  transport_not_ready,
  packetization_failed,
  transport_closed,
  stale_generation,
  bitrate_reconfiguration_failed,
};

struct VideoMediaSendResult {
  VideoMediaSessionFailure failure{VideoMediaSessionFailure::none};
  MediaPacketizerFailure packetizer_failure{MediaPacketizerFailure::none};
  std::uint64_t sequence{};
  std::size_t attempted_datagrams{};
  std::size_t accepted_datagrams{};
};

enum class MediaControlApplicationOutcome {
  applied,
  failed,
  superseded,
};

struct MediaControlApplication {
  std::uint64_t decision_sequence{};
  std::uint32_t bitrate_bps{};
  MediaControlApplicationOutcome outcome{
      MediaControlApplicationOutcome::applied};
};

struct VideoEncoderControlResult {
  bool ready{true};
  bool force_idr{};
  std::optional<std::uint32_t> applied_bitrate_bps;
  VideoMediaSessionFailure failure{VideoMediaSessionFailure::none};
};

class VideoMediaSession final {
public:
  VideoMediaSession(IMediaDatagramTransport &transport,
                    IVideoBitrateControl &bitrate_control,
                    MediaRatePlan rate_plan);

  [[nodiscard]] bool
  begin_transport_generation(std::uint64_t session_generation,
                             std::uint16_t maximum_datagram_bytes);
  [[nodiscard]] VideoMediaSendResult
  send_access_unit(std::uint64_t session_generation,
                   const EncodedH264AccessUnit &access_unit,
                   std::uint64_t presentation_time_us);
  [[nodiscard]] MediaRateDecision observe(MediaRateEvidence evidence);
  [[nodiscard]] VideoEncoderControlResult apply_pending_encoder_control();
  [[nodiscard]] std::vector<MediaRateDecision> take_rate_decisions();
  [[nodiscard]] std::vector<MediaControlApplication>
  take_control_applications();
  [[nodiscard]] VideoMediaSessionFailure failure() const;

private:
  struct PendingBitrate {
    std::uint64_t decision_sequence{};
    std::uint32_t bitrate_bps{};
  };

  void arm_idr();
  void arm_idr_and_close_transport_generation();

  IMediaDatagramTransport &transport_;
  IVideoBitrateControl &bitrate_control_;
  MediaPacketizer packetizer_;
  MediaRateController rate_controller_;
  std::mutex send_mutex_;
  mutable std::mutex mutex_;
  std::uint16_t maximum_datagram_bytes_{};
  std::uint64_t session_generation_{};
  std::uint64_t next_access_unit_sequence_{1};
  std::uint64_t idr_request_revision_{1};
  bool force_idr_{true};
  std::optional<PendingBitrate> pending_bitrate_;
  VideoMediaSessionFailure failure_{VideoMediaSessionFailure::none};
  std::vector<MediaControlApplication> control_applications_;
};

} // namespace beacon::worker::video
