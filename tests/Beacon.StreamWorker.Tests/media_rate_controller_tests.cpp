#include "beacon/worker/video/media_rate_controller.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <cstdint>

namespace {

namespace video = beacon::worker::video;

video::MediaRatePlan rate_plan() {
  return {
      .minimum_bitrate_bps = 8'000'000,
      .initial_bitrate_bps = 24'000'000,
      .maximum_bitrate_bps = 40'000'000,
  };
}

void begin(video::MediaRateController &controller,
           std::uint64_t session_generation = 1) {
  BEACON_TEST_REQUIRE(
      controller.begin_transport_generation(session_generation));
}

void invalid_plan_fails_closed() {
  const video::MediaRateController zero({});
  const video::MediaRateController reversed({
      .minimum_bitrate_bps = 20,
      .initial_bitrate_bps = 10,
      .maximum_bitrate_bps = 30,
  });
  const video::MediaRateController over_max({
      .minimum_bitrate_bps = 10,
      .initial_bitrate_bps = 40,
      .maximum_bitrate_bps = 30,
  });

  BEACON_TEST_REQUIRE(!zero.valid());
  BEACON_TEST_REQUIRE(!reversed.valid());
  BEACON_TEST_REQUIRE(!over_max.valid());
}

void transport_loss_records_all_facts_and_reduces_within_bounds() {
  video::MediaRateController controller(rate_plan());
  begin(controller);
  const video::MediaRateEvidence evidence{
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 1,
      .evidence_sequence = 19,
      .frame_sequence = 7,
      .smoothed_rtt_us = 4'321,
      .congestion_window_bytes = 98'765,
  };

  const auto decision = controller.observe(evidence);

  BEACON_TEST_REQUIRE(controller.valid());
  BEACON_TEST_REQUIRE(decision.evidence.kind == evidence.kind);
  BEACON_TEST_REQUIRE(decision.evidence.evidence_sequence == 19);
  BEACON_TEST_REQUIRE(decision.evidence.frame_sequence == 7);
  BEACON_TEST_REQUIRE(decision.evidence.smoothed_rtt_us == 4'321);
  BEACON_TEST_REQUIRE(decision.evidence.congestion_window_bytes == 98'765);
  BEACON_TEST_REQUIRE(decision.previous_bitrate_bps == 24'000'000);
  BEACON_TEST_REQUIRE(decision.target_bitrate_bps == 21'000'000);
  BEACON_TEST_REQUIRE(decision.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(decision.force_idr);
  BEACON_TEST_REQUIRE(decision.reason ==
                      video::MediaRateDecisionReason::transport_loss);
  BEACON_TEST_REQUIRE(controller.current_bitrate_bps() == 21'000'000);
}

void repeated_loss_never_crosses_the_server_minimum() {
  video::MediaRateController controller(rate_plan());
  begin(controller);
  for (std::uint64_t sequence = 1; sequence <= 64; ++sequence) {
    const auto decision = controller.observe({
        .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
        .session_generation = 1,
        .evidence_sequence = sequence,
        .frame_sequence = sequence,
    });
    BEACON_TEST_REQUIRE(decision.target_bitrate_bps >= 8'000'000);
    BEACON_TEST_REQUIRE(decision.target_bitrate_bps <= 40'000'000);
  }

  BEACON_TEST_REQUIRE(controller.current_bitrate_bps() == 8'000'000);
  const auto held = controller.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 1,
      .evidence_sequence = 65,
      .frame_sequence = 65,
  });
  BEACON_TEST_REQUIRE(!held.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(held.force_idr);
  BEACON_TEST_REQUIRE(held.reason ==
                      video::MediaRateDecisionReason::minimum_held);
}

void multiple_lost_chunks_from_one_frame_reduce_only_once() {
  video::MediaRateController controller(rate_plan());
  begin(controller);
  const auto first = controller.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 1,
      .evidence_sequence = 20,
      .frame_sequence = 7,
  });
  const auto second = controller.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 1,
      .evidence_sequence = 21,
      .frame_sequence = 7,
  });
  const auto client_confirmation = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_datagram_loss,
      .session_generation = 1,
      .evidence_sequence = 22,
      .frame_sequence = 7,
      .missing_chunk_count = 2,
  });

  BEACON_TEST_REQUIRE(first.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(first.target_bitrate_bps == 21'000'000);
  BEACON_TEST_REQUIRE(!second.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(second.force_idr);
  BEACON_TEST_REQUIRE(second.reason ==
                      video::MediaRateDecisionReason::duplicate_frame_loss);
  BEACON_TEST_REQUIRE(!client_confirmation.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(client_confirmation.force_idr);
  BEACON_TEST_REQUIRE(client_confirmation.reason ==
                      video::MediaRateDecisionReason::duplicate_frame_loss);
  BEACON_TEST_REQUIRE(controller.current_bitrate_bps() == 21'000'000);
}

void delayed_distinct_loss_is_not_misclassified_as_a_duplicate() {
  video::MediaRateController controller(rate_plan());
  begin(controller, 7);
  const auto frame_eleven = controller.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 7,
      .evidence_sequence = 1,
      .frame_sequence = 11,
  });
  const auto delayed_frame_ten = controller.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 7,
      .evidence_sequence = 2,
      .frame_sequence = 10,
  });
  const auto duplicate_eleven = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_datagram_loss,
      .session_generation = 7,
      .evidence_sequence = 3,
      .frame_sequence = 11,
      .missing_chunk_count = 1,
  });

  BEACON_TEST_REQUIRE(frame_eleven.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(delayed_frame_ten.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(delayed_frame_ten.reason ==
                      video::MediaRateDecisionReason::transport_loss);
  BEACON_TEST_REQUIRE(!duplicate_eleven.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(duplicate_eleven.reason ==
                      video::MediaRateDecisionReason::duplicate_frame_loss);
}

void stale_generation_evidence_cannot_change_current_recovery_state() {
  video::MediaRateController controller(rate_plan());
  begin(controller, 1);
  begin(controller, 2);
  const auto bitrate_before = controller.current_bitrate_bps();

  const auto stale = controller.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 1,
      .evidence_sequence = 1,
      .frame_sequence = 9,
  });

  BEACON_TEST_REQUIRE(stale.reason ==
                      video::MediaRateDecisionReason::stale_generation);
  BEACON_TEST_REQUIRE(!stale.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(!stale.force_idr);
  BEACON_TEST_REQUIRE(controller.current_bitrate_bps() == bitrate_before);
}

void queue_pressure_reduces_once_until_the_queue_recovers() {
  video::MediaRateController controller(rate_plan());
  begin(controller);

  const auto pressure = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 1,
      .queued_access_units = 3,
  });
  const auto sustained = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 2,
      .queued_access_units = 4,
  });
  const auto recovered = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 3,
      .queued_access_units = 1,
  });
  const auto pressure_again = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 4,
      .queued_access_units = 3,
  });

  BEACON_TEST_REQUIRE(pressure.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(pressure.reason ==
                      video::MediaRateDecisionReason::queue_pressure);
  BEACON_TEST_REQUIRE(!sustained.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(sustained.reason ==
                      video::MediaRateDecisionReason::queue_stable);
  BEACON_TEST_REQUIRE(!recovered.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(recovered.reason ==
                      video::MediaRateDecisionReason::queue_recovered);
  BEACON_TEST_REQUIRE(pressure_again.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(pressure_again.target_bitrate_bps <
                      pressure.target_bitrate_bps);
}

void client_drop_and_reliable_idr_are_recovery_without_retransmission_policy() {
  video::MediaRateController controller(rate_plan());
  begin(controller);

  const auto dropped = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 8,
      .queued_access_units = 2,
      .dropped_access_units = 1,
  });
  const auto same_total = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 9,
      .queued_access_units = 2,
      .dropped_access_units = 1,
  });
  const auto request = controller.observe({
      .kind = video::MediaRateEvidenceKind::reliable_idr_request,
      .session_generation = 1,
      .evidence_sequence = 10,
      .frame_sequence = 77,
  });

  BEACON_TEST_REQUIRE(dropped.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(dropped.force_idr);
  BEACON_TEST_REQUIRE(dropped.reason ==
                      video::MediaRateDecisionReason::client_drop);
  BEACON_TEST_REQUIRE(!same_total.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(!same_total.force_idr);
  BEACON_TEST_REQUIRE(!request.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(request.force_idr);
  BEACON_TEST_REQUIRE(request.reason ==
                      video::MediaRateDecisionReason::reliable_idr_request);

  const auto records = controller.take_decisions();
  BEACON_TEST_REQUIRE(records.size() == 3);
  BEACON_TEST_REQUIRE(controller.take_decisions().empty());
}

void healthy_observations_are_recorded_without_inventing_policy() {
  video::MediaRateController controller(rate_plan());
  begin(controller);
  const auto acknowledged = controller.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_acknowledged,
      .session_generation = 1,
      .evidence_sequence = 11,
      .frame_sequence = 4,
      .smoothed_rtt_us = 2'100,
      .congestion_window_bytes = 120'000,
  });
  const auto rendered = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_frame_rendered,
      .session_generation = 1,
      .evidence_sequence = 12,
      .frame_sequence = 4,
      .presentation_time_us = 100'000,
      .rendered_at_us = 104'000,
  });

  BEACON_TEST_REQUIRE(!acknowledged.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(!acknowledged.force_idr);
  BEACON_TEST_REQUIRE(acknowledged.reason ==
                      video::MediaRateDecisionReason::transport_acknowledged);
  BEACON_TEST_REQUIRE(!rendered.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(!rendered.force_idr);
  BEACON_TEST_REQUIRE(rendered.reason ==
                      video::MediaRateDecisionReason::frame_rendered);
  BEACON_TEST_REQUIRE(controller.current_bitrate_bps() == 24'000'000);
  BEACON_TEST_REQUIRE(controller.take_decisions().size() == 2);
}

void reconnect_resets_client_counters_without_resetting_planned_rate() {
  video::MediaRateController controller(rate_plan());
  begin(controller);
  const auto before = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 1,
      .queued_access_units = 2,
      .dropped_access_units = 10,
  });
  BEACON_TEST_REQUIRE(before.reconfigure_bitrate);
  const auto reduced = controller.current_bitrate_bps();

  begin(controller, 2);
  const auto after = controller.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 2,
      .evidence_sequence = 2,
      .queued_access_units = 2,
      .dropped_access_units = 1,
  });

  BEACON_TEST_REQUIRE(after.reason ==
                      video::MediaRateDecisionReason::client_drop);
  BEACON_TEST_REQUIRE(after.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(after.previous_bitrate_bps == reduced);
  BEACON_TEST_REQUIRE(controller.current_bitrate_bps() < reduced);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    invalid_plan_fails_closed();
    transport_loss_records_all_facts_and_reduces_within_bounds();
    repeated_loss_never_crosses_the_server_minimum();
    multiple_lost_chunks_from_one_frame_reduce_only_once();
    delayed_distinct_loss_is_not_misclassified_as_a_duplicate();
    stale_generation_evidence_cannot_change_current_recovery_state();
    queue_pressure_reduces_once_until_the_queue_recovers();
    client_drop_and_reliable_idr_are_recovery_without_retransmission_policy();
    healthy_observations_are_recorded_without_inventing_policy();
    reconnect_resets_client_counters_without_resetting_planned_rate();
  });
}
