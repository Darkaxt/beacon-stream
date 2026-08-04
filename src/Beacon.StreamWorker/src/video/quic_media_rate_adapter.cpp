#include "beacon/worker/video/quic_media_rate_adapter.h"

#include <type_traits>
#include <variant>

namespace beacon::worker::video {

std::optional<MediaRateEvidence>
media_rate_evidence_from(const QuicMediaEvent &event,
                         std::uint64_t evidence_sequence) {
  if (evidence_sequence == 0) {
    return std::nullopt;
  }
  return std::visit(
      [evidence_sequence](
          const auto &value) -> std::optional<MediaRateEvidence> {
        using Event = std::decay_t<decltype(value)>;
        if constexpr (std::is_same_v<Event, QuicDatagramOutcome>) {
          std::optional<MediaRateEvidenceKind> kind;
          switch (value.kind) {
          case QuicDatagramOutcomeKind::acknowledged:
            kind = MediaRateEvidenceKind::transport_datagram_acknowledged;
            break;
          case QuicDatagramOutcomeKind::lost:
            kind = MediaRateEvidenceKind::transport_datagram_lost;
            break;
          case QuicDatagramOutcomeKind::canceled:
            kind = MediaRateEvidenceKind::transport_datagram_canceled;
            break;
          }
          if (!kind.has_value()) {
            return std::nullopt;
          }
          return MediaRateEvidence{
              .kind = *kind,
              .session_generation = value.session_generation,
              .evidence_sequence = evidence_sequence,
              .frame_sequence = value.access_unit_sequence,
              .smoothed_rtt_us = value.smoothed_rtt_us,
              .congestion_window_bytes = value.congestion_window_bytes,
          };
        } else if constexpr (
            std::is_same_v<
                Event,
                stream::ServerSessionProtocolOutput::AcceptedIdrRequest>) {
          return MediaRateEvidence{
              .kind = MediaRateEvidenceKind::reliable_idr_request,
              .session_generation = value.session_generation,
              .evidence_sequence = evidence_sequence,
              .frame_sequence = value.request.last_complete_sequence(),
          };
        } else if constexpr (
            std::is_same_v<
                Event, stream::ServerSessionProtocolOutput::ParsedFeedback>) {
          const auto &feedback = value.feedback;
          if (feedback.has_rendered_frame()) {
            return MediaRateEvidence{
                .kind = MediaRateEvidenceKind::client_frame_rendered,
                .session_generation = value.session_generation,
                .evidence_sequence = evidence_sequence,
                .frame_sequence = feedback.rendered_frame().frame_sequence(),
                .presentation_time_us =
                    feedback.rendered_frame().presentation_time_us(),
                .rendered_at_us = feedback.rendered_frame().rendered_at_us(),
            };
          }
          if (feedback.has_datagram_loss()) {
            return MediaRateEvidence{
                .kind = MediaRateEvidenceKind::client_datagram_loss,
                .session_generation = value.session_generation,
                .evidence_sequence = evidence_sequence,
                .frame_sequence = feedback.datagram_loss().frame_sequence(),
                .missing_chunk_count = static_cast<std::uint32_t>(
                    feedback.datagram_loss().missing_chunk_indexes_size()),
            };
          }
          if (feedback.has_queue_depth()) {
            return MediaRateEvidence{
                .kind = MediaRateEvidenceKind::client_queue,
                .session_generation = value.session_generation,
                .evidence_sequence = evidence_sequence,
                .queued_access_units =
                    feedback.queue_depth().queued_access_units(),
                .dropped_access_units =
                    feedback.queue_depth().dropped_access_units(),
            };
          }
          if (feedback.has_decoder() &&
              feedback.decoder().state() ==
                  stream::v1::DECODER_STATE_AWAITING_IDR) {
            return MediaRateEvidence{
                .kind = MediaRateEvidenceKind::decoder_awaiting_idr,
                .session_generation = value.session_generation,
                .evidence_sequence = evidence_sequence,
            };
          }
          return std::nullopt;
        } else {
          return std::nullopt;
        }
      },
      event);
}

} // namespace beacon::worker::video
