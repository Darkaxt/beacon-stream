#include "hosted_benchmark_worker_channel.h"
#include "hosted_video_generation.h"
#include "access_unit_vector.h"

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
#include <memory>
#include <mutex>
#include <optional>
#include <string>
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

class AcceptingAudioPipeline final
    : public beacon::worker::audio::IWorkerAudioPipeline {
public:
  bool prepare(const beacon::worker::audio::WorkerAudioPlan &plan) override {
    std::lock_guard lock{mutex_};
    prepared_ = beacon::worker::audio::valid_worker_audio_plan(plan);
    return prepared_;
  }
  void handle_media_event(const beacon::worker::QuicMediaEvent &) override {}
  void reset() noexcept override {
    std::lock_guard lock{mutex_};
    prepared_ = false;
  }

private:
  std::mutex mutex_;
  bool prepared_{};
};

struct HostedWorkerArguments {
  std::filesystem::path identity;
  std::optional<std::filesystem::path> video_720p;
  std::optional<std::filesystem::path> video_360p;

  [[nodiscard]] bool video_enabled() const noexcept {
    return video_720p.has_value() && video_360p.has_value();
  }
};

std::optional<HostedWorkerArguments>
parse_arguments(int argument_count, char **arguments) {
  if (argument_count == 3 &&
      std::string_view(arguments[1]) == "--identity" &&
      !std::string_view(arguments[2]).empty()) {
    return HostedWorkerArguments{
        .identity = arguments[2],
        .video_720p = std::nullopt,
        .video_360p = std::nullopt,
    };
  }
  if (argument_count == 7 &&
      std::string_view(arguments[1]) == "--identity" &&
      !std::string_view(arguments[2]).empty() &&
      std::string_view(arguments[3]) == "--video-720p" &&
      !std::string_view(arguments[4]).empty() &&
      std::string_view(arguments[5]) == "--video-360p" &&
      !std::string_view(arguments[6]).empty()) {
    return HostedWorkerArguments{
        .identity = arguments[2],
        .video_720p = std::filesystem::path{arguments[4]},
        .video_360p = std::filesystem::path{arguments[6]},
    };
  }
  return std::nullopt;
}

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
  const auto options = parse_arguments(argument_count, arguments);
  if (!options.has_value()) {
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
    beacon::worker::QuicListener transport(options->identity, tickets);
    std::unique_ptr<beacon::testing::HostedVideoGenerationFactory>
        generation_factory;
    std::unique_ptr<beacon::worker::video::IWorkerVideoPipeline>
        video_pipeline;
    std::unique_ptr<beacon::worker::audio::IWorkerAudioPipeline>
        audio_pipeline;
    if (options->video_enabled()) {
      auto video_720p = beacon::stream::testing::load_access_unit_vector(
          *options->video_720p);
      auto video_360p = beacon::stream::testing::load_access_unit_vector(
          *options->video_360p);
      if (video_720p.error !=
              beacon::stream::testing::AccessUnitVectorError::none ||
          video_360p.error !=
              beacon::stream::testing::AccessUnitVectorError::none) {
        write_failure_marker(7);
        return 7;
      }
      generation_factory =
          std::make_unique<beacon::testing::HostedVideoGenerationFactory>(
              transport, std::move(video_720p.access_units),
              std::move(video_360p.access_units));
      if (!generation_factory->valid()) {
        write_failure_marker(7);
        return 7;
      }
      video_pipeline =
          std::make_unique<beacon::worker::video::WorkerVideoPipeline>(
              *generation_factory);
      audio_pipeline = std::make_unique<AcceptingAudioPipeline>();
    } else {
      video_pipeline = std::make_unique<NoVideoPipeline>();
      audio_pipeline = std::make_unique<NoAudioPipeline>();
    }
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
      video_pipeline->handle_media_event(event);
      audio_pipeline->handle_media_event(event);
    });
    beacon::worker::WorkerHost host(
        std::move(instance_id), static_cast<std::uint32_t>(::getpid()),
        transport, tickets, *video_pipeline, *audio_pipeline,
        {.available = options->video_enabled(),
         .unavailable_boundary =
             beacon::worker::video::ProductionVideoCapabilityBoundary::encoder,
         .unavailable_code = kHostedVideoUnavailableCode},
        {.available = options->video_enabled(),
         .unavailable_boundary =
             beacon::worker::audio::ProductionAudioCapabilityBoundary::capture,
         .unavailable_code = kHostedAudioUnavailableCode});

    write_marker("BEACON_HOSTED_WORKER_READY");
    const auto result =
        beacon::testing::run_hosted_worker_control(channel, host, outbound);
    if (!host.shutdown_requested()) {
      video_pipeline->reset();
      audio_pipeline->reset();
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
