#include "beacon/worker/worker_host.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "worker_ipc.pb.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <utility>
#include <vector>

namespace {

using beacon::stream::TransportPacket;
using beacon::stream::TransportSendResult;
using beacon::worker::IWorkerMediaTransport;
using beacon::worker::WorkerHost;
using beacon::worker::AuthorizedQuicTicketStore;
using beacon::worker::v1::WorkerIpcEnvelope;

class RecordingTransport final : public IWorkerMediaTransport {
 public:
  bool configure_listener(std::string_view address, std::uint16_t port) override {
    listen_address = address;
    listen_port = port;
    return true;
  }

  std::uint16_t local_port() const noexcept override {
    return selected_port == 0 ? listen_port : selected_port;
  }

  bool open_connection() override {
    ++open_count;
    if (!open_result) {
      return false;
    }
    open = true;
    return true;
  }

  void close_connection() noexcept override {
    if (open) {
      open = false;
      ++close_count;
    }
  }

  TransportSendResult send(TransportPacket packet) override {
    packets.push_back(std::move(packet));
    return TransportSendResult::accepted;
  }

  void shutdown() noexcept override {
    close_connection();
    ++shutdown_count;
  }

  bool open{};
  bool open_result{true};
  std::string listen_address;
  std::uint16_t listen_port{};
  std::uint16_t selected_port{};
  std::size_t open_count{};
  std::size_t close_count{};
  std::size_t shutdown_count{};
  std::vector<TransportPacket> packets;
};

const WorkerIpcEnvelope& completion(const std::vector<WorkerIpcEnvelope>& responses) {
  const auto response = std::ranges::find_if(responses, [](const WorkerIpcEnvelope& value) {
    return value.body_case() == WorkerIpcEnvelope::kWorkerCompletion;
  });
  BEACON_TEST_REQUIRE(response != responses.end());
  return *response;
}

WorkerIpcEnvelope command(std::uint64_t request_id, const char* session_id) {
  WorkerIpcEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_request_id(request_id);
  envelope.set_session_id(session_id);
  return envelope;
}

void hello_and_ready_are_typed_and_instance_bound() {
  RecordingTransport transport;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}, std::byte{2}, std::byte{3}}, 42, transport, tickets);

  const auto hello = host.hello();
  const auto ready = host.ready();

  BEACON_TEST_REQUIRE(hello.protocol_version() == 1);
  BEACON_TEST_REQUIRE(hello.worker_hello().process_id() == 42);
  BEACON_TEST_REQUIRE(hello.worker_hello().worker_instance_id() == "\x01\x02\x03");
  BEACON_TEST_REQUIRE(ready.protocol_version() == 1);
  BEACON_TEST_REQUIRE(ready.worker_ready().worker_instance_id() == "\x01\x02\x03");
}

void unsupported_versions_receive_one_correlated_failure() {
  RecordingTransport transport;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets);
  auto request = command(17, "session-a");
  request.set_protocol_version(2);
  request.mutable_prepare_session();

  const auto responses = host.dispatch(request);
  const auto& result = completion(responses);

  BEACON_TEST_REQUIRE(result.request_id() == 17);
  BEACON_TEST_REQUIRE(result.session_id() == "session-a");
  BEACON_TEST_REQUIRE(!result.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(
      result.worker_completion().error_code() ==
      beacon::worker::v1::WORKER_ERROR_CODE_UNSUPPORTED_VERSION);
  BEACON_TEST_REQUIRE(std::ranges::count_if(responses, [](const WorkerIpcEnvelope& value) {
                        return value.body_case() == WorkerIpcEnvelope::kWorkerCompletion;
                      }) == 1);
}

void prepared_session_reports_selected_listener_port_without_pre_auth_media() {
  RecordingTransport transport;
  transport.selected_port = 45999;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets);

  auto prepare = command(20, "session-a");
  auto* plan = prepare.mutable_prepare_session();
  plan->set_display_target("display-a");
  plan->set_video_codec(beacon::worker::v1::WORKER_VIDEO_CODEC_H264);
  plan->set_width(2560);
  plan->set_height(1600);
  plan->set_frames_per_second_numerator(120);
  plan->set_frames_per_second_denominator(1);
  plan->set_dynamic_range(beacon::worker::v1::WORKER_DYNAMIC_RANGE_SDR);
  BEACON_TEST_REQUIRE(completion(host.dispatch(prepare)).worker_completion().succeeded());

  auto start = command(21, "session-a");
  start.mutable_start_media()->set_listen_port(0);
  const auto responses = host.dispatch(start);

  BEACON_TEST_REQUIRE(completion(responses).request_id() == 21);
  BEACON_TEST_REQUIRE(completion(responses).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(transport.open_count == 1);
  BEACON_TEST_REQUIRE(transport.listen_address.empty());
  BEACON_TEST_REQUIRE(transport.listen_port == 0);
  BEACON_TEST_REQUIRE(transport.packets.empty());
  BEACON_TEST_REQUIRE(std::ranges::count_if(responses, [](const WorkerIpcEnvelope& value) {
                        return value.body_case() ==
                               WorkerIpcEnvelope::kWorkerTransportReady;
                      }) == 1);
  BEACON_TEST_REQUIRE(std::ranges::any_of(responses, [](const WorkerIpcEnvelope& value) {
    return value.body_case() == WorkerIpcEnvelope::kWorkerTransportReady &&
           value.worker_transport_ready().listener_port() == 45999;
  }));
  BEACON_TEST_REQUIRE(std::ranges::any_of(responses, [](const WorkerIpcEnvelope& value) {
    return value.body_case() == WorkerIpcEnvelope::kMediaMetrics &&
           value.media_metrics().encoded_frames() == 0;
  }));
}

void failed_listener_open_emits_no_transport_ready_event() {
  RecordingTransport transport;
  transport.open_result = false;
  transport.selected_port = 45999;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets);

  auto prepare = command(22, "session-a");
  auto* plan = prepare.mutable_prepare_session();
  plan->set_display_target("display-a");
  plan->set_video_codec(beacon::worker::v1::WORKER_VIDEO_CODEC_H264);
  plan->set_width(2560);
  plan->set_height(1600);
  plan->set_frames_per_second_numerator(120);
  plan->set_frames_per_second_denominator(1);
  plan->set_dynamic_range(beacon::worker::v1::WORKER_DYNAMIC_RANGE_SDR);
  BEACON_TEST_REQUIRE(completion(host.dispatch(prepare)).worker_completion().succeeded());

  auto start = command(23, "session-a");
  start.mutable_start_media()->set_listen_port(0);
  const auto responses = host.dispatch(start);

  BEACON_TEST_REQUIRE(!completion(responses).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(std::ranges::none_of(responses, [](const WorkerIpcEnvelope& value) {
    return value.body_case() == WorkerIpcEnvelope::kWorkerTransportReady;
  }));
}

void ticket_authorization_is_hash_only_and_worker_bound() {
  RecordingTransport transport;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}, std::byte{2}}, 42, transport, tickets);
  auto authorize = command(25, "session-a");
  auto* ticket = authorize.mutable_authorize_ticket();
  ticket->set_ticket_hash(std::string(32, '\x5a'));
  ticket->set_client_id("z-fold-7");
  ticket->set_plan_revision(8);
  ticket->set_expires_at_unix_ms(1'800'000'000'000ULL);
  ticket->set_worker_instance_id("\x01\x02");

  const auto accepted = host.dispatch(authorize);
  auto revoke = command(26, "session-a");
  revoke.mutable_revoke_ticket()->set_ticket_hash(std::string(32, '\x5a'));
  const auto revoked = host.dispatch(revoke);
  ticket->set_worker_instance_id("\x09");
  const auto rejected = host.dispatch(authorize);

  BEACON_TEST_REQUIRE(completion(accepted).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(completion(revoked).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(host.authorized_ticket_count() == 0);
  BEACON_TEST_REQUIRE(!completion(rejected).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(host.authorized_ticket_count() == 0);
}

void explicit_shutdown_is_acknowledged_and_releases_once() {
  RecordingTransport transport;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets);
  auto shutdown = command(30, "");
  shutdown.mutable_shutdown_worker();

  const auto first = host.dispatch(shutdown);
  const auto second = host.dispatch(shutdown);

  BEACON_TEST_REQUIRE(completion(first).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(!completion(second).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(host.shutdown_requested());
  BEACON_TEST_REQUIRE(transport.shutdown_count == 1);
}

}  // namespace

int main() {
  hello_and_ready_are_typed_and_instance_bound();
  unsupported_versions_receive_one_correlated_failure();
  prepared_session_reports_selected_listener_port_without_pre_auth_media();
  failed_listener_open_emits_no_transport_ready_event();
  ticket_authorization_is_hash_only_and_worker_bound();
  explicit_shutdown_is_acknowledged_and_releases_once();
  return 0;
}
