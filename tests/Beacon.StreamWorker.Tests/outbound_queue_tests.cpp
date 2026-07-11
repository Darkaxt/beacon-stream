#include "beacon/worker/outbound_queue.h"
#include "beacon/worker/worker_ipc_limits.h"
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
using beacon::worker::WorkerOutboundEnqueueResult;
using beacon::worker::WorkerOutboundQueue;
using beacon::worker::WorkerOutboundQueueLimits;
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

std::size_t serialized_bytes(const WorkerOutboundBatch &batch) {
  std::size_t result = 0;
  for (const auto &envelope : batch) {
    result += 4 + envelope.ByteSizeLong();
  }
  return result;
}

template <typename Message>
void resize_string_field_to_serialized_size(Message &message,
                                            std::string *field,
                                            std::size_t target_bytes) {
  for (std::size_t attempt = 0; attempt < 8; ++attempt) {
    const auto current = message.ByteSizeLong();
    if (current == target_bytes) {
      return;
    }
    if (current < target_bytes) {
      field->append(target_bytes - current, 'x');
    } else {
      BEACON_TEST_REQUIRE(field->size() >= current - target_bytes);
      field->resize(field->size() - (current - target_bytes));
    }
  }
  BEACON_TEST_REQUIRE(message.ByteSizeLong() == target_bytes);
}

WorkerIpcEnvelope sized_response(std::size_t target_bytes) {
  auto value = response(80, 1);
  resize_string_field_to_serialized_size(
      value, value.mutable_session_id(), target_bytes);
  return value;
}

stream_v1::InputStreamEnvelope sized_input(std::size_t target_bytes) {
  stream_v1::InputStreamEnvelope input;
  input.set_protocol_version(1);
  input.set_sequence(1);
  input.mutable_input_batch();
  resize_string_field_to_serialized_size(
      input, input.mutable_session_id(), target_bytes);
  return input;
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
    BEACON_TEST_REQUIRE(
        queue.enqueue(WorkerOutboundBatch{response(11, 1), response(11, 2)}) ==
        WorkerOutboundEnqueueResult::accepted);
  });
  std::thread second([&queue] {
    BEACON_TEST_REQUIRE(
        queue.enqueue(WorkerOutboundBatch{
            beacon::worker::make_transport_authenticated_event(
                "session-a", 1, 1232)}) ==
        WorkerOutboundEnqueueResult::accepted);
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
  BEACON_TEST_REQUIRE(
      queue.enqueue(WorkerOutboundBatch{response(12, 3)}) ==
      WorkerOutboundEnqueueResult::closed);
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

  BEACON_TEST_REQUIRE(
      queue.enqueue(WorkerOutboundBatch{
          beacon::worker::make_transport_disconnected_event(
              "session-a", 4)}) == WorkerOutboundEnqueueResult::accepted);
  BEACON_TEST_REQUIRE(
      queue.enqueue(WorkerOutboundBatch{
          beacon::worker::make_input_received_event(4, input)}) ==
      WorkerOutboundEnqueueResult::accepted);
  BEACON_TEST_REQUIRE(queue.enqueue_terminal(std::move(terminal)) ==
                      WorkerOutboundEnqueueResult::accepted);
  BEACON_TEST_REQUIRE(!queue.closed());
  BEACON_TEST_REQUIRE(
      queue.enqueue(WorkerOutboundBatch{
          beacon::worker::make_media_evidence_event(
              "session-a", 4, 1, 1'000'000, 44)}) ==
      WorkerOutboundEnqueueResult::terminal_pending);

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
      BEACON_TEST_REQUIRE(
          queue.enqueue(WorkerOutboundBatch{
              beacon::worker::make_media_evidence_event(
                  "session-a", 1, index + 1, 1'000'000, 44)}) ==
          WorkerOutboundEnqueueResult::accepted);
    }
    first_half_queued.count_down();
    continue_producer.wait();
    for (std::uint64_t index = 64; index < 128; ++index) {
      BEACON_TEST_REQUIRE(
          queue.enqueue(WorkerOutboundBatch{
              beacon::worker::make_media_evidence_event(
                  "session-a", 1, index + 1, 1'000'000, 44)}) ==
          WorkerOutboundEnqueueResult::accepted);
    }
  });

  first_half_queued.wait();
  BEACON_TEST_REQUIRE(
      queue.enqueue(WorkerOutboundBatch{response(40, 1), response(40, 2)}) ==
      WorkerOutboundEnqueueResult::accepted);
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

void count_capacity_reserves_one_atomic_terminal_batch() {
  WorkerOutboundQueue queue(WorkerOutboundQueueLimits{
      .maximum_items = 3,
      .maximum_serialized_bytes = 64 * 1024,
      .terminal_reserved_bytes = 1024,
  });
  BEACON_TEST_REQUIRE(queue.enqueue({response(50, 1)}) ==
                      WorkerOutboundEnqueueResult::accepted);
  BEACON_TEST_REQUIRE(queue.enqueue({response(51, 1)}) ==
                      WorkerOutboundEnqueueResult::accepted);
  BEACON_TEST_REQUIRE(queue.enqueue({response(52, 1)}) ==
                      WorkerOutboundEnqueueResult::count_capacity_exceeded);
  BEACON_TEST_REQUIRE(queue.enqueue_terminal({response(53, 1)}) ==
                      WorkerOutboundEnqueueResult::accepted);
}

void serialized_byte_capacity_is_exact_and_batch_atomic() {
  WorkerOutboundBatch full_batch{response(60, 1), response(60, 2)};
  WorkerOutboundBatch terminal{response(61, 1)};
  const auto full_batch_bytes = serialized_bytes(full_batch);
  const auto terminal_bytes = serialized_bytes(terminal);
  WorkerOutboundQueue queue(WorkerOutboundQueueLimits{
      .maximum_items = 4,
      .maximum_serialized_bytes = full_batch_bytes + terminal_bytes,
      .terminal_reserved_bytes = terminal_bytes,
  });

  BEACON_TEST_REQUIRE(queue.enqueue(std::move(full_batch)) ==
                      WorkerOutboundEnqueueResult::accepted);
  BEACON_TEST_REQUIRE(queue.enqueue({response(62, 1)}) ==
                      WorkerOutboundEnqueueResult::byte_capacity_exceeded);
  BEACON_TEST_REQUIRE(queue.enqueue_terminal(std::move(terminal)) ==
                      WorkerOutboundEnqueueResult::accepted);

  auto regular = queue.wait_pop();
  auto terminal_item = queue.wait_pop();
  BEACON_TEST_REQUIRE(regular && regular->batch.size() == 2);
  BEACON_TEST_REQUIRE(terminal_item &&
                      terminal_item->kind == WorkerOutboundBatchKind::terminal);
}

void oversized_batch_and_terminal_fail_without_partial_admission() {
  WorkerOutboundBatch one{response(70, 1)};
  const auto one_bytes = serialized_bytes(one);
  WorkerOutboundQueue queue(WorkerOutboundQueueLimits{
      .maximum_items = 3,
      .maximum_serialized_bytes = one_bytes * 2,
      .terminal_reserved_bytes = one_bytes,
  });
  BEACON_TEST_REQUIRE(
      queue.enqueue({response(71, 1), response(71, 2)}) ==
      WorkerOutboundEnqueueResult::byte_capacity_exceeded);
  queue.close();
  BEACON_TEST_REQUIRE(!queue.wait_pop());

  WorkerOutboundQueue terminal_too_large(WorkerOutboundQueueLimits{
      .maximum_items = 1,
      .maximum_serialized_bytes = one_bytes - 1,
      .terminal_reserved_bytes = one_bytes - 1,
  });
  BEACON_TEST_REQUIRE(terminal_too_large.enqueue_terminal(std::move(one)) ==
                      WorkerOutboundEnqueueResult::byte_capacity_exceeded);
}

void per_envelope_pipe_limit_is_exact_and_batch_atomic() {
  const auto maximum = static_cast<std::size_t>(
      beacon::worker::maximum_worker_message_bytes);
  auto exact = sized_response(maximum);
  auto oversized = sized_response(maximum + 1U);
  WorkerOutboundQueue queue(WorkerOutboundQueueLimits{
      .maximum_items = 4,
      .maximum_serialized_bytes = maximum * 3U,
      .terminal_reserved_bytes = 0,
  });

  BEACON_TEST_REQUIRE(exact.ByteSizeLong() == maximum);
  BEACON_TEST_REQUIRE(queue.enqueue({std::move(exact)}) ==
                      WorkerOutboundEnqueueResult::accepted);
  BEACON_TEST_REQUIRE(queue.enqueue({response(81, 1), std::move(oversized)}) ==
                      WorkerOutboundEnqueueResult::message_size_exceeded);

  WorkerOutboundQueue terminal_queue;
  BEACON_TEST_REQUIRE(
      terminal_queue.enqueue_terminal({sized_response(maximum + 1U)}) ==
      WorkerOutboundEnqueueResult::message_size_exceeded);
  terminal_queue.close();
  BEACON_TEST_REQUIRE(!terminal_queue.wait_pop());

  queue.close();
  const auto admitted = queue.wait_pop();
  BEACON_TEST_REQUIRE(admitted && admitted->batch.size() == 1);
  BEACON_TEST_REQUIRE(!queue.wait_pop());
}

void input_received_wrapper_overhead_is_rejected_before_pipe_write() {
  const auto maximum = static_cast<std::size_t>(
      beacon::worker::maximum_worker_message_bytes);
  auto input = sized_input(maximum);
  BEACON_TEST_REQUIRE(input.ByteSizeLong() == maximum);
  auto event = beacon::worker::make_input_received_event(1, input);
  BEACON_TEST_REQUIRE(event.ByteSizeLong() > maximum);

  WorkerOutboundQueue queue(WorkerOutboundQueueLimits{
      .maximum_items = 2,
      .maximum_serialized_bytes = event.ByteSizeLong() + 4U,
      .terminal_reserved_bytes = 0,
  });
  BEACON_TEST_REQUIRE(queue.enqueue({std::move(event)}) ==
                      WorkerOutboundEnqueueResult::message_size_exceeded);
  queue.close();
  BEACON_TEST_REQUIRE(!queue.wait_pop());
}

} // namespace

int main() {
  worker_events_are_uncorrelated_typed_and_generation_bound();
  response_batches_remain_contiguous_with_concurrent_event_producers();
  terminal_batch_remains_open_until_the_writer_closes_after_progress();
  command_response_progresses_contiguously_through_event_backlog();
  count_capacity_reserves_one_atomic_terminal_batch();
  serialized_byte_capacity_is_exact_and_batch_atomic();
  oversized_batch_and_terminal_fail_without_partial_admission();
  per_envelope_pipe_limit_is_exact_and_batch_atomic();
  input_received_wrapper_overhead_is_rejected_before_pipe_write();
  return 0;
}
