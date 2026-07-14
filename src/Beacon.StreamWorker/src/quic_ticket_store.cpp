#include "beacon/worker/quic_ticket_store.h"

#include <Windows.h>
#include <bcrypt.h>

#include <algorithm>
#include <limits>
#include <stdexcept>
#include <utility>

namespace beacon::worker {
namespace {

bool hashes_equal(const TicketHash &left,
                  std::span<const std::byte> right) noexcept {
  if (right.size() != left.size()) {
    return false;
  }

  unsigned int difference = 0;
  for (std::size_t index = 0; index < left.size(); ++index) {
    difference |= std::to_integer<unsigned int>(left[index] ^ right[index]);
  }
  return difference == 0;
}

} // namespace

TicketHash hash_stream_ticket(std::span<const std::byte> ticket) {
  if (ticket.size() > std::numeric_limits<ULONG>::max()) {
    throw std::length_error("Stream ticket exceeds the CNG input limit.");
  }

  BCRYPT_ALG_HANDLE algorithm = nullptr;
  if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr,
                                  0) < 0) {
    throw std::runtime_error("Could not open the SHA-256 provider.");
  }

  TicketHash result{};
  const auto status = BCryptHash(
      algorithm, nullptr, 0,
      reinterpret_cast<PUCHAR>(const_cast<std::byte *>(ticket.data())),
      static_cast<ULONG>(ticket.size()),
      reinterpret_cast<PUCHAR>(result.data()),
      static_cast<ULONG>(result.size()));
  BCryptCloseAlgorithmProvider(algorithm, 0);
  if (status < 0) {
    throw std::runtime_error("Could not hash the stream ticket.");
  }
  return result;
}

bool AuthorizedQuicTicketStore::authorize(AuthorizedQuicTicket ticket) {
  if (ticket.client_id.empty() || ticket.session_id.empty() ||
      ticket.plan_revision == 0 || ticket.expires_at_unix_ms == 0 ||
      ticket.selected_video.has_value() == ticket.benchmark_plan.has_value()) {
    return false;
  }

  std::lock_guard lock{mutex_};
  const auto duplicate =
      std::ranges::find_if(records_, [&ticket](const Record &record) {
        return hashes_equal(record.ticket.hash, ticket.hash);
      });
  if (duplicate != records_.end()) {
    return false;
  }
  records_.push_back({.ticket = std::move(ticket), .consumed = false});
  return true;
}

void AuthorizedQuicTicketStore::revoke(std::span<const std::byte> hash) {
  std::lock_guard lock{mutex_};
  std::erase_if(records_, [hash](const Record &record) {
    return hashes_equal(record.ticket.hash, hash);
  });
}

stream::StreamTicketAuthorization AuthorizedQuicTicketStore::authorize(
    std::span<const std::byte> raw_ticket, std::string_view client_id,
    std::string_view session_id, std::uint64_t plan_revision,
    std::uint64_t now_unix_ms) {
  const auto hash = hash_stream_ticket(raw_ticket);
  std::lock_guard lock{mutex_};
  const auto found =
      std::ranges::find_if(records_, [&hash](const Record &record) {
        return hashes_equal(record.ticket.hash, hash);
      });
  if (found == records_.end()) {
    return {.result = stream::StreamTicketAuthorizationResult::unknown};
  }
  if (found->consumed) {
    return {.result = stream::StreamTicketAuthorizationResult::replayed};
  }
  if (found->ticket.client_id != client_id) {
    return {.result =
                stream::StreamTicketAuthorizationResult::client_mismatch};
  }
  if (found->ticket.session_id != session_id) {
    return {.result =
                stream::StreamTicketAuthorizationResult::session_mismatch};
  }
  if (found->ticket.plan_revision != plan_revision) {
    return {.result =
                stream::StreamTicketAuthorizationResult::plan_mismatch};
  }
  if (now_unix_ms > found->ticket.expires_at_unix_ms) {
    return {.result = stream::StreamTicketAuthorizationResult::expired};
  }

  found->consumed = true;
  return {.result = stream::StreamTicketAuthorizationResult::accepted,
          .selected_video = found->ticket.selected_video,
          .benchmark_plan = found->ticket.benchmark_plan};
}

std::size_t AuthorizedQuicTicketStore::size() const {
  std::lock_guard lock{mutex_};
  return records_.size();
}

} // namespace beacon::worker
