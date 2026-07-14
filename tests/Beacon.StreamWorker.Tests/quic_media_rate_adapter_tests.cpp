#include "beacon/worker/video/quic_media_rate_adapter.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <utility>

namespace {

namespace stream_v1 = beacon::stream::v1;
namespace video = beacon::worker::video;

void transport_outcomes_preserve_generation_and_congestion_facts() {
  const beacon::worker::QuicMediaEvent event =
      beacon::worker::QuicDatagramOutcome{
          .kind = beacon::worker::QuicDatagramOutcomeKind::lost,
          .session_generation = 7,
          .access_unit_sequence = 19,
          .smoothed_rtt_us = 4'321,
          .congestion_window_bytes = 98'765,
      };

  const auto evidence = video::media_rate_evidence_from(event, 3);

  BEACON_TEST_REQUIRE(evidence.has_value());
  BEACON_TEST_REQUIRE(evidence->kind ==
                      video::MediaRateEvidenceKind::transport_datagram_lost);
  BEACON_TEST_REQUIRE(evidence->session_generation == 7);
  BEACON_TEST_REQUIRE(evidence->evidence_sequence == 3);
  BEACON_TEST_REQUIRE(evidence->frame_sequence == 19);
  BEACON_TEST_REQUIRE(evidence->smoothed_rtt_us == 4'321);
  BEACON_TEST_REQUIRE(evidence->congestion_window_bytes == 98'765);
}

void reliable_idr_preserves_the_client_recovery_cursor() {
  beacon::worker::QuicSessionProtocolOutput::AcceptedIdrRequest request{
      .session_generation = 8,
  };
  request.request.set_reason(stream_v1::IDR_REQUEST_REASON_FRAME_EVICTED);
  request.request.set_last_complete_sequence(41);
  const beacon::worker::QuicMediaEvent event = std::move(request);

  const auto evidence = video::media_rate_evidence_from(event, 4);

  BEACON_TEST_REQUIRE(evidence.has_value());
  BEACON_TEST_REQUIRE(evidence->kind ==
                      video::MediaRateEvidenceKind::reliable_idr_request);
  BEACON_TEST_REQUIRE(evidence->session_generation == 8);
  BEACON_TEST_REQUIRE(evidence->frame_sequence == 41);
}

void client_feedback_maps_only_rate_and_recovery_facts() {
  beacon::worker::QuicSessionProtocolOutput::ParsedFeedback queue{
      .session_generation = 9,
  };
  queue.feedback.mutable_queue_depth()->set_queued_access_units(3);
  queue.feedback.mutable_queue_depth()->set_dropped_access_units(2);
  auto queue_evidence =
      video::media_rate_evidence_from(beacon::worker::QuicMediaEvent{queue}, 5);
  BEACON_TEST_REQUIRE(queue_evidence.has_value());
  BEACON_TEST_REQUIRE(queue_evidence->kind ==
                      video::MediaRateEvidenceKind::client_queue);
  BEACON_TEST_REQUIRE(queue_evidence->queued_access_units == 3);
  BEACON_TEST_REQUIRE(queue_evidence->dropped_access_units == 2);

  beacon::worker::QuicSessionProtocolOutput::ParsedFeedback loss{
      .session_generation = 9,
  };
  loss.feedback.mutable_datagram_loss()->set_frame_sequence(22);
  loss.feedback.mutable_datagram_loss()->add_missing_chunk_indexes(1);
  loss.feedback.mutable_datagram_loss()->add_missing_chunk_indexes(4);
  auto loss_evidence =
      video::media_rate_evidence_from(beacon::worker::QuicMediaEvent{loss}, 6);
  BEACON_TEST_REQUIRE(loss_evidence.has_value());
  BEACON_TEST_REQUIRE(loss_evidence->kind ==
                      video::MediaRateEvidenceKind::client_datagram_loss);
  BEACON_TEST_REQUIRE(loss_evidence->frame_sequence == 22);
  BEACON_TEST_REQUIRE(loss_evidence->missing_chunk_count == 2);

  beacon::worker::QuicSessionProtocolOutput::ParsedFeedback rendered{
      .session_generation = 9,
  };
  rendered.feedback.mutable_rendered_frame()->set_frame_sequence(23);
  rendered.feedback.mutable_rendered_frame()->set_presentation_time_us(100);
  rendered.feedback.mutable_rendered_frame()->set_rendered_at_us(104);
  auto rendered_evidence = video::media_rate_evidence_from(
      beacon::worker::QuicMediaEvent{rendered}, 7);
  BEACON_TEST_REQUIRE(rendered_evidence.has_value());
  BEACON_TEST_REQUIRE(rendered_evidence->kind ==
                      video::MediaRateEvidenceKind::client_frame_rendered);
  BEACON_TEST_REQUIRE(rendered_evidence->presentation_time_us == 100);
  BEACON_TEST_REQUIRE(rendered_evidence->rendered_at_us == 104);

  beacon::worker::QuicSessionProtocolOutput::ParsedFeedback decoder{
      .session_generation = 9,
  };
  decoder.feedback.mutable_decoder()->set_state(
      stream_v1::DECODER_STATE_AWAITING_IDR);
  auto decoder_evidence = video::media_rate_evidence_from(
      beacon::worker::QuicMediaEvent{decoder}, 8);
  BEACON_TEST_REQUIRE(decoder_evidence.has_value());
  BEACON_TEST_REQUIRE(decoder_evidence->kind ==
                      video::MediaRateEvidenceKind::decoder_awaiting_idr);

  decoder.feedback.mutable_decoder()->set_state(stream_v1::DECODER_STATE_READY);
  BEACON_TEST_REQUIRE(!video::media_rate_evidence_from(
                           beacon::worker::QuicMediaEvent{decoder}, 9)
                           .has_value());
}

void lifecycle_and_invalid_sequence_events_do_not_invent_rate_evidence() {
  beacon::worker::QuicSessionProtocolOutput::AcceptedStopSession stop{
      .session_generation = 1,
  };
  stop.stop_session.set_reason(stream_v1::SESSION_STOP_REASON_CLIENT_REQUEST);
  const beacon::worker::QuicMediaEvent event = std::move(stop);

  BEACON_TEST_REQUIRE(!video::media_rate_evidence_from(event, 1).has_value());
  const beacon::worker::QuicMediaEvent outcome =
      beacon::worker::QuicDatagramOutcome{
          .kind = beacon::worker::QuicDatagramOutcomeKind::acknowledged,
          .session_generation = 1,
          .access_unit_sequence = 1,
      };
  BEACON_TEST_REQUIRE(!video::media_rate_evidence_from(outcome, 0).has_value());
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    transport_outcomes_preserve_generation_and_congestion_facts();
    reliable_idr_preserves_the_client_recovery_cursor();
    client_feedback_maps_only_rate_and_recovery_facts();
    lifecycle_and_invalid_sequence_events_do_not_invent_rate_evidence();
  });
}
