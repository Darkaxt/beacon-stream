#pragma once

#include "beacon/stream/transport.h"
#include "beacon/worker/quic_listener.h"
#include "worker_ipc.pb.h"

#include <cstddef>
#include <cstdint>
#include <string>
#include <vector>

namespace beacon::worker {

inline constexpr std::uint32_t worker_protocol_version = 1;

class WorkerHost {
 public:
  WorkerHost(std::vector<std::byte> worker_instance_id,
             std::uint32_t process_id,
             IWorkerMediaTransport& transport,
             AuthorizedQuicTicketStore& authorized_tickets);

  [[nodiscard]] v1::WorkerIpcEnvelope hello() const;
  [[nodiscard]] v1::WorkerIpcEnvelope ready() const;
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> dispatch(
      const v1::WorkerIpcEnvelope& request);
  [[nodiscard]] bool shutdown_requested() const noexcept;
  [[nodiscard]] std::size_t authorized_ticket_count() const;

 private:
  [[nodiscard]] v1::WorkerIpcEnvelope response_envelope(
      const v1::WorkerIpcEnvelope& request) const;
  [[nodiscard]] v1::WorkerIpcEnvelope completion(
      const v1::WorkerIpcEnvelope& request,
      bool succeeded,
      v1::WorkerErrorCode error_code) const;
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> reject(
      const v1::WorkerIpcEnvelope& request,
      v1::WorkerErrorCode error_code) const;
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> prepare(
      const v1::WorkerIpcEnvelope& request);
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> prepare_benchmark(
      const v1::WorkerIpcEnvelope& request);
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> authorize_ticket(
      const v1::WorkerIpcEnvelope& request);
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> revoke_ticket(
      const v1::WorkerIpcEnvelope& request);
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> start_media(
      const v1::WorkerIpcEnvelope& request);
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> stop_media(
      const v1::WorkerIpcEnvelope& request);
  [[nodiscard]] std::vector<v1::WorkerIpcEnvelope> shutdown(
      const v1::WorkerIpcEnvelope& request);

  std::vector<std::byte> worker_instance_id_;
  std::uint32_t process_id_{};
  IWorkerMediaTransport& transport_;
  AuthorizedQuicTicketStore& authorized_tickets_;
  bool prepared_{};
  bool benchmark_prepared_{};
  bool streaming_{};
  bool shutdown_requested_{};
  std::string session_id_;
  stream::v1::StartBenchmark benchmark_plan_;
};

}  // namespace beacon::worker
