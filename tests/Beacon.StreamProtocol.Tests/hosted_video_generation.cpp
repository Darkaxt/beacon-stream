#include "hosted_video_generation.h"

#include "beacon/stream/video_media_packetizer.h"

#include <algorithm>
#include <mutex>
#include <optional>
#include <utility>

namespace beacon::testing {
namespace {

class HostedVideoGeneration final
    : public worker::video::IVideoPipelineGeneration {
public:
  HostedVideoGeneration(worker::IWorkerMediaTransport &transport,
                        const worker::video::WorkerVideoPlan &plan,
                        const HostedAccessUnits &access_units)
      : transport_(transport), plan_(plan), access_units_(access_units) {}

  bool start(std::uint64_t session_generation,
             std::uint16_t maximum_datagram_bytes) override {
    {
      std::lock_guard lock{mutex_};
      if (started_ || stopped_ || session_generation == 0 ||
          maximum_datagram_bytes == 0 || access_units_.empty()) {
        return false;
      }
      started_ = true;
      session_generation_ = session_generation;
      maximum_datagram_bytes_ = maximum_datagram_bytes;
    }
    return send_next();
  }

  void handle_media_event(const worker::QuicMediaEvent &event) override {
    const auto *parsed = std::get_if<
        stream::ServerSessionProtocolOutput::ParsedFeedback>(&event);
    if (parsed == nullptr || !parsed->feedback.has_rendered_frame()) {
      return;
    }
    {
      std::lock_guard lock{mutex_};
      if (!started_ || stopped_ || sending_ ||
          parsed->session_generation != session_generation_ ||
          parsed->feedback.rendered_frame().frame_sequence() !=
              last_sent_sequence_ ||
          next_access_unit_ >= access_units_.size()) {
        return;
      }
    }
    static_cast<void>(send_next());
  }

  bool request_idr() override {
    std::lock_guard lock{mutex_};
    return started_ && !stopped_;
  }

  void stop() noexcept override {
    std::lock_guard lock{mutex_};
    stopped_ = true;
    started_ = false;
    session_generation_ = 0;
    maximum_datagram_bytes_ = 0;
  }

private:
  bool send_next() {
    std::size_t access_unit_index = 0;
    std::uint64_t sequence = 0;
    std::uint64_t generation = 0;
    std::uint16_t maximum_datagram_bytes = 0;
    {
      std::lock_guard lock{mutex_};
      if (!started_ || stopped_ || sending_ ||
          next_access_unit_ >= access_units_.size()) {
        return false;
      }
      sending_ = true;
      access_unit_index = next_access_unit_;
      sequence = next_sequence_;
      generation = session_generation_;
      maximum_datagram_bytes = maximum_datagram_bytes_;
    }

    const auto &unit = access_units_[access_unit_index];
    const stream::EncodedVideoAccessUnitView view{
        .bytes = unit,
        .idr = access_unit_index == 0,
        .codec_configuration = access_unit_index == 0,
    };
    const std::uint64_t presentation_time_us =
        static_cast<std::uint64_t>(access_unit_index) * 1'000'000ULL *
        plan_.frame_rate_denominator / plan_.frame_rate_numerator;
    auto packetized = packetizer_.packetize(
        view, sequence, presentation_time_us, maximum_datagram_bytes);
    bool accepted =
        packetized.failure == stream::VideoMediaPacketizerFailure::none &&
        !packetized.packets.empty();
    if (accepted) {
      for (auto &packet : packetized.packets) {
        if (transport_.send_for_generation(std::move(packet), generation) !=
            stream::TransportSendResult::accepted) {
          accepted = false;
          break;
        }
      }
    }

    {
      std::lock_guard lock{mutex_};
      sending_ = false;
      if (!accepted || stopped_ || generation != session_generation_) {
        return false;
      }
      ++next_access_unit_;
      ++next_sequence_;
      last_sent_sequence_ = sequence;
    }
    return true;
  }

  worker::IWorkerMediaTransport &transport_;
  worker::video::WorkerVideoPlan plan_;
  const HostedAccessUnits &access_units_;
  stream::VideoMediaPacketizer packetizer_;
  std::mutex mutex_;
  std::size_t next_access_unit_{};
  std::uint64_t next_sequence_{1};
  std::uint64_t last_sent_sequence_{};
  std::uint64_t session_generation_{};
  std::uint16_t maximum_datagram_bytes_{};
  bool started_{};
  bool stopped_{};
  bool sending_{};
};

bool valid_vectors(const HostedAccessUnits &vectors) noexcept {
  return vectors.size() >= 2 &&
         std::all_of(vectors.begin(), vectors.end(),
                     [](const auto &unit) { return !unit.empty(); });
}

} // namespace

HostedVideoGenerationFactory::HostedVideoGenerationFactory(
    worker::IWorkerMediaTransport &transport, HostedAccessUnits video_720p,
    HostedAccessUnits video_360p)
    : transport_(transport), video_720p_(std::move(video_720p)),
      video_360p_(std::move(video_360p)) {}

bool HostedVideoGenerationFactory::valid() const noexcept {
  return valid_vectors(video_720p_) && valid_vectors(video_360p_);
}

std::shared_ptr<worker::video::IVideoPipelineGeneration>
HostedVideoGenerationFactory::create(
    const worker::video::WorkerVideoPlan &plan) {
  const HostedAccessUnits *selected = nullptr;
  if (plan.width == 1280 && plan.height == 720 &&
      plan.frame_rate_numerator == 60 && plan.frame_rate_denominator == 1) {
    selected = &video_720p_;
  } else if (plan.width == 640 && plan.height == 360 &&
             plan.frame_rate_numerator == 30 &&
             plan.frame_rate_denominator == 1) {
    selected = &video_360p_;
  }
  if (selected == nullptr || !valid_vectors(*selected)) {
    return {};
  }
  return std::make_shared<HostedVideoGeneration>(transport_, plan, *selected);
}

} // namespace beacon::testing
