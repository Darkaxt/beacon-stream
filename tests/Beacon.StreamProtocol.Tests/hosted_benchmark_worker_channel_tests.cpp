#include "hosted_benchmark_worker_channel.h"

#include "beacon/worker/outbound_queue.h"
#include "beacon/worker/quic_listener.h"
#include "beacon/worker/video/worker_video_pipeline.h"
#include "beacon/worker/worker_host.h"
#include "test_failure.h"

#include <unistd.h>
#include <sys/wait.h>

#include <array>
#include <cerrno>
#include <cstddef>
#include <cstdint>
#include <latch>
#include <span>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace {

namespace worker_v1 = beacon::worker::v1;
using beacon::testing::HostedBenchmarkWorkerChannel;
using beacon::testing::HostedWorkerChannelReadStatus;
using beacon::testing::HostedWorkerControlResult;

class UniqueFd {
public:
  UniqueFd() = default;
  explicit UniqueFd(int value) noexcept : value_(value) {}
  ~UniqueFd() { reset(); }

  UniqueFd(const UniqueFd &) = delete;
  UniqueFd &operator=(const UniqueFd &) = delete;

  UniqueFd(UniqueFd &&other) noexcept
      : value_(std::exchange(other.value_, -1)) {}
  UniqueFd &operator=(UniqueFd &&other) noexcept {
    if (this != &other) {
      reset(std::exchange(other.value_, -1));
    }
    return *this;
  }

  [[nodiscard]] int get() const noexcept { return value_; }
  void reset(int value = -1) noexcept {
    if (value_ >= 0) {
      ::close(value_);
    }
    value_ = value;
  }

private:
  int value_{-1};
};

struct Pipe {
  UniqueFd read;
  UniqueFd write;
};

Pipe make_pipe() {
  std::array<int, 2> descriptors{};
  BEACON_TEST_REQUIRE(::pipe(descriptors.data()) == 0);
  return {.read = UniqueFd{descriptors[0]}, .write = UniqueFd{descriptors[1]}};
}

bool write_exact(int descriptor, std::span<const std::byte> bytes) {
  std::size_t offset = 0;
  while (offset < bytes.size()) {
    const auto written = ::write(descriptor, bytes.data() + offset,
                                 bytes.size() - offset);
    if (written <= 0) {
      return false;
    }
    offset += static_cast<std::size_t>(written);
  }
  return true;
}

bool read_exact(int descriptor, std::span<std::byte> bytes) {
  std::size_t offset = 0;
  while (offset < bytes.size()) {
    const auto received =
        ::read(descriptor, bytes.data() + offset, bytes.size() - offset);
    if (received <= 0) {
      return false;
    }
    offset += static_cast<std::size_t>(received);
  }
  return true;
}

std::vector<std::byte> frame(const worker_v1::WorkerIpcEnvelope &envelope) {
  const auto body_size = envelope.ByteSizeLong();
  std::vector<std::byte> result(4U + body_size);
  const auto size = static_cast<std::uint32_t>(body_size);
  result[0] = static_cast<std::byte>((size >> 24U) & 0xffU);
  result[1] = static_cast<std::byte>((size >> 16U) & 0xffU);
  result[2] = static_cast<std::byte>((size >> 8U) & 0xffU);
  result[3] = static_cast<std::byte>(size & 0xffU);
  BEACON_TEST_REQUIRE(envelope.SerializeToArray(
      result.data() + 4, static_cast<int>(body_size)));
  return result;
}

worker_v1::WorkerIpcEnvelope read_frame(int descriptor) {
  std::array<std::byte, 4> prefix{};
  BEACON_TEST_REQUIRE(read_exact(descriptor, prefix));
  const auto size = (std::to_integer<std::uint32_t>(prefix[0]) << 24U) |
                    (std::to_integer<std::uint32_t>(prefix[1]) << 16U) |
                    (std::to_integer<std::uint32_t>(prefix[2]) << 8U) |
                    std::to_integer<std::uint32_t>(prefix[3]);
  std::vector<std::byte> body(size);
  BEACON_TEST_REQUIRE(read_exact(descriptor, body));
  worker_v1::WorkerIpcEnvelope result;
  BEACON_TEST_REQUIRE(result.ParseFromArray(body.data(), static_cast<int>(size)));
  return result;
}

std::string read_to_end(int descriptor) {
  std::string result;
  std::array<char, 256> buffer{};
  for (;;) {
    const auto received = ::read(descriptor, buffer.data(), buffer.size());
    if (received < 0 && errno == EINTR) {
      continue;
    }
    if (received <= 0) {
      return result;
    }
    result.append(buffer.data(), static_cast<std::size_t>(received));
  }
}

class RecordingTransport final : public beacon::worker::IWorkerMediaTransport {
public:
  bool configure_listener(std::string_view, std::uint16_t) override {
    return true;
  }
  std::uint16_t local_port() const noexcept override { return 45'000; }
  bool open_connection() override { return true; }
  void close_connection() noexcept override { ++close_calls; }
  void request_active_disconnect() noexcept override {}
  beacon::stream::TransportSendResult
  send_for_generation(beacon::stream::TransportPacket,
                      std::uint64_t) override {
    return beacon::stream::TransportSendResult::accepted;
  }
  void shutdown() noexcept override { ++shutdown_calls; }

  int close_calls{};
  int shutdown_calls{};
};

class NoVideoPipeline final
    : public beacon::worker::video::IWorkerVideoPipeline {
public:
  bool prepare(const beacon::worker::video::WorkerVideoPlan &) override {
    return false;
  }
  void handle_media_event(const beacon::worker::QuicMediaEvent &) override {}
  bool request_idr() override { return false; }
  void reset() noexcept override { ++reset_calls; }

  int reset_calls{};
};

void exact_big_endian_frames_round_trip_over_file_descriptors() {
  auto input = make_pipe();
  auto output = make_pipe();
  HostedBenchmarkWorkerChannel channel(input.read.get(), output.write.get());
  worker_v1::WorkerIpcEnvelope source;
  source.set_protocol_version(1);
  source.set_request_id(17);
  source.set_session_id("session-a");
  source.mutable_worker_health()->set_active_sessions(2);
  const auto inbound = frame(source);
  BEACON_TEST_REQUIRE(write_exact(input.write.get(), inbound));

  worker_v1::WorkerIpcEnvelope decoded;
  BEACON_TEST_REQUIRE(channel.read(decoded) ==
                      HostedWorkerChannelReadStatus::success);
  BEACON_TEST_REQUIRE(decoded.request_id() == 17);
  BEACON_TEST_REQUIRE(channel.write(source));

  std::array<std::byte, 4> prefix{};
  BEACON_TEST_REQUIRE(read_exact(output.read.get(), prefix));
  BEACON_TEST_REQUIRE(prefix[0] == inbound[0]);
  BEACON_TEST_REQUIRE(prefix[1] == inbound[1]);
  BEACON_TEST_REQUIRE(prefix[2] == inbound[2]);
  BEACON_TEST_REQUIRE(prefix[3] == inbound[3]);
  std::vector<std::byte> body(inbound.size() - 4U);
  BEACON_TEST_REQUIRE(read_exact(output.read.get(), body));
  worker_v1::WorkerIpcEnvelope round_trip;
  BEACON_TEST_REQUIRE(round_trip.ParseFromArray(
      body.data(), static_cast<int>(body.size())));
  BEACON_TEST_REQUIRE(round_trip.request_id() == 17);
}

void malformed_oversized_and_invalid_frames_are_rejected() {
  {
    auto input = make_pipe();
    auto output = make_pipe();
    HostedBenchmarkWorkerChannel channel(input.read.get(), output.write.get());
    constexpr std::array truncated{std::byte{0}, std::byte{0}};
    BEACON_TEST_REQUIRE(write_exact(input.write.get(), truncated));
    input.write.reset();
    worker_v1::WorkerIpcEnvelope envelope;
    BEACON_TEST_REQUIRE(channel.read(envelope) ==
                        HostedWorkerChannelReadStatus::truncated_frame);
  }
  {
    auto input = make_pipe();
    auto output = make_pipe();
    HostedBenchmarkWorkerChannel channel(input.read.get(), output.write.get());
    constexpr std::array oversized{std::byte{0}, std::byte{0x10},
                                   std::byte{0}, std::byte{1}};
    BEACON_TEST_REQUIRE(write_exact(input.write.get(), oversized));
    worker_v1::WorkerIpcEnvelope envelope;
    BEACON_TEST_REQUIRE(channel.read(envelope) ==
                        HostedWorkerChannelReadStatus::message_too_large);
  }
  {
    auto input = make_pipe();
    auto output = make_pipe();
    HostedBenchmarkWorkerChannel channel(input.read.get(), output.write.get());
    constexpr std::array invalid{std::byte{0}, std::byte{0}, std::byte{0},
                                 std::byte{1}, std::byte{0xff}};
    BEACON_TEST_REQUIRE(write_exact(input.write.get(), invalid));
    worker_v1::WorkerIpcEnvelope envelope;
    BEACON_TEST_REQUIRE(channel.read(envelope) ==
                        HostedWorkerChannelReadStatus::invalid_protobuf);
  }
}

void clean_eof_is_distinct_from_a_truncated_frame() {
  auto input = make_pipe();
  auto output = make_pipe();
  HostedBenchmarkWorkerChannel channel(input.read.get(), output.write.get());
  input.write.reset();
  worker_v1::WorkerIpcEnvelope envelope;
  BEACON_TEST_REQUIRE(channel.read(envelope) ==
                      HostedWorkerChannelReadStatus::end_of_stream);
}

void cancellation_wakes_a_blocked_reader_without_a_deadline() {
  auto input = make_pipe();
  auto output = make_pipe();
  HostedBenchmarkWorkerChannel channel(input.read.get(), output.write.get());
  std::latch reader_started{1};
  auto status = HostedWorkerChannelReadStatus::success;
  std::thread reader([&] {
    reader_started.count_down();
    worker_v1::WorkerIpcEnvelope envelope;
    status = channel.read(envelope);
  });
  reader_started.wait();

  channel.cancel_read();
  reader.join();

  BEACON_TEST_REQUIRE(status == HostedWorkerChannelReadStatus::canceled);
}

void typed_shutdown_writes_terminal_completion_and_stops_the_worker() {
  auto input = make_pipe();
  auto output = make_pipe();
  HostedBenchmarkWorkerChannel channel(input.read.get(), output.write.get());
  worker_v1::WorkerIpcEnvelope shutdown;
  shutdown.set_protocol_version(1);
  shutdown.set_request_id(91);
  shutdown.mutable_shutdown_worker();
  const auto shutdown_frame = frame(shutdown);
  BEACON_TEST_REQUIRE(write_exact(input.write.get(), shutdown_frame));

  RecordingTransport transport;
  NoVideoPipeline video_pipeline;
  beacon::worker::AuthorizedQuicTicketStore tickets;
  beacon::worker::WorkerHost host(
      {std::byte{1}, std::byte{2}}, 42, transport, tickets, video_pipeline,
      {.available = false,
       .unavailable_boundary =
           beacon::worker::video::ProductionVideoCapabilityBoundary::encoder,
       .unavailable_code = 1});
  beacon::worker::WorkerOutboundQueue outbound;

  const auto result = beacon::testing::run_hosted_worker_control(
      channel, host, outbound);

  BEACON_TEST_REQUIRE(result == HostedWorkerControlResult::clean_shutdown);
  BEACON_TEST_REQUIRE(host.shutdown_requested());
  BEACON_TEST_REQUIRE(transport.shutdown_calls == 1);
  BEACON_TEST_REQUIRE(video_pipeline.reset_calls == 1);
  const auto hello = read_frame(output.read.get());
  const auto capabilities = read_frame(output.read.get());
  const auto ready = read_frame(output.read.get());
  const auto completion = read_frame(output.read.get());
  BEACON_TEST_REQUIRE(hello.has_worker_hello());
  BEACON_TEST_REQUIRE(capabilities.has_worker_capabilities());
  BEACON_TEST_REQUIRE(!capabilities.worker_capabilities().video_available());
  BEACON_TEST_REQUIRE(ready.has_worker_ready());
  BEACON_TEST_REQUIRE(completion.request_id() == 91);
  BEACON_TEST_REQUIRE(completion.worker_completion().succeeded());
}

void hosted_worker_process_publishes_fixed_markers_and_exits_cleanly() {
  auto input = make_pipe();
  auto output = make_pipe();
  auto diagnostics = make_pipe();
  const auto process = ::fork();
  BEACON_TEST_REQUIRE(process >= 0);
  if (process == 0) {
    if (::dup2(input.read.get(), STDIN_FILENO) < 0 ||
        ::dup2(output.write.get(), STDOUT_FILENO) < 0 ||
        ::dup2(diagnostics.write.get(), STDERR_FILENO) < 0) {
      ::_exit(126);
    }
    ::close(input.read.get());
    ::close(input.write.get());
    ::close(output.read.get());
    ::close(output.write.get());
    ::close(diagnostics.read.get());
    ::close(diagnostics.write.get());
    ::execl(BEACON_HOSTED_WORKER_PATH, BEACON_HOSTED_WORKER_PATH,
            "--identity", "/tmp/beacon-hosted-worker-unused.pfx",
            static_cast<char *>(nullptr));
    ::_exit(127);
  }

  input.read.reset();
  output.write.reset();
  diagnostics.write.reset();
  const auto hello = read_frame(output.read.get());
  const auto capabilities = read_frame(output.read.get());
  const auto ready = read_frame(output.read.get());

  worker_v1::WorkerIpcEnvelope prepare_video;
  prepare_video.set_protocol_version(1);
  prepare_video.set_request_id(302);
  prepare_video.set_session_id("session-video");
  auto *video = prepare_video.mutable_prepare_session();
  video->set_display_target("virtual-display-a");
  video->set_display_device_name("display-a");
  video->set_width(2560);
  video->set_height(1600);
  video->set_frames_per_second_numerator(120);
  video->set_frames_per_second_denominator(1);
  video->set_video_codec(worker_v1::WORKER_VIDEO_CODEC_H264);
  video->set_dynamic_range(worker_v1::WORKER_DYNAMIC_RANGE_SDR);
  video->set_minimum_bitrate_kbps(10'000);
  video->set_initial_bitrate_kbps(20'000);
  video->set_maximum_bitrate_kbps(40'000);
  const auto prepare_video_frame = frame(prepare_video);
  BEACON_TEST_REQUIRE(write_exact(input.write.get(), prepare_video_frame));
  const auto video_rejection = read_frame(output.read.get());

  worker_v1::WorkerIpcEnvelope prepare_benchmark;
  prepare_benchmark.set_protocol_version(1);
  prepare_benchmark.set_request_id(303);
  prepare_benchmark.set_session_id("session-benchmark");
  auto *benchmark = prepare_benchmark.mutable_prepare_benchmark()->mutable_plan();
  benchmark->set_run_id("11111111-1111-1111-1111-111111111111");
  benchmark->set_schema_version(3);
  const std::array<std::byte, 16> run_token{
      std::byte{1}, std::byte{2}, std::byte{3}, std::byte{4},
      std::byte{5}, std::byte{6}, std::byte{7}, std::byte{8},
      std::byte{9}, std::byte{10}, std::byte{11}, std::byte{12},
      std::byte{13}, std::byte{14}, std::byte{15}, std::byte{16}};
  benchmark->set_run_token(run_token.data(), run_token.size());
  benchmark->mutable_reliable_round()->set_packet_count(8);
  benchmark->mutable_reliable_round()->set_payload_bytes(4096);
  benchmark->mutable_reliable_round()->set_measurement_interval_us(500'000);
  benchmark->mutable_datagram_round()->set_packet_count(16);
  benchmark->mutable_datagram_round()->set_payload_bytes(1000);
  benchmark->mutable_datagram_round()->set_measurement_interval_us(500'000);
  const auto prepare_benchmark_frame = frame(prepare_benchmark);
  BEACON_TEST_REQUIRE(
      write_exact(input.write.get(), prepare_benchmark_frame));
  const auto benchmark_state = read_frame(output.read.get());
  const auto benchmark_completion = read_frame(output.read.get());

  worker_v1::WorkerIpcEnvelope shutdown;
  shutdown.set_protocol_version(1);
  shutdown.set_request_id(301);
  shutdown.mutable_shutdown_worker();
  const auto shutdown_frame = frame(shutdown);
  BEACON_TEST_REQUIRE(write_exact(input.write.get(), shutdown_frame));
  input.write.reset();

  const auto completion = read_frame(output.read.get());
  const auto markers = read_to_end(diagnostics.read.get());
  int process_status = 0;
  BEACON_TEST_REQUIRE(::waitpid(process, &process_status, 0) == process);

  BEACON_TEST_REQUIRE(WIFEXITED(process_status));
  BEACON_TEST_REQUIRE(WEXITSTATUS(process_status) == 0);
  BEACON_TEST_REQUIRE(hello.has_worker_hello());
  BEACON_TEST_REQUIRE(capabilities.has_worker_capabilities());
  BEACON_TEST_REQUIRE(!capabilities.worker_capabilities().video_available());
  BEACON_TEST_REQUIRE(ready.has_worker_ready());
  BEACON_TEST_REQUIRE(video_rejection.request_id() == 302);
  BEACON_TEST_REQUIRE(!video_rejection.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(
      video_rejection.worker_completion().error_code() ==
      worker_v1::WORKER_ERROR_CODE_OPERATION_FAILED);
  BEACON_TEST_REQUIRE(benchmark_state.request_id() == 303);
  BEACON_TEST_REQUIRE(benchmark_state.session_state_changed().state() ==
                      worker_v1::WORKER_SESSION_STATE_PREPARED);
  BEACON_TEST_REQUIRE(benchmark_completion.request_id() == 303);
  BEACON_TEST_REQUIRE(benchmark_completion.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(completion.request_id() == 301);
  BEACON_TEST_REQUIRE(completion.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(markers ==
                      "BEACON_HOSTED_WORKER_READY\n"
                      "BEACON_HOSTED_WORKER_STOPPED\n");
  BEACON_TEST_REQUIRE(markers.find("unused.pfx") == std::string::npos);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    exact_big_endian_frames_round_trip_over_file_descriptors();
    malformed_oversized_and_invalid_frames_are_rejected();
    clean_eof_is_distinct_from_a_truncated_frame();
    cancellation_wakes_a_blocked_reader_without_a_deadline();
    typed_shutdown_writes_terminal_completion_and_stops_the_worker();
    hosted_worker_process_publishes_fixed_markers_and_exits_cleanly();
  });
}
