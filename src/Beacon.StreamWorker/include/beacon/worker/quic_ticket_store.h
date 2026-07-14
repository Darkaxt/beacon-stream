#pragma once

#include "stream_control.pb.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <mutex>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace beacon::worker {

using TicketHash = std::array<std::byte, 32>;

struct AuthorizedQuicTicket {
  TicketHash hash{};
  std::string client_id;
  std::string session_id;
  std::uint64_t plan_revision{};
  std::uint64_t expires_at_unix_ms{};
  std::optional<stream::v1::StartBenchmark> benchmark_plan;
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

struct QuicTicketConsumeOutcome {
  QuicTicketConsumeResult result{QuicTicketConsumeResult::unknown};
  std::optional<stream::v1::StartBenchmark> benchmark_plan;
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
  [[nodiscard]] QuicTicketConsumeOutcome
  consume_authorized(std::span<const std::byte> raw_ticket,
                     std::string_view client_id, std::string_view session_id,
                     std::uint64_t plan_revision, std::uint64_t now_unix_ms);
  [[nodiscard]] std::size_t size() const;

private:
  struct Record {
    AuthorizedQuicTicket ticket;
    bool consumed{};
  };

  mutable std::mutex mutex_;
  std::vector<Record> records_;
};

} // namespace beacon::worker
