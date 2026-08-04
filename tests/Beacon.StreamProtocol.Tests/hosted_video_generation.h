#pragma once

#include "beacon/worker/video/worker_video_pipeline.h"

#include <cstdint>
#include <memory>
#include <vector>

namespace beacon::testing {

using HostedAccessUnits = std::vector<std::vector<std::uint8_t>>;

class HostedVideoGenerationFactory final
    : public worker::video::IVideoPipelineGenerationFactory {
public:
  HostedVideoGenerationFactory(worker::IWorkerMediaTransport &transport,
                               HostedAccessUnits video_720p,
                               HostedAccessUnits video_360p);

  [[nodiscard]] bool valid() const noexcept;

  [[nodiscard]] std::shared_ptr<worker::video::IVideoPipelineGeneration>
  create(const worker::video::WorkerVideoPlan &plan) override;

private:
  worker::IWorkerMediaTransport &transport_;
  HostedAccessUnits video_720p_;
  HostedAccessUnits video_360p_;
};

} // namespace beacon::testing
