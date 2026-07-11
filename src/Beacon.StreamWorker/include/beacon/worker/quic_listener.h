#pragma once

#include "beacon/stream/msquic_transport.h"
#include "beacon/stream/transport.h"
#include "worker_ipc.pb.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <memory>
#include <mutex>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace beacon::worker {

class IWorkerMediaTransport : public stream::IStreamTransport {
public:
  [[nodiscard]] virtual bool configure_listener(std::string_view listen_address,
                                                std::uint16_t listen_port) = 0;
  [[nodiscard]] virtual std::uint16_t local_port() const noexcept = 0;
};

using TicketHash = std::array<std::byte, 32>;

struct AuthorizedQuicTicket {
  TicketHash hash{};
  std::string client_id;
  std::string session_id;
  std::uint64_t plan_revision{};
  std::uint64_t expires_at_unix_ms{};
};

enum class QuicTicketConsumeResult {
  accepted,
  unknown,
  replayed,
  client_mismatch,
  session_mismatch,
  plan_mismatch,
  expired,
};

[[nodiscard]] TicketHash hash_stream_ticket(std::span<const std::byte> ticket);

class AuthorizedQuicTicketStore {
public:
  [[nodiscard]] bool authorize(AuthorizedQuicTicket ticket);
  void revoke(std::span<const std::byte> hash);
  [[nodiscard]] QuicTicketConsumeResult
  consume(std::span<const std::byte> raw_ticket, std::string_view client_id,
          std::string_view session_id, std::uint64_t plan_revision,
          std::uint64_t now_unix_ms);
  [[nodiscard]] std::size_t size() const;

private:
  struct Record {
    AuthorizedQuicTicket ticket;
    bool consumed{};
  };

  mutable std::mutex mutex_;
  std::vector<Record> records_;
};

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
};

class QuicListener final : public IWorkerMediaTransport {
public:
  using EventSink = std::function<void(v1::WorkerIpcEnvelope)>;

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
  [[nodiscard]] bool open_connection() override;
  void close_connection() noexcept override;
  [[nodiscard]] stream::TransportSendResult
  send(stream::TransportPacket packet) override;
  void shutdown() noexcept override;

private:
  class Impl;
  std::unique_ptr<Impl> impl_;
};

} // namespace beacon::worker
