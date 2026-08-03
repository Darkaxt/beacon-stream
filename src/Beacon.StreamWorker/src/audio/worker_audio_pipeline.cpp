#include "beacon/worker/audio/worker_audio_pipeline.h"

#include "beacon/worker/audio/opus_audio_encoder.h"

#include <type_traits>
#include <utility>
#include <variant>

namespace beacon::worker::audio {

bool valid_worker_audio_plan(const WorkerAudioPlan &plan) noexcept {
  return !plan.session_id.empty() &&
         plan.sample_rate_hz == opus_sample_rate_hz &&
         plan.channel_count == opus_channel_count &&
         plan.frame_duration_us == opus_frame_duration_us &&
         plan.bitrate_bps == opus_bitrate_bps;
}

bool selected_audio_matches_plan(
    const stream::v1::SelectedAudioMode &selected_audio,
    const WorkerAudioPlan &plan) noexcept {
  return selected_audio.codec() == stream::v1::AUDIO_CODEC_OPUS &&
         selected_audio.sample_rate_hz() == plan.sample_rate_hz &&
         selected_audio.channel_count() == plan.channel_count &&
         selected_audio.frame_duration_us() == plan.frame_duration_us &&
         selected_audio.bitrate_bps() == plan.bitrate_bps;
}

stream::v1::SelectedAudioMode
selected_audio_from_plan(const WorkerAudioPlan &plan) {
  stream::v1::SelectedAudioMode selected_audio;
  selected_audio.set_codec(stream::v1::AUDIO_CODEC_OPUS);
  selected_audio.set_sample_rate_hz(plan.sample_rate_hz);
  selected_audio.set_channel_count(plan.channel_count);
  selected_audio.set_frame_duration_us(plan.frame_duration_us);
  selected_audio.set_bitrate_bps(plan.bitrate_bps);
  return selected_audio;
}

WorkerAudioPipeline::WorkerAudioPipeline(
    IAudioPipelineGenerationFactory &factory)
    : factory_(factory) {}

WorkerAudioPipeline::~WorkerAudioPipeline() { reset(); }

bool WorkerAudioPipeline::prepare(const WorkerAudioPlan &plan) {
  if (!valid_worker_audio_plan(plan)) {
    return false;
  }
  std::lock_guard lock{mutex_};
  if (active_ || starting_generation_ != 0) {
    return false;
  }
  plan_ = plan;
  prepared_ = true;
  return true;
}

void WorkerAudioPipeline::handle_media_event(const QuicMediaEvent &event) {
  std::visit(
      [this](const auto &value) {
        using Event = std::remove_cvref_t<decltype(value)>;
        if constexpr (std::is_same_v<Event,
                                     stream::ServerSessionProtocolOutput::
                                         AcceptedStartSession>) {
          start_generation(value);
        } else if constexpr (std::is_same_v<
                                 Event, stream::ServerSessionProtocolOutput::
                                            AcceptedStopSession> ||
                             std::is_same_v<Event, QuicTransportDisconnected>) {
          stop_generation(value.session_generation);
        }
      },
      event);
}

void WorkerAudioPipeline::reset() noexcept {
  std::shared_ptr<IAudioPipelineGeneration> active;
  {
    std::lock_guard lock{mutex_};
    active = std::move(active_);
    starting_generation_ = 0;
    active_generation_ = 0;
    plan_ = {};
    prepared_ = false;
  }
  if (active) {
    active->stop();
  }
}

bool WorkerAudioPipeline::prepared() const noexcept {
  std::lock_guard lock{mutex_};
  return prepared_;
}

std::uint64_t WorkerAudioPipeline::active_generation() const noexcept {
  std::lock_guard lock{mutex_};
  return active_generation_;
}

std::shared_ptr<IAudioPipelineGeneration>
WorkerAudioPipeline::create_started_generation(
    const WorkerAudioPlan &plan, std::uint64_t session_generation,
    std::uint16_t maximum_datagram_bytes) noexcept {
  std::shared_ptr<IAudioPipelineGeneration> generation;
  try {
    generation = factory_.create(plan);
  } catch (...) {
    return {};
  }
  if (!generation) {
    return {};
  }
  try {
    if (generation->start(session_generation, maximum_datagram_bytes)) {
      return generation;
    }
  } catch (...) {
  }
  generation->stop();
  return {};
}

void WorkerAudioPipeline::start_generation(
    const stream::ServerSessionProtocolOutput::AcceptedStartSession &start) {
  WorkerAudioPlan plan;
  {
    std::lock_guard lock{mutex_};
    if (!prepared_ || active_ || starting_generation_ != 0 ||
        start.session_generation == 0 || start.maximum_datagram_bytes == 0 ||
        start.session_id != plan_.session_id ||
        !start.start_session.has_selected_audio() ||
        !selected_audio_matches_plan(start.start_session.selected_audio(),
                                     plan_)) {
      return;
    }
    plan = plan_;
    starting_generation_ = start.session_generation;
  }

  auto generation = create_started_generation(plan, start.session_generation,
                                              start.maximum_datagram_bytes);
  std::shared_ptr<IAudioPipelineGeneration> abandoned;
  {
    std::lock_guard lock{mutex_};
    if (starting_generation_ != start.session_generation) {
      abandoned = std::move(generation);
    } else {
      starting_generation_ = 0;
      if (generation && prepared_ && !active_ && plan_ == plan) {
        active_ = std::move(generation);
        active_generation_ = start.session_generation;
      } else {
        abandoned = std::move(generation);
      }
    }
  }
  if (abandoned) {
    abandoned->stop();
  }
}

void WorkerAudioPipeline::stop_generation(
    std::uint64_t session_generation) noexcept {
  std::shared_ptr<IAudioPipelineGeneration> active;
  {
    std::lock_guard lock{mutex_};
    if (session_generation == 0) {
      return;
    }
    if (session_generation == starting_generation_) {
      starting_generation_ = 0;
    }
    if (active_ && session_generation == active_generation_) {
      active = std::move(active_);
      active_generation_ = 0;
    }
  }
  if (active) {
    active->stop();
  }
}

} // namespace beacon::worker::audio
