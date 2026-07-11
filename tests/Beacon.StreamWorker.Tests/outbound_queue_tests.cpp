#include "beacon/worker/outbound_queue.h"
#include "beacon/worker/worker_events.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "stream_control.pb.h"

#include <cstdint>
#include <latch>
#include <thread>
#include <vector>

namespace {

namespace stream_v1 = beacon::stream::v1;
using beacon::worker::WorkerOutboundBatch;
using beacon::worker::WorkerOutboundBatchKind;
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
  while (auto item = queue.wait_pop()) {
    batches.push_back(std::move(item->batch));
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

void terminal_batch_remains_open_until_the_writer_closes_after_progress() {
  WorkerOutboundQueue queue;
  WorkerOutboundBatch terminal{response(30, 1), response(30, 2)};
  stream_v1::InputStreamEnvelope input;
  input.set_protocol_version(1);
  input.set_session_id("session-a");
  input.set_sequence(1);
  input.mutable_input_batch()->add_events()->mutable_keyboard()->set_scan_code(
      30);

  BEACON_TEST_REQUIRE(queue.enqueue(WorkerOutboundBatch{
      beacon::worker::make_transport_disconnected_event("session-a", 4)}));
  BEACON_TEST_REQUIRE(queue.enqueue(WorkerOutboundBatch{
      beacon::worker::make_input_received_event(4, input)}));
  BEACON_TEST_REQUIRE(queue.enqueue_terminal(std::move(terminal)));
  BEACON_TEST_REQUIRE(!queue.closed());
  BEACON_TEST_REQUIRE(!queue.enqueue(WorkerOutboundBatch{
      beacon::worker::make_media_evidence_event("session-a", 4, 1,
                                                1'000'000, 44)}));

  auto disconnected = queue.wait_pop();
  auto received_input = queue.wait_pop();
  auto completion = queue.wait_pop();
  BEACON_TEST_REQUIRE(disconnected.has_value());
  BEACON_TEST_REQUIRE(received_input.has_value());
  BEACON_TEST_REQUIRE(completion.has_value());
  BEACON_TEST_REQUIRE(disconnected->kind == WorkerOutboundBatchKind::regular);
  BEACON_TEST_REQUIRE(
      disconnected->batch[0].body_case() ==
      WorkerIpcEnvelope::kTransportDisconnected);
  BEACON_TEST_REQUIRE(received_input->kind ==
                      WorkerOutboundBatchKind::regular);
  BEACON_TEST_REQUIRE(received_input->batch[0].body_case() ==
                      WorkerIpcEnvelope::kInputReceived);
  BEACON_TEST_REQUIRE(
      received_input->batch[0].input_received().input().sequence() == 1);
  BEACON_TEST_REQUIRE(completion->kind == WorkerOutboundBatchKind::terminal);
  BEACON_TEST_REQUIRE(completion->batch.size() == 2);
  BEACON_TEST_REQUIRE(completion->batch[0].request_id() == 30);
  BEACON_TEST_REQUIRE(completion->batch[1].request_id() == 30);

  queue.close();
  BEACON_TEST_REQUIRE(queue.closed());
  BEACON_TEST_REQUIRE(!queue.wait_pop().has_value());
}

void command_response_progresses_contiguously_through_event_backlog() {
  WorkerOutboundQueue queue;
  std::latch first_half_queued{1};
  std::latch continue_producer{1};
  std::thread producer([&] {
    for (std::uint64_t index = 0; index < 64; ++index) {
      BEACON_TEST_REQUIRE(queue.enqueue(WorkerOutboundBatch{
          beacon::worker::make_media_evidence_event(
              "session-a", 1, index + 1, 1'000'000, 44)}));
    }
    first_half_queued.count_down();
    continue_producer.wait();
    for (std::uint64_t index = 64; index < 128; ++index) {
      BEACON_TEST_REQUIRE(queue.enqueue(WorkerOutboundBatch{
          beacon::worker::make_media_evidence_event(
              "session-a", 1, index + 1, 1'000'000, 44)}));
    }
  });

  first_half_queued.wait();
  BEACON_TEST_REQUIRE(queue.enqueue(
      WorkerOutboundBatch{response(40, 1), response(40, 2)}));
  continue_producer.count_down();
  producer.join();
  queue.close();

  std::size_t position = 0;
  std::size_t response_position = 0;
  std::size_t event_count = 0;
  while (auto item = queue.wait_pop()) {
    if (item->batch.front().request_id() == 40) {
      response_position = position;
      BEACON_TEST_REQUIRE(item->batch.size() == 2);
      BEACON_TEST_REQUIRE(item->batch[0].session_id() == "response-1");
      BEACON_TEST_REQUIRE(item->batch[1].session_id() == "response-2");
    } else {
      ++event_count;
    }
    ++position;
  }

  BEACON_TEST_REQUIRE(response_position == 64);
  BEACON_TEST_REQUIRE(event_count == 128);
}

} // namespace

int main() {
  worker_events_are_uncorrelated_typed_and_generation_bound();
  response_batches_remain_contiguous_with_concurrent_event_producers();
  terminal_batch_remains_open_until_the_writer_closes_after_progress();
  command_response_progresses_contiguously_through_event_backlog();
  return 0;
}
