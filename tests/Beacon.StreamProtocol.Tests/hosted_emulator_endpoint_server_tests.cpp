#include "hosted_emulator_endpoint_server.h"

#include "beacon/stream/selected_video_mode.h"
#include "test_failure.h"

#include <cstddef>
#include <span>
#include <string_view>

namespace {

using beacon::stream::StreamTicketAuthorizationResult;
using beacon::stream::testing::HostedEmulatorFrameFlow;
using beacon::stream::testing::HostedEmulatorFrameFlowError;
using beacon::stream::testing::HostedEmulatorTicketAuthorizer;

std::span<const std::byte> bytes(std::string_view value) {
  return {reinterpret_cast<const std::byte *>(value.data()), value.size()};
}

void exact_test_identity_receives_the_fixed_video_plan_once() {
  HostedEmulatorTicketAuthorizer authorizer;

  const auto accepted = authorizer.authorize(
      bytes("beacon-hosted-emulator-ticket-v1"), "hosted-emulator",
      "hosted-emulator-stream", 1, 1'000);
  const auto replayed = authorizer.authorize(
      bytes("beacon-hosted-emulator-ticket-v1"), "hosted-emulator",
      "hosted-emulator-stream", 1, 1'001);

  BEACON_TEST_REQUIRE(accepted.result ==
                      StreamTicketAuthorizationResult::accepted);
  BEACON_TEST_REQUIRE(accepted.selected_video.has_value());
  BEACON_TEST_REQUIRE(!accepted.benchmark_plan.has_value());
  BEACON_TEST_REQUIRE(accepted.selected_video->codec() ==
                      beacon::stream::v1::VIDEO_CODEC_H264);
  BEACON_TEST_REQUIRE(accepted.selected_video->width() == 640);
  BEACON_TEST_REQUIRE(accepted.selected_video->height() == 360);
  BEACON_TEST_REQUIRE(accepted.selected_video->frames_per_second_numerator() ==
                      30);
  BEACON_TEST_REQUIRE(
      accepted.selected_video->frames_per_second_denominator() == 1);
  BEACON_TEST_REQUIRE(accepted.selected_video->dynamic_range() ==
                      beacon::stream::v1::DYNAMIC_RANGE_SDR);
  BEACON_TEST_REQUIRE(
      beacon::stream::valid_selected_video_mode(*accepted.selected_video));
  BEACON_TEST_REQUIRE(replayed.result ==
                      StreamTicketAuthorizationResult::replayed);
}

void every_identity_component_is_validated_before_consumption() {
  HostedEmulatorTicketAuthorizer authorizer;

  BEACON_TEST_REQUIRE(authorizer
                          .authorize(bytes("wrong-ticket"), "hosted-emulator",
                                     "hosted-emulator-stream", 1, 1'000)
                          .result == StreamTicketAuthorizationResult::unknown);
  BEACON_TEST_REQUIRE(
      authorizer
          .authorize(bytes("beacon-hosted-emulator-ticket-v1"), "wrong-client",
                     "hosted-emulator-stream", 1, 1'000)
          .result == StreamTicketAuthorizationResult::client_mismatch);
  BEACON_TEST_REQUIRE(
      authorizer
          .authorize(bytes("beacon-hosted-emulator-ticket-v1"),
                     "hosted-emulator", "wrong-session", 1, 1'000)
          .result == StreamTicketAuthorizationResult::session_mismatch);
  BEACON_TEST_REQUIRE(
      authorizer
          .authorize(bytes("beacon-hosted-emulator-ticket-v1"),
                     "hosted-emulator", "hosted-emulator-stream", 2, 1'000)
          .result == StreamTicketAuthorizationResult::plan_mismatch);

  BEACON_TEST_REQUIRE(authorizer
                          .authorize(bytes("beacon-hosted-emulator-ticket-v1"),
                                     "hosted-emulator",
                                     "hosted-emulator-stream", 1, 1'000)
                          .result == StreamTicketAuthorizationResult::accepted);
}

void frame_flow_releases_exactly_one_unit_per_rendered_feedback() {
  HostedEmulatorFrameFlow flow{30};

  const auto started = flow.start();
  BEACON_TEST_REQUIRE(started.error == HostedEmulatorFrameFlowError::none);
  BEACON_TEST_REQUIRE(started.next_access_unit_index == 0);

  for (std::uint64_t sequence = 1; sequence <= 30; ++sequence) {
    const auto rendered = flow.rendered(sequence);
    BEACON_TEST_REQUIRE(rendered.error == HostedEmulatorFrameFlowError::none);
    if (sequence < 30) {
      BEACON_TEST_REQUIRE(rendered.next_access_unit_index == sequence);
    } else {
      BEACON_TEST_REQUIRE(!rendered.next_access_unit_index.has_value());
      BEACON_TEST_REQUIRE(rendered.rendering_complete);
    }
  }

  BEACON_TEST_REQUIRE(flow.stop() == HostedEmulatorFrameFlowError::none);
  BEACON_TEST_REQUIRE(flow.sent_frames() == 30);
  BEACON_TEST_REQUIRE(flow.rendered_feedback() == 30);
}

void frame_flow_rejects_early_duplicate_and_out_of_order_events() {
  HostedEmulatorFrameFlow flow{3};

  BEACON_TEST_REQUIRE(flow.rendered(1).error ==
                      HostedEmulatorFrameFlowError::not_started);
  BEACON_TEST_REQUIRE(flow.stop() == HostedEmulatorFrameFlowError::not_started);
  BEACON_TEST_REQUIRE(flow.start().next_access_unit_index == 0);
  BEACON_TEST_REQUIRE(flow.start().error ==
                      HostedEmulatorFrameFlowError::already_started);
  BEACON_TEST_REQUIRE(flow.rendered(2).error ==
                      HostedEmulatorFrameFlowError::unexpected_sequence);
  BEACON_TEST_REQUIRE(flow.rendered(1).next_access_unit_index == 1);
  BEACON_TEST_REQUIRE(flow.rendered(1).error ==
                      HostedEmulatorFrameFlowError::unexpected_sequence);
  BEACON_TEST_REQUIRE(flow.stop() ==
                      HostedEmulatorFrameFlowError::rendering_incomplete);
}

void frame_flow_accepts_stop_while_the_final_feedback_is_in_flight() {
  HostedEmulatorFrameFlow flow{3};

  BEACON_TEST_REQUIRE(flow.start().next_access_unit_index == 0);
  BEACON_TEST_REQUIRE(flow.rendered(1).next_access_unit_index == 1);
  BEACON_TEST_REQUIRE(flow.rendered(2).next_access_unit_index == 2);
  BEACON_TEST_REQUIRE(flow.sent_frames() == 3);
  BEACON_TEST_REQUIRE(flow.rendered_feedback() == 2);
  BEACON_TEST_REQUIRE(flow.stop() == HostedEmulatorFrameFlowError::none);
  BEACON_TEST_REQUIRE(flow.rendered(3).rendering_complete);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    exact_test_identity_receives_the_fixed_video_plan_once();
    every_identity_component_is_validated_before_consumption();
    frame_flow_releases_exactly_one_unit_per_rendered_feedback();
    frame_flow_rejects_early_duplicate_and_out_of_order_events();
    frame_flow_accepts_stop_while_the_final_feedback_is_in_flight();
  });
}
