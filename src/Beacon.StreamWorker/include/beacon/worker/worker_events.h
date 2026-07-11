#pragma once

#include "stream_control.pb.h"
#include "worker_ipc.pb.h"

#include <cstdint>
#include <string_view>

namespace beacon::worker {

[[nodiscard]] v1::WorkerIpcEnvelope make_transport_authenticated_event(
    std::string_view session_id, std::uint64_t session_generation,
    std::uint16_t maximum_datagram_bytes);
[[nodiscard]] v1::WorkerIpcEnvelope make_transport_disconnected_event(
    std::string_view session_id, std::uint64_t session_generation);
[[nodiscard]] v1::WorkerIpcEnvelope make_input_received_event(
    std::uint64_t session_generation,
    const stream::v1::InputStreamEnvelope &input);
[[nodiscard]] v1::WorkerIpcEnvelope make_feedback_received_event(
    std::uint64_t session_generation,
    const stream::v1::FeedbackStreamEnvelope &feedback);
[[nodiscard]] v1::WorkerIpcEnvelope make_media_evidence_event(
    std::string_view session_id, std::uint64_t session_generation,
    std::uint64_t sequence, std::uint64_t presentation_time_us,
    std::uint32_t datagram_bytes);

} // namespace beacon::worker
