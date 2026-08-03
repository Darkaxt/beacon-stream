#include "hosted_benchmark_worker_channel.h"

#include "beacon/worker/outbound_queue.h"
#include "beacon/worker/quic_listener.h"
#include "beacon/worker/quic_ticket_store.h"
#include "beacon/worker/video/worker_video_capabilities.h"
#include "beacon/worker/video/worker_video_pipeline.h"
#include "beacon/worker/worker_host.h"

#include <openssl/rand.h>

#include <signal.h>
#include <unistd.h>

#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <filesystem>
#include <string_view>
#include <vector>

namespace {

constexpr std::uint32_t kHostedVideoUnavailableCode{1};
constexpr std::uint32_t kHostedAudioUnavailableCode{1};

class NoVideoPipeline final
    : public beacon::worker::video::IWorkerVideoPipeline {
public:
  bool prepare(const beacon::worker::video::WorkerVideoPlan &) override {
    return false;
  }
  void handle_media_event(const beacon::worker::QuicMediaEvent &) override {}
  bool request_idr() override { return false; }
  void reset() noexcept override {}
};

class NoAudioPipeline final
    : public beacon::worker::audio::IWorkerAudioPipeline {
public:
  bool prepare(const beacon::worker::audio::WorkerAudioPlan &) override {
    return false;
  }
  void handle_media_event(const beacon::worker::QuicMediaEvent &) override {}
  void reset() noexcept override {}
};

std::vector<std::byte> create_instance_id() {
  std::vector<std::byte> result(16);
  if (RAND_bytes(reinterpret_cast<unsigned char *>(result.data()),
                 static_cast<int>(result.size())) != 1) {
    result.clear();
  }
  return result;
}

void write_marker(const char *marker) noexcept {
  std::fprintf(stderr, "%s\n", marker);
  std::fflush(stderr);
}

void write_failure_marker(int category) noexcept {
  std::fprintf(stderr, "BEACON_HOSTED_WORKER_FAILURE %d\n", category);
  std::fflush(stderr);
}

int failure_code(beacon::testing::HostedWorkerControlResult result) noexcept {
  switch (result) {
  case beacon::testing::HostedWorkerControlResult::clean_shutdown:
  case beacon::testing::HostedWorkerControlResult::input_closed:
    return 0;
  case beacon::testing::HostedWorkerControlResult::read_failure:
    return 3;
  case beacon::testing::HostedWorkerControlResult::write_failure:
    return 4;
  case beacon::testing::HostedWorkerControlResult::outbound_failure:
    return 5;
  }
  return 6;
}

} // namespace

int main(int argument_count, char **arguments) {
  if (argument_count != 3 || std::string_view(arguments[1]) != "--identity" ||
      std::string_view(arguments[2]).empty()) {
    write_failure_marker(1);
    return 1;
  }
  static_cast<void>(::signal(SIGPIPE, SIG_IGN));

  try {
    auto instance_id = create_instance_id();
    if (instance_id.empty()) {
      write_failure_marker(2);
      return 2;
    }

    beacon::testing::HostedBenchmarkWorkerChannel channel(STDIN_FILENO,
                                                          STDOUT_FILENO);
    beacon::worker::WorkerOutboundQueue outbound;
    beacon::worker::AuthorizedQuicTicketStore tickets;
    NoVideoPipeline video_pipeline;
    NoAudioPipeline audio_pipeline;
    beacon::worker::QuicListener transport(std::filesystem::path{arguments[2]},
                                           tickets);
    transport.set_event_sink(
        [&outbound, &channel](beacon::worker::v1::WorkerIpcEnvelope event) {
          std::vector<beacon::worker::v1::WorkerIpcEnvelope> batch;
          batch.push_back(std::move(event));
          if (outbound.enqueue(std::move(batch)) !=
              beacon::worker::WorkerOutboundEnqueueResult::accepted) {
            outbound.close();
            channel.cancel_read();
          }
        });
    transport.set_media_event_sink([&video_pipeline, &audio_pipeline](
                                       beacon::worker::QuicMediaEvent event) {
      video_pipeline.handle_media_event(event);
      audio_pipeline.handle_media_event(event);
    });
    beacon::worker::WorkerHost host(
        std::move(instance_id), static_cast<std::uint32_t>(::getpid()),
        transport, tickets, video_pipeline, audio_pipeline,
        {.available = false,
         .unavailable_boundary =
             beacon::worker::video::ProductionVideoCapabilityBoundary::encoder,
         .unavailable_code = kHostedVideoUnavailableCode},
        {.available = false,
         .unavailable_boundary =
             beacon::worker::audio::ProductionAudioCapabilityBoundary::capture,
         .unavailable_code = kHostedAudioUnavailableCode});

    write_marker("BEACON_HOSTED_WORKER_READY");
    const auto result =
        beacon::testing::run_hosted_worker_control(channel, host, outbound);
    if (!host.shutdown_requested()) {
      video_pipeline.reset();
      audio_pipeline.reset();
      transport.shutdown();
    }
    const auto code = failure_code(result);
    if (code == 0) {
      write_marker("BEACON_HOSTED_WORKER_STOPPED");
    } else {
      write_failure_marker(code);
    }
    return code;
  } catch (...) {
    write_failure_marker(6);
    return 6;
  }
}
