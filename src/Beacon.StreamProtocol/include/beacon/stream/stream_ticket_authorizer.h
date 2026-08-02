#pragma once

#include "stream_control.pb.h"

#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <string_view>

namespace beacon::stream {

enum class StreamTicketAuthorizationResult {
  accepted,
  unknown,
  replayed,
  client_mismatch,
  session_mismatch,
  plan_mismatch,
  expired,
};

struct StreamTicketAuthorization {
  StreamTicketAuthorizationResult result{
      StreamTicketAuthorizationResult::unknown};
  std::optional<v1::SelectedVideoMode> selected_video;
  std::optional<v1::SelectedAudioMode> selected_audio;
  std::optional<v1::StartBenchmark> benchmark_plan;
};

class IStreamTicketAuthorizer {
public:
  virtual ~IStreamTicketAuthorizer() = default;

  [[nodiscard]] virtual StreamTicketAuthorization authorize(
      std::span<const std::byte> ticket, std::string_view client_id,
      std::string_view session_id, std::uint64_t plan_revision,
      std::uint64_t now_unix_ms) = 0;
};

} // namespace beacon::stream
