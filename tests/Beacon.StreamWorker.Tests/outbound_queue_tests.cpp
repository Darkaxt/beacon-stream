#include "beacon/worker/outbound_queue.h"
#include "beacon/worker/worker_events.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "stream_control.pb.h"

#include <cstdint>
#include <thread>
#include <vector>

namespace {

namespace stream_v1 = beacon::stream::v1;
using beacon::worker::WorkerOutboundBatch;
using beacon::worker::WorkerOutboundQueue;
using beacon::worker::v1::WorkerIpcEnvelope;

WorkerIpcEnvelope response(std::uint64_t request_id,
                           std::uint64_t marker) {
  WorkerIpcEnvelope value;
  value.set_protocol_version(1);
  value.set_request_id(request_id);
  value.mutable_worker_completion()->set_succeeded(true);
  value.mutable_worker_completion()->set_error_code(
      beacon::worker::v1::WORKER_ERROR_CODE_NONE);
  value.set_session_id("response-" + std::to_string(marker));
  return value;
}

void worker_events_are_uncorrelated_typed_and_generation_bound() {
  stream_v1::InputStreamEnvelope input;
  input.set_protocol_version(1);
  input.set_session_id("session-a");
  input.set_sequence(7);
  input.mutable_input_batch()->add_events()->mutable_keyboard()->set_scan_code(
      30);
  stream_v1::FeedbackStreamEnvelope feedback;
  feedback.set_protocol_version(1);
  feedback.set_session_id("session-a");
  feedback.set_sequence(9);
  feedback.mutable_queue_depth()->set_queued_access_units(2);

  const auto authenticated = beacon::worker::make_transport_authenticated_event(
      "session-a", 4, 1232);
  const auto input_event =
      beacon::worker::make_input_received_event(4, input);
  const auto feedback_event =
      beacon::worker::make_feedback_received_event(4, feedback);
  const auto media = beacon::worker::make_media_evidence_event(
      "session-a", 4, 1, 1'000'000, 44);
  const auto disconnected =
      beacon::worker::make_transport_disconnected_event("session-a", 4);

  for (const auto *event :
       {&authenticated, &input_event, &feedback_event, &media, &disconnected}) {
    BEACON_TEST_REQUIRE(event->protocol_version() == 1);
    BEACON_TEST_REQUIRE(event->request_id() == 0);
    BEACON_TEST_REQUIRE(event->session_id() == "session-a");
  }
  BEACON_TEST_REQUIRE(
      authenticated.transport_authenticated().session_generation() == 4);
  BEACON_TEST_REQUIRE(
      input_event.input_received().session_generation() == 4);
  BEACON_TEST_REQUIRE(
      input_event.input_received().input().SerializeAsString() ==
      input.SerializeAsString());
  BEACON_TEST_REQUIRE(
      feedback_event.feedback_received().feedback().SerializeAsString() ==
      feedback.SerializeAsString());
  BEACON_TEST_REQUIRE(media.media_evidence().sequence() == 1);
  BEACON_TEST_REQUIRE(
      disconnected.transport_disconnected().session_generation() == 4);
}

void response_batches_remain_contiguous_with_concurrent_event_producers() {
  WorkerOutboundQueue queue;
  std::thread first([&queue] {
    BEACON_TEST_REQUIRE(queue.enqueue(
        WorkerOutboundBatch{response(11, 1), response(11, 2)}));
  });
  std::thread second([&queue] {
    BEACON_TEST_REQUIRE(queue.enqueue(WorkerOutboundBatch{
        beacon::worker::make_transport_authenticated_event("session-a", 1,
                                                            1232)}));
  });
  first.join();
  second.join();
  queue.close();

  std::vector<WorkerOutboundBatch> batches;
  while (auto batch = queue.wait_pop()) {
    batches.push_back(std::move(*batch));
  }

  BEACON_TEST_REQUIRE(batches.size() == 2);
  const auto response_batch =
      std::ranges::find_if(batches, [](const WorkerOutboundBatch &batch) {
        return batch.size() == 2;
      });
  BEACON_TEST_REQUIRE(response_batch != batches.end());
  BEACON_TEST_REQUIRE((*response_batch)[0].request_id() == 11);
  BEACON_TEST_REQUIRE((*response_batch)[1].request_id() == 11);
  BEACON_TEST_REQUIRE((*response_batch)[0].session_id() == "response-1");
  BEACON_TEST_REQUIRE((*response_batch)[1].session_id() == "response-2");
  BEACON_TEST_REQUIRE(!queue.enqueue(WorkerOutboundBatch{response(12, 3)}));
}

} // namespace

int main() {
  worker_events_are_uncorrelated_typed_and_generation_bound();
  response_batches_remain_contiguous_with_concurrent_event_producers();
  return 0;
}
