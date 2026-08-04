#pragma once

#include "beacon/stream/stream_ticket_authorizer.h"

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <memory>
#include <optional>
#include <span>
#include <string_view>
#include <vector>

namespace beacon::stream::testing {

class HostedEmulatorTicketAuthorizer final : public IStreamTicketAuthorizer {
public:
  [[nodiscard]] StreamTicketAuthorization
  authorize(std::span<const std::byte> ticket, std::string_view client_id,
            std::string_view session_id, std::uint64_t plan_revision,
            std::uint64_t now_unix_ms) override;

private:
  bool consumed_{};
};

enum class HostedEmulatorFrameFlowError {
  none,
  invalid_frame_count,
  not_started,
  already_started,
  unexpected_sequence,
  rendering_incomplete,
};

struct HostedEmulatorFrameFlowResult {
  HostedEmulatorFrameFlowError error{HostedEmulatorFrameFlowError::none};
  std::optional<std::size_t> next_access_unit_index;
  bool rendering_complete{};
};

class HostedEmulatorFrameFlow final {
public:
  explicit HostedEmulatorFrameFlow(std::size_t frame_count) noexcept;

  [[nodiscard]] HostedEmulatorFrameFlowResult start() noexcept;
  [[nodiscard]] HostedEmulatorFrameFlowResult
  rendered(std::uint64_t frame_sequence) noexcept;
  [[nodiscard]] HostedEmulatorFrameFlowError stop() noexcept;

  [[nodiscard]] std::size_t sent_frames() const noexcept;
  [[nodiscard]] std::size_t rendered_feedback() const noexcept;

private:
  std::size_t frame_count_{};
  std::size_t sent_frames_{};
  std::size_t rendered_feedback_{};
  bool started_{};
};

enum class HostedEmulatorEndpointFailure {
  none,
  invalid_configuration,
  msquic_open,
  registration_open,
  configuration_open,
  credential_load,
  listener_open,
  listener_start,
  listener_address,
  connection_refused,
  connection_configuration,
  unsupported_alpn,
  invalid_peer_stream,
  protocol,
  packetization,
  datagram_send,
  session_send,
  unexpected_action,
  unexpected_feedback,
  transport,
  peer_shutdown,
  callback_exception,
};

struct HostedEmulatorEndpointConfig {
  std::filesystem::path certificate_path;
  std::filesystem::path private_key_path;
  std::vector<std::vector<std::uint8_t>> access_units;
};

class HostedEmulatorEndpointServer final {
public:
  explicit HostedEmulatorEndpointServer(HostedEmulatorEndpointConfig config);
  ~HostedEmulatorEndpointServer();

  HostedEmulatorEndpointServer(const HostedEmulatorEndpointServer &) = delete;
  HostedEmulatorEndpointServer &
  operator=(const HostedEmulatorEndpointServer &) = delete;

  [[nodiscard]] int run();
  [[nodiscard]] HostedEmulatorEndpointFailure failure() const noexcept;

private:
  class Impl;
  std::unique_ptr<Impl> impl_;
};

[[nodiscard]] const char *hosted_emulator_endpoint_failure_name(
    HostedEmulatorEndpointFailure failure) noexcept;

} // namespace beacon::stream::testing
