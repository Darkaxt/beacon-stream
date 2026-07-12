#pragma once

#include "stream_core.h"
#include "callback_gate.h"

#include <msquic.h>

#include <array>
#include <cstddef>
#ifndef NDEBUG
#include <functional>
#endif
#include <mutex>
#include <optional>
#include <span>
#include <vector>

namespace beacon::android::streamcore {

[[nodiscard]] QUIC_SETTINGS make_msquic_client_settings() noexcept;
[[nodiscard]] constexpr bool msquic_receive_is_synchronous() noexcept {
  return true;
}
[[nodiscard]] constexpr bool msquic_receive_requires_completion() noexcept {
  return false;
}
void secure_clear_send_bytes(std::vector<std::byte> &bytes) noexcept;
[[nodiscard]] std::uint64_t expected_stream_id(StreamRole role) noexcept;
[[nodiscard]] bool stream_id_matches(StreamRole role, std::uint64_t id) noexcept;

enum class StreamStartValidation { accepted, failed_status, unexpected_id };

[[nodiscard]] StreamStartValidation validate_stream_start(
    StreamRole role, QUIC_STATUS status, std::uint64_t id) noexcept;

struct ShutdownCleanupAction {
  bool notify_closed{};
  std::optional<Endpoint> reconnect;
};

[[nodiscard]] ShutdownCleanupAction select_shutdown_cleanup_action(
    bool release_requested, std::optional<Endpoint> pending_endpoint);

class MsQuicClientCallbacks {
 public:
  virtual ~MsQuicClientCallbacks() = default;
  virtual void transport_connected(std::uint64_t generation) = 0;
  virtual void session_bytes(std::uint64_t generation,
                             std::vector<std::byte> bytes) = 0;
  virtual void media_datagram(std::uint64_t generation,
                              std::vector<std::byte> bytes) = 0;
  virtual void connection_lost(std::uint64_t generation) = 0;
  virtual void transport_closed(std::uint64_t generation) = 0;
};

class MsQuicClient final : public Transport {
 public:
  explicit MsQuicClient(MsQuicClientCallbacks &callbacks);
  ~MsQuicClient() override;

  bool connect(const Endpoint &endpoint) override;
  bool open_stream(StreamRole role) override;
  bool send(StreamRole role, std::vector<std::byte> bytes) override;
  void shutdown() override;
  void release() override;
  void report_local_failure(std::uint64_t generation) noexcept;

 private:
  friend class MsQuicClientTestAccess;
  struct StreamContext {
    MsQuicClient *owner{};
    StreamRole role{};
    std::uint64_t generation{};
  };
  struct SendContext {
    std::vector<std::byte> bytes;
    QUIC_BUFFER buffer{};

    void clear() noexcept {
      secure_clear_send_bytes(bytes);
      buffer = {};
    }
    ~SendContext() { clear(); }
  };

  static QUIC_STATUS QUIC_API connection_callback(
      HQUIC connection, void *context, QUIC_CONNECTION_EVENT *event);
  static QUIC_STATUS QUIC_API stream_callback(
      HQUIC stream, void *context, QUIC_STREAM_EVENT *event);
  HQUIC stream_for(StreamRole role) const noexcept;
  void fail_stream_start(std::uint64_t generation) noexcept;
  void close_api_handles();
  void complete_shutdown();

  MsQuicClientCallbacks &callbacks_;
  mutable std::mutex mutex_;
  const QUIC_API_TABLE *api_{};
  HQUIC registration_{};
  HQUIC configuration_{};
  HQUIC connection_{};
  HQUIC session_stream_{};
  HQUIC input_stream_{};
  HQUIC feedback_stream_{};
  StreamContext session_context_{this, StreamRole::session};
  StreamContext input_context_{this, StreamRole::input};
  StreamContext feedback_context_{this, StreamRole::feedback};
  std::array<std::byte, 32> expected_pin_{};
  std::uint64_t connection_generation_{};
  bool certificate_validated_{};
  bool shutdown_started_{};
  bool loss_reported_{};
  bool release_requested_{};
  bool api_closed_{};
  std::optional<Endpoint> pending_endpoint_;
  std::shared_ptr<CallbackBarrier> callback_barrier_{
      std::make_shared<CallbackBarrier>()};
  bool cleanup_scheduled_{};
#ifndef NDEBUG
  std::function<bool(const Endpoint &)> test_connect_hook_;
  bool test_skip_msquic_handle_cleanup_{};
#endif
};

}  // namespace beacon::android::streamcore
