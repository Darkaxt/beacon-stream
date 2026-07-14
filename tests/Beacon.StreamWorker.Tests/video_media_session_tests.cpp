#include "beacon/stream/media_datagram.h"
#include "beacon/worker/video/video_media_session.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <utility>
#include <vector>

namespace {

namespace stream = beacon::stream;
namespace video = beacon::worker::video;

class RecordingTransport final
    : public beacon::worker::IMediaDatagramTransport {
public:
  stream::TransportSendResult
  send_for_generation(stream::TransportPacket packet,
                      std::uint64_t session_generation) override {
    ++send_calls;
    if (close_on_call.has_value() && send_calls == *close_on_call) {
      return stream::TransportSendResult::connection_closed;
    }
    generations.push_back(session_generation);
    packets.push_back(std::move(packet));
    return stream::TransportSendResult::accepted;
  }

  std::optional<std::size_t> close_on_call;
  std::size_t send_calls{};
  std::vector<std::uint64_t> generations;
  std::vector<stream::TransportPacket> packets;
};

class RecordingBitrateControl final : public video::IVideoBitrateControl {
public:
  bool reconfigure_bitrate(std::uint32_t bitrate_bps) noexcept override {
    requested.push_back(bitrate_bps);
    return accept;
  }

  std::uint32_t configured_bitrate_bps() const noexcept override {
    return configured_bitrate;
  }

  bool accept{true};
  std::uint32_t configured_bitrate{24'000'000};
  std::vector<std::uint32_t> requested;
};

video::MediaRatePlan rate_plan() {
  return {
      .minimum_bitrate_bps = 8'000'000,
      .initial_bitrate_bps = 24'000'000,
      .maximum_bitrate_bps = 40'000'000,
  };
}

video::EncodedH264AccessUnit access_unit(std::size_t bytes, bool idr = false) {
  video::EncodedH264AccessUnit result;
  result.annex_b.resize(bytes);
  for (std::size_t index = 0; index < bytes; ++index) {
    result.annex_b[index] = static_cast<std::uint8_t>(index % 239U);
  }
  result.idr = idr;
  result.has_sps = idr;
  result.has_pps = idr;
  return result;
}

void access_units_are_packetized_and_sent_once() {
  RecordingTransport transport;
  RecordingBitrateControl bitrate;
  video::VideoMediaSession session(transport, bitrate, rate_plan());
  BEACON_TEST_REQUIRE(session.begin_transport_generation(1, 96));
  const auto first_control = session.apply_pending_encoder_control();
  BEACON_TEST_REQUIRE(first_control.ready);
  BEACON_TEST_REQUIRE(first_control.force_idr);
  BEACON_TEST_REQUIRE(!first_control.applied_bitrate_bps.has_value());

  const auto result = session.send_access_unit(1, access_unit(130, true), 55);

  BEACON_TEST_REQUIRE(result.failure == video::VideoMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(result.sequence == 1);
  BEACON_TEST_REQUIRE(result.attempted_datagrams == 3);
  BEACON_TEST_REQUIRE(result.accepted_datagrams == 3);
  BEACON_TEST_REQUIRE(transport.send_calls == 3);
  BEACON_TEST_REQUIRE(transport.packets.size() == 3);
  BEACON_TEST_REQUIRE(transport.generations ==
                      std::vector<std::uint64_t>({1, 1, 1}));
  for (const auto &packet : transport.packets) {
    BEACON_TEST_REQUIRE(packet.sequence == 1);
    BEACON_TEST_REQUIRE(packet.payload.size() <= 96);
  }
  BEACON_TEST_REQUIRE(!session.apply_pending_encoder_control().force_idr);
}

void loss_arms_idr_and_bitrate_without_retransmitting_media() {
  RecordingTransport transport;
  RecordingBitrateControl bitrate;
  video::VideoMediaSession session(transport, bitrate, rate_plan());
  BEACON_TEST_REQUIRE(session.begin_transport_generation(1, 96));
  static_cast<void>(session.apply_pending_encoder_control());
  BEACON_TEST_REQUIRE(
      session.send_access_unit(1, access_unit(64, true), 10).failure ==
      video::VideoMediaSessionFailure::none);
  const auto sends_before_loss = transport.send_calls;

  const auto decision = session.observe({
      .kind = video::MediaRateEvidenceKind::transport_datagram_lost,
      .session_generation = 1,
      .evidence_sequence = 4,
      .frame_sequence = 1,
      .smoothed_rtt_us = 3'000,
      .congestion_window_bytes = 80'000,
  });

  BEACON_TEST_REQUIRE(decision.force_idr);
  BEACON_TEST_REQUIRE(decision.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(transport.send_calls == sends_before_loss);
  const auto control = session.apply_pending_encoder_control();
  BEACON_TEST_REQUIRE(control.ready);
  BEACON_TEST_REQUIRE(control.force_idr);
  BEACON_TEST_REQUIRE(control.applied_bitrate_bps == 21'000'000);
  BEACON_TEST_REQUIRE(bitrate.requested ==
                      std::vector<std::uint32_t>{21'000'000});
  BEACON_TEST_REQUIRE(transport.send_calls == sends_before_loss);
  BEACON_TEST_REQUIRE(session.apply_pending_encoder_control().force_idr);

  const auto completed_before_control =
      session.send_access_unit(1, access_unit(32), 11);
  BEACON_TEST_REQUIRE(completed_before_control.failure ==
                      video::VideoMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(transport.send_calls == sends_before_loss + 1);
  BEACON_TEST_REQUIRE(session.apply_pending_encoder_control().force_idr);
  BEACON_TEST_REQUIRE(
      session.send_access_unit(1, access_unit(32, true), 12).failure ==
      video::VideoMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(!session.apply_pending_encoder_control().force_idr);

  const auto applications = session.take_control_applications();
  BEACON_TEST_REQUIRE(applications.size() == 1);
  BEACON_TEST_REQUIRE(applications[0].decision_sequence ==
                      decision.decision_sequence);
  BEACON_TEST_REQUIRE(applications[0].outcome ==
                      video::MediaControlApplicationOutcome::applied);
}

void packetization_failure_requires_a_fresh_idr() {
  RecordingTransport transport;
  RecordingBitrateControl bitrate;
  video::VideoMediaSession session(transport, bitrate, rate_plan());
  BEACON_TEST_REQUIRE(session.begin_transport_generation(1, 1232));
  static_cast<void>(session.apply_pending_encoder_control());
  BEACON_TEST_REQUIRE(
      session.send_access_unit(1, access_unit(80, true), 9).failure ==
      video::VideoMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(!session.apply_pending_encoder_control().force_idr);
  const auto sends_before_failure = transport.send_calls;
  const video::EncodedH264AccessUnit empty;

  const auto failed = session.send_access_unit(1, empty, 10);

  BEACON_TEST_REQUIRE(failed.failure ==
                      video::VideoMediaSessionFailure::packetization_failed);
  BEACON_TEST_REQUIRE(transport.send_calls == sends_before_failure);
  BEACON_TEST_REQUIRE(session.apply_pending_encoder_control().force_idr);
}

void reliable_idr_request_never_replays_an_access_unit() {
  RecordingTransport transport;
  RecordingBitrateControl bitrate;
  video::VideoMediaSession session(transport, bitrate, rate_plan());
  BEACON_TEST_REQUIRE(session.begin_transport_generation(1, 1232));
  static_cast<void>(session.apply_pending_encoder_control());
  BEACON_TEST_REQUIRE(
      session.send_access_unit(1, access_unit(80, true), 10).failure ==
      video::VideoMediaSessionFailure::none);
  const auto sends_before_request = transport.send_calls;

  const auto decision = session.observe({
      .kind = video::MediaRateEvidenceKind::reliable_idr_request,
      .session_generation = 1,
      .evidence_sequence = 9,
      .frame_sequence = 1,
  });
  const auto control = session.apply_pending_encoder_control();

  BEACON_TEST_REQUIRE(decision.force_idr);
  BEACON_TEST_REQUIRE(!decision.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(control.ready);
  BEACON_TEST_REQUIRE(control.force_idr);
  BEACON_TEST_REQUIRE(!control.applied_bitrate_bps.has_value());
  BEACON_TEST_REQUIRE(bitrate.requested.empty());
  BEACON_TEST_REQUIRE(transport.send_calls == sends_before_request);
}

void partial_send_is_not_retried_and_reconnect_requires_idr() {
  RecordingTransport transport;
  transport.close_on_call = 2;
  RecordingBitrateControl bitrate;
  video::VideoMediaSession session(transport, bitrate, rate_plan());
  BEACON_TEST_REQUIRE(session.begin_transport_generation(1, 96));
  static_cast<void>(session.apply_pending_encoder_control());

  const auto result = session.send_access_unit(1, access_unit(130, true), 55);

  BEACON_TEST_REQUIRE(result.failure ==
                      video::VideoMediaSessionFailure::transport_closed);
  BEACON_TEST_REQUIRE(result.sequence == 1);
  BEACON_TEST_REQUIRE(result.attempted_datagrams == 2);
  BEACON_TEST_REQUIRE(result.accepted_datagrams == 1);
  BEACON_TEST_REQUIRE(transport.send_calls == 2);
  BEACON_TEST_REQUIRE(transport.packets.size() == 1);
  const auto recovery = session.apply_pending_encoder_control();
  BEACON_TEST_REQUIRE(recovery.ready);
  BEACON_TEST_REQUIRE(recovery.force_idr);

  transport.close_on_call.reset();
  BEACON_TEST_REQUIRE(session.begin_transport_generation(2, 96));
  BEACON_TEST_REQUIRE(session.apply_pending_encoder_control().force_idr);
  const auto stale = session.send_access_unit(1, access_unit(20, true), 59);
  BEACON_TEST_REQUIRE(stale.failure ==
                      video::VideoMediaSessionFailure::stale_generation);
  const auto next = session.send_access_unit(2, access_unit(20, true), 60);
  BEACON_TEST_REQUIRE(next.failure == video::VideoMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(next.sequence == 2);
}

void pending_queue_control_does_not_discard_an_encoded_reference_frame() {
  RecordingTransport transport;
  RecordingBitrateControl bitrate;
  video::VideoMediaSession session(transport, bitrate, rate_plan());
  BEACON_TEST_REQUIRE(session.begin_transport_generation(1, 1232));
  static_cast<void>(session.apply_pending_encoder_control());
  BEACON_TEST_REQUIRE(
      session.send_access_unit(1, access_unit(80, true), 10).failure ==
      video::VideoMediaSessionFailure::none);
  const auto decision = session.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 3,
      .queued_access_units = 3,
  });
  BEACON_TEST_REQUIRE(decision.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(!decision.force_idr);
  const auto sends_before = transport.send_calls;

  const auto completed = session.send_access_unit(1, access_unit(64), 11);

  BEACON_TEST_REQUIRE(completed.failure ==
                      video::VideoMediaSessionFailure::none);
  BEACON_TEST_REQUIRE(transport.send_calls == sends_before + 1);
  const auto control = session.apply_pending_encoder_control();
  BEACON_TEST_REQUIRE(control.ready);
  BEACON_TEST_REQUIRE(control.applied_bitrate_bps == 21'000'000);
}

void rate_plan_must_match_the_encoder_initial_bitrate() {
  RecordingTransport transport;
  RecordingBitrateControl bitrate;
  bitrate.configured_bitrate = 12'000'000;

  video::VideoMediaSession session(transport, bitrate, rate_plan());

  BEACON_TEST_REQUIRE(session.failure() ==
                      video::VideoMediaSessionFailure::invalid_rate_plan);
  BEACON_TEST_REQUIRE(!session.begin_transport_generation(1, 1232));
}

void failed_bitrate_reconfiguration_fails_the_media_control_boundary() {
  RecordingTransport transport;
  RecordingBitrateControl bitrate;
  bitrate.accept = false;
  video::VideoMediaSession session(transport, bitrate, rate_plan());
  BEACON_TEST_REQUIRE(session.begin_transport_generation(1, 1232));
  static_cast<void>(session.apply_pending_encoder_control());
  const auto decision = session.observe({
      .kind = video::MediaRateEvidenceKind::client_queue,
      .session_generation = 1,
      .evidence_sequence = 2,
      .queued_access_units = 3,
  });

  const auto control = session.apply_pending_encoder_control();

  BEACON_TEST_REQUIRE(decision.reconfigure_bitrate);
  BEACON_TEST_REQUIRE(!control.ready);
  BEACON_TEST_REQUIRE(
      control.failure ==
      video::VideoMediaSessionFailure::bitrate_reconfiguration_failed);
  BEACON_TEST_REQUIRE(
      session.failure() ==
      video::VideoMediaSessionFailure::bitrate_reconfiguration_failed);
  const auto applications = session.take_control_applications();
  BEACON_TEST_REQUIRE(applications.size() == 1);
  BEACON_TEST_REQUIRE(applications[0].outcome ==
                      video::MediaControlApplicationOutcome::failed);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    access_units_are_packetized_and_sent_once();
    loss_arms_idr_and_bitrate_without_retransmitting_media();
    reliable_idr_request_never_replays_an_access_unit();
    packetization_failure_requires_a_fresh_idr();
    partial_send_is_not_retried_and_reconnect_requires_idr();
    pending_queue_control_does_not_discard_an_encoded_reference_frame();
    rate_plan_must_match_the_encoder_initial_bitrate();
    failed_bitrate_reconfiguration_fails_the_media_control_boundary();
  });
}
