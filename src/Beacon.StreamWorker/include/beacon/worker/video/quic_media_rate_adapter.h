#pragma once

#include "beacon/worker/quic_listener.h"
#include "beacon/worker/video/media_rate_controller.h"

#include <cstdint>
#include <optional>

namespace beacon::worker::video {

[[nodiscard]] std::optional<MediaRateEvidence>
media_rate_evidence_from(const QuicMediaEvent &event,
                         std::uint64_t evidence_sequence);

} // namespace beacon::worker::video
