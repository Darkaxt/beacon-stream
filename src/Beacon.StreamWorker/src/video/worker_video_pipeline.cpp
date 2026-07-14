#include "beacon/worker/video/worker_video_pipeline.h"

#include <type_traits>
#include <utility>
#include <variant>

namespace beacon::worker::video {

bool valid_worker_video_plan(const WorkerVideoPlan &plan) noexcept {
  return !plan.session_id.empty() && !plan.display_device_name.empty() &&
         plan.width != 0 && plan.height != 0 &&
         plan.frame_rate_numerator != 0 && plan.frame_rate_denominator != 0 &&
         plan.minimum_bitrate_bps != 0 &&
         plan.minimum_bitrate_bps <= plan.initial_bitrate_bps &&
         plan.initial_bitrate_bps <= plan.maximum_bitrate_bps;
}

bool selected_video_matches_plan(
    const stream::v1::SelectedVideoMode &selected_video,
    const WorkerVideoPlan &plan) noexcept {
  return selected_video.codec() == stream::v1::VIDEO_CODEC_H264 &&
         selected_video.width() == plan.width &&
         selected_video.height() == plan.height &&
         selected_video.frames_per_second_numerator() ==
             plan.frame_rate_numerator &&
         selected_video.frames_per_second_denominator() ==
             plan.frame_rate_denominator &&
         selected_video.dynamic_range() == stream::v1::DYNAMIC_RANGE_SDR;
}

stream::v1::SelectedVideoMode
selected_video_from_plan(const WorkerVideoPlan &plan) {
  stream::v1::SelectedVideoMode selected_video;
  selected_video.set_codec(stream::v1::VIDEO_CODEC_H264);
  selected_video.set_width(plan.width);
  selected_video.set_height(plan.height);
  selected_video.set_frames_per_second_numerator(plan.frame_rate_numerator);
  selected_video.set_frames_per_second_denominator(plan.frame_rate_denominator);
  selected_video.set_dynamic_range(stream::v1::DYNAMIC_RANGE_SDR);
  return selected_video;
}

WorkerVideoPipeline::WorkerVideoPipeline(
    IVideoPipelineGenerationFactory &factory)
    : factory_(factory) {}

WorkerVideoPipeline::~WorkerVideoPipeline() { reset(); }

bool WorkerVideoPipeline::prepare(const WorkerVideoPlan &plan) {
  if (!valid_worker_video_plan(plan)) {
    return false;
  }
  std::lock_guard lock{mutex_};
  if (active_) {
    return false;
  }
  plan_ = plan;
  prepared_ = true;
  return true;
}

void WorkerVideoPipeline::handle_media_event(const QuicMediaEvent &event) {
  std::visit(
      [this, &event](const auto &value) {
        using Event = std::remove_cvref_t<decltype(value)>;
        if constexpr (std::is_same_v<
                          Event,
                          QuicSessionProtocolOutput::AcceptedStartSession>) {
          start_generation(value);
        } else if constexpr (
            std::is_same_v<Event,
                           QuicSessionProtocolOutput::AcceptedStopSession> ||
            std::is_same_v<Event, QuicTransportDisconnected>) {
          stop_generation(value.session_generation);
        } else if constexpr (
            std::is_same_v<Event,
                           QuicSessionProtocolOutput::AcceptedIdrRequest> ||
            std::is_same_v<Event,
                           QuicSessionProtocolOutput::ParsedFeedback> ||
            std::is_same_v<Event, QuicDatagramOutcome>) {
          forward_generation_event(event, value.session_generation);
        }
      },
      event);
}

bool WorkerVideoPipeline::request_idr() {
  std::shared_ptr<IVideoPipelineGeneration> active;
  {
    std::lock_guard lock{mutex_};
    active = active_;
  }
  return active && active->request_idr();
}

void WorkerVideoPipeline::reset() noexcept {
  std::shared_ptr<IVideoPipelineGeneration> active;
  {
    std::lock_guard lock{mutex_};
    active = std::move(active_);
    active_generation_ = 0;
    plan_ = {};
    prepared_ = false;
  }
  if (active) {
    active->stop();
  }
}

bool WorkerVideoPipeline::prepared() const noexcept {
  std::lock_guard lock{mutex_};
  return prepared_;
}

std::uint64_t WorkerVideoPipeline::active_generation() const noexcept {
  std::lock_guard lock{mutex_};
  return active_generation_;
}

void WorkerVideoPipeline::start_generation(
    const QuicSessionProtocolOutput::AcceptedStartSession &start) {
  std::shared_ptr<IVideoPipelineGeneration> failed;
  {
    std::lock_guard lock{mutex_};
    if (!prepared_ || active_ || start.session_generation == 0 ||
        start.maximum_datagram_bytes == 0 ||
        start.session_id != plan_.session_id ||
        !start.start_session.has_selected_video() ||
        !selected_video_matches_plan(start.start_session.selected_video(),
                                     plan_)) {
      return;
    }
    try {
      auto generation = factory_.create(plan_);
      if (!generation || !generation->start(start.session_generation,
                                            start.maximum_datagram_bytes)) {
        failed = std::move(generation);
      } else {
        active_ = std::move(generation);
        active_generation_ = start.session_generation;
      }
    } catch (...) {
      return;
    }
  }
  if (failed) {
    failed->stop();
  }
}

void WorkerVideoPipeline::stop_generation(
    std::uint64_t session_generation) noexcept {
  std::shared_ptr<IVideoPipelineGeneration> active;
  {
    std::lock_guard lock{mutex_};
    if (!active_ || session_generation == 0 ||
        session_generation != active_generation_) {
      return;
    }
    active = std::move(active_);
    active_generation_ = 0;
  }
  active->stop();
}

void WorkerVideoPipeline::forward_generation_event(
    const QuicMediaEvent &event, std::uint64_t session_generation) {
  std::shared_ptr<IVideoPipelineGeneration> active;
  {
    std::lock_guard lock{mutex_};
    if (!active_ || session_generation == 0 ||
        session_generation != active_generation_) {
      return;
    }
    active = active_;
  }
  active->handle_media_event(event);
}

} // namespace beacon::worker::video
