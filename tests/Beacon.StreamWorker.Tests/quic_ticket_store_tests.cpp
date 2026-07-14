#include "beacon/worker/quic_ticket_store.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <cstddef>
#include <span>
#include <string_view>
#include <utility>

namespace {

using beacon::worker::AuthorizedQuicTicket;
using beacon::worker::AuthorizedQuicTicketStore;
using QuicTicketConsumeResult =
    beacon::stream::StreamTicketAuthorizationResult;
namespace stream_v1 = beacon::stream::v1;

std::span<const std::byte> bytes(std::string_view value) {
  return {reinterpret_cast<const std::byte *>(value.data()), value.size()};
}

stream_v1::SelectedVideoMode selected_video() {
  stream_v1::SelectedVideoMode video;
  video.set_codec(stream_v1::VIDEO_CODEC_H264);
  video.set_width(2560);
  video.set_height(1600);
  video.set_frames_per_second_numerator(120);
  video.set_frames_per_second_denominator(1);
  video.set_dynamic_range(stream_v1::DYNAMIC_RANGE_SDR);
  return video;
}

stream_v1::StartBenchmark benchmark_plan() {
  stream_v1::StartBenchmark benchmark;
  benchmark.set_run_id("11111111-1111-1111-1111-111111111111");
  benchmark.set_schema_version(3);
  benchmark.set_run_token(
      "\x00\x01\x02\x03\x04\x05\x06\x07\x08\x09\x0a\x0b\x0c\x0d\x0e\x0f", 16);
  benchmark.mutable_reliable_round()->set_packet_count(4);
  benchmark.mutable_reliable_round()->set_payload_bytes(1024);
  benchmark.mutable_reliable_round()->set_measurement_interval_us(500'000);
  benchmark.mutable_datagram_round()->set_packet_count(8);
  benchmark.mutable_datagram_round()->set_payload_bytes(1000);
  benchmark.mutable_datagram_round()->set_measurement_interval_us(500'000);
  return benchmark;
}

AuthorizedQuicTicket grant(std::string_view raw_ticket) {
  AuthorizedQuicTicket ticket{
      .hash = beacon::worker::hash_stream_ticket(bytes(raw_ticket)),
      .client_id = "z-fold-7",
      .session_id = "session-a",
      .plan_revision = 8,
      .expires_at_unix_ms = 2'000,
  };
  ticket.selected_video = selected_video();
  return ticket;
}

QuicTicketConsumeResult consume(AuthorizedQuicTicketStore &store,
                                std::span<const std::byte> raw_ticket,
                                std::string_view client_id,
                                std::string_view session_id,
                                std::uint64_t plan_revision,
                                std::uint64_t now_unix_ms) {
  return store
      .authorize(raw_ticket, client_id, session_id, plan_revision,
                 now_unix_ms)
      .result;
}

void ticket_hash_is_sha256_and_deterministic() {
  const auto first = beacon::worker::hash_stream_ticket(bytes("raw-ticket"));
  const auto second = beacon::worker::hash_stream_ticket(bytes("raw-ticket"));
  const auto different =
      beacon::worker::hash_stream_ticket(bytes("different-ticket"));

  BEACON_TEST_REQUIRE(first.size() == 32);
  BEACON_TEST_REQUIRE(first == second);
  BEACON_TEST_REQUIRE(first != different);
}

void ticket_is_consumed_once_without_retaining_the_raw_secret() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-a")));

  BEACON_TEST_REQUIRE(
      consume(store, bytes("raw-ticket-a"), "z-fold-7", "session-a", 8, 1'000) ==
      QuicTicketConsumeResult::accepted);
  BEACON_TEST_REQUIRE(
      consume(store, bytes("raw-ticket-a"), "z-fold-7", "session-a", 8, 1'000) ==
      QuicTicketConsumeResult::replayed);
  BEACON_TEST_REQUIRE(consume(store, bytes("another-ticket"), "z-fold-7",
                             "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::unknown);
}

void ticket_identity_and_security_expiry_are_validated_before_consumption() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-b")));

  BEACON_TEST_REQUIRE(consume(store, bytes("raw-ticket-b"), "wrong-client",
                             "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::client_mismatch);
  BEACON_TEST_REQUIRE(consume(store, bytes("raw-ticket-b"), "z-fold-7",
                             "wrong-session", 8, 1'000) ==
                      QuicTicketConsumeResult::session_mismatch);
  BEACON_TEST_REQUIRE(
      consume(store, bytes("raw-ticket-b"), "z-fold-7", "session-a", 9, 1'000) ==
      QuicTicketConsumeResult::plan_mismatch);
  BEACON_TEST_REQUIRE(consume(store, bytes("raw-ticket-b"), "z-fold-7",
                             "session-a", 8, 2'001) ==
                      QuicTicketConsumeResult::expired);
  BEACON_TEST_REQUIRE(
      consume(store, bytes("raw-ticket-b"), "z-fold-7", "session-a", 8, 1'000) ==
      QuicTicketConsumeResult::accepted);
}

void revocation_duplicate_and_hash_length_checks_are_deterministic() {
  AuthorizedQuicTicketStore store;
  auto ticket = grant("raw-ticket-c");
  BEACON_TEST_REQUIRE(store.authorize(ticket));
  BEACON_TEST_REQUIRE(!store.authorize(ticket));
  BEACON_TEST_REQUIRE(store.size() == 1);

  store.revoke(bytes("not-a-sha256-hash"));
  BEACON_TEST_REQUIRE(store.size() == 1);
  store.revoke(ticket.hash);
  BEACON_TEST_REQUIRE(store.size() == 0);
  BEACON_TEST_REQUIRE(consume(store, bytes("raw-ticket-c"), "z-fold-7",
                             "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::unknown);
}

void tickets_authorize_exactly_one_prepared_operation() {
  AuthorizedQuicTicketStore store;

  auto missing_operation = grant("missing-operation");
  missing_operation.selected_video.reset();
  BEACON_TEST_REQUIRE(!store.authorize(std::move(missing_operation)));

  auto ambiguous_operation = grant("ambiguous-operation");
  ambiguous_operation.benchmark_plan = benchmark_plan();
  BEACON_TEST_REQUIRE(!store.authorize(std::move(ambiguous_operation)));

  auto benchmark_operation = grant("benchmark-operation");
  benchmark_operation.selected_video.reset();
  benchmark_operation.benchmark_plan = benchmark_plan();
  BEACON_TEST_REQUIRE(store.authorize(std::move(benchmark_operation)));
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    ticket_hash_is_sha256_and_deterministic();
    ticket_is_consumed_once_without_retaining_the_raw_secret();
    ticket_identity_and_security_expiry_are_validated_before_consumption();
    revocation_duplicate_and_hash_length_checks_are_deterministic();
    tickets_authorize_exactly_one_prepared_operation();
  });
}
