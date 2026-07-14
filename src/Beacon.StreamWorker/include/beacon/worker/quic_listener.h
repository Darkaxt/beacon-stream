#pragma once

#include "beacon/stream/msquic_transport.h"
#include "beacon/stream/server_session_protocol.h"
#include "beacon/stream/transport.h"
#include "beacon/worker/media_datagram_transport.h"
#include "beacon/worker/quic_ticket_store.h"
#include "worker_ipc.pb.h"

#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <string_view>
#include <variant>
#include <vector>

namespace beacon::worker {

class IWorkerMediaTransport : public IMediaDatagramTransport {
public:
  virtual ~IWorkerMediaTransport() = default;

  [[nodiscard]] virtual bool configure_listener(std::string_view listen_address,
                                                std::uint16_t listen_port) = 0;
  [[nodiscard]] virtual std::uint16_t local_port() const noexcept = 0;
  [[nodiscard]] virtual bool open_connection() = 0;
  virtual void close_connection() noexcept = 0;
  virtual void request_active_disconnect() noexcept = 0;
  virtual void shutdown() noexcept = 0;
};

enum class QuicDatagramOutcomeKind {
  acknowledged,
  lost,
  canceled,
};

struct QuicDatagramOutcome {
  QuicDatagramOutcomeKind kind{QuicDatagramOutcomeKind::acknowledged};
  std::uint64_t session_generation{};
  std::uint64_t access_unit_sequence{};
  std::uint64_t smoothed_rtt_us{};
  std::uint64_t congestion_window_bytes{};
};

struct QuicTransportDisconnected {
  std::uint64_t session_generation{};
};

using QuicMediaEvent =
    std::variant<stream::ServerSessionProtocolOutput::AcceptedStartSession,
                 stream::ServerSessionProtocolOutput::AcceptedStopSession,
                 stream::ServerSessionProtocolOutput::AcceptedIdrRequest,
                 stream::ServerSessionProtocolOutput::ParsedFeedback,
                 QuicDatagramOutcome, QuicTransportDisconnected>;

enum class QuicListenerFailure {
  none,
  invalid_endpoint,
  identity_open,
  identity_import,
  msquic_open,
  registration_open,
  configuration_open,
  credential_load,
  listener_open,
  listener_start,
  callback_exception,
};

enum class QuicListenerFaultPoint {
  connection_context_allocation,
  event_serialization,
  datagram_context_allocation,
  datagram_final_state_telemetry,
  disconnect_event_construction,
  disconnect_event_publication,
};

using QuicListenerFaultInjector =
    std::function<void(QuicListenerFaultPoint point)>;

struct QuicListenerMetrics {
  std::uint64_t sent_datagrams{};
  std::uint64_t acknowledged_datagrams{};
  std::uint64_t lost_datagrams{};
  std::uint64_t canceled_datagrams{};
  std::uint64_t session_messages{};
  std::uint64_t input_messages{};
  std::uint64_t feedback_messages{};
  std::uint32_t smoothed_rtt_us{};
  std::uint32_t path_mtu{};
  std::uint32_t congestion_window_bytes{};
  std::uint64_t closed_connection_handles{};
  std::uint64_t live_datagram_send_contexts{};
};

class QuicListener final : public IWorkerMediaTransport {
public:
  using EventSink = std::function<void(v1::WorkerIpcEnvelope)>;
  using MediaEventSink = std::function<void(QuicMediaEvent)>;

  QuicListener(std::wstring identity_path,
               AuthorizedQuicTicketStore &authorized_tickets,
               QuicListenerFaultInjector fault_injector = {});
  ~QuicListener() override;

  QuicListener(const QuicListener &) = delete;
  QuicListener &operator=(const QuicListener &) = delete;

  [[nodiscard]] bool configure_listener(std::string_view listen_address,
                                        std::uint16_t listen_port) override;
  [[nodiscard]] std::uint16_t local_port() const noexcept override;
  [[nodiscard]] QuicListenerFailure failure() const noexcept;
  [[nodiscard]] std::uint64_t platform_error() const noexcept;
  [[nodiscard]] bool authenticated() const noexcept;
  [[nodiscard]] bool wait_until_authenticated();
  [[nodiscard]] bool wait_until_media_ready();
  void wait_until_disconnected();
  [[nodiscard]] bool wait_for_received_packets(std::size_t count);
  [[nodiscard]] std::vector<stream::TransportPacket> take_received_packets();
  [[nodiscard]] QuicListenerMetrics metrics() const noexcept;
  [[nodiscard]] std::vector<stream::MsQuicTransportEvent>
  take_transport_events();
  void set_event_sink(EventSink sink);
  void set_media_event_sink(MediaEventSink sink);
  [[nodiscard]] bool open_connection() override;
  void close_connection() noexcept override;
  void request_active_disconnect() noexcept override;
  [[nodiscard]] stream::TransportSendResult
  send_for_generation(stream::TransportPacket packet,
                      std::uint64_t session_generation) override;
  void shutdown() noexcept override;

private:
  class Impl;
  std::unique_ptr<Impl> impl_;
};

} // namespace beacon::worker
