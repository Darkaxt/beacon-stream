#pragma once

#include "beacon/worker/quic_listener.h"

#include <cstdint>
#include <memory>
#include <mutex>
#include <string>

namespace beacon::worker::audio {

struct WorkerAudioPlan {
  std::string session_id;
  std::uint32_t sample_rate_hz{};
  std::uint32_t channel_count{};
  std::uint32_t frame_duration_us{};
  std::uint32_t bitrate_bps{};

  bool operator==(const WorkerAudioPlan &) const = default;
};

[[nodiscard]] bool
valid_worker_audio_plan(const WorkerAudioPlan &plan) noexcept;
[[nodiscard]] bool
selected_audio_matches_plan(const stream::v1::SelectedAudioMode &selected_audio,
                            const WorkerAudioPlan &plan) noexcept;
[[nodiscard]] stream::v1::SelectedAudioMode
selected_audio_from_plan(const WorkerAudioPlan &plan);

class IAudioPipelineGeneration {
public:
  virtual ~IAudioPipelineGeneration() = default;

  [[nodiscard]] virtual bool start(std::uint64_t session_generation,
                                   std::uint16_t maximum_datagram_bytes) = 0;
  virtual void stop() noexcept = 0;
};

class IAudioPipelineGenerationFactory {
public:
  virtual ~IAudioPipelineGenerationFactory() = default;

  [[nodiscard]] virtual std::shared_ptr<IAudioPipelineGeneration>
  create(const WorkerAudioPlan &plan) = 0;
};

class IWorkerAudioPipeline {
public:
  virtual ~IWorkerAudioPipeline() = default;

  [[nodiscard]] virtual bool prepare(const WorkerAudioPlan &plan) = 0;
  virtual void handle_media_event(const QuicMediaEvent &event) = 0;
  virtual void reset() noexcept = 0;
};

class WorkerAudioPipeline final : public IWorkerAudioPipeline {
public:
  explicit WorkerAudioPipeline(IAudioPipelineGenerationFactory &factory);
  ~WorkerAudioPipeline() override;

  WorkerAudioPipeline(const WorkerAudioPipeline &) = delete;
  WorkerAudioPipeline &operator=(const WorkerAudioPipeline &) = delete;

  [[nodiscard]] bool prepare(const WorkerAudioPlan &plan) override;
  void handle_media_event(const QuicMediaEvent &event) override;
  void reset() noexcept override;

  [[nodiscard]] bool prepared() const noexcept;
  [[nodiscard]] std::uint64_t active_generation() const noexcept;

private:
  [[nodiscard]] std::shared_ptr<IAudioPipelineGeneration>
  create_started_generation(const WorkerAudioPlan &plan,
                            std::uint64_t session_generation,
                            std::uint16_t maximum_datagram_bytes) noexcept;
  void start_generation(
      const stream::ServerSessionProtocolOutput::AcceptedStartSession &start);
  void stop_generation(std::uint64_t session_generation) noexcept;

  IAudioPipelineGenerationFactory &factory_;
  mutable std::mutex mutex_;
  WorkerAudioPlan plan_;
  std::shared_ptr<IAudioPipelineGeneration> active_;
  std::uint64_t starting_generation_{};
  std::uint64_t active_generation_{};
  bool prepared_{};
};

} // namespace beacon::worker::audio
