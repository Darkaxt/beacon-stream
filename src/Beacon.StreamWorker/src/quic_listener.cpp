#include "beacon/worker/quic_listener.h"

#include "beacon/stream/msquic_transport.h"
#include "beacon/worker/quic_session_protocol.h"
#include "beacon/worker/secure_bytes.h"
#include "beacon/worker/synthetic_media_source.h"
#include "beacon/worker/worker_events.h"

#include <Windows.h>
#include <bcrypt.h>
#include <ncrypt.h>
#include <wincrypt.h>

#include <msquic.h>

#include <algorithm>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <deque>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <utility>

namespace beacon::worker {
namespace {

constexpr std::string_view kBeaconAlpn{"beacon-stream/1"};
constexpr std::size_t kMaximumIdentityBytes{1024U * 1024U};
constexpr std::uint64_t kSyntheticPresentationTimeUs{1'000'000};

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
      ticket.plan_revision == 0 || ticket.expires_at_unix_ms == 0) {
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

QuicTicketConsumeResult AuthorizedQuicTicketStore::consume(
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
    return QuicTicketConsumeResult::unknown;
  }
  if (found->consumed) {
    return QuicTicketConsumeResult::replayed;
  }
  if (found->ticket.client_id != client_id) {
    return QuicTicketConsumeResult::client_mismatch;
  }
  if (found->ticket.session_id != session_id) {
    return QuicTicketConsumeResult::session_mismatch;
  }
  if (found->ticket.plan_revision != plan_revision) {
    return QuicTicketConsumeResult::plan_mismatch;
  }
  if (now_unix_ms > found->ticket.expires_at_unix_ms) {
    return QuicTicketConsumeResult::expired;
  }

  found->consumed = true;
  return QuicTicketConsumeResult::accepted;
}

std::size_t AuthorizedQuicTicketStore::size() const {
  std::lock_guard lock{mutex_};
  return records_.size();
}

class QuicListener::Impl {
public:
  Impl(std::wstring identity_path,
       AuthorizedQuicTicketStore &authorized_tickets,
       QuicListenerFaultInjector fault_injector)
      : identity_path_(std::move(identity_path)),
        protocol_(authorized_tickets),
        fault_injector_(std::move(fault_injector)) {
    if (identity_path_.empty()) {
      throw std::invalid_argument("A QUIC server identity path is required.");
    }
  }

  ~Impl() { shutdown(); }

  bool configure_listener(std::string_view listen_address,
                          std::uint16_t listen_port) {
    std::lock_guard lock{mutex_};
    if (listener_ != nullptr || connection_ != nullptr || shutdown_) {
      return false;
    }

    QUIC_ADDR parsed{};
    if (listen_address.empty()) {
      QuicAddrSetFamily(&parsed, QUIC_ADDRESS_FAMILY_UNSPEC);
      QuicAddrSetPort(&parsed, listen_port);
    } else {
      const std::string address{listen_address};
      if (!QuicAddrFromString(address.c_str(), listen_port, &parsed)) {
        set_failure(QuicListenerFailure::invalid_endpoint, 0);
        return false;
      }
    }
    listen_address_ = parsed;
    configured_ = true;
    local_port_ = listen_port;
    return true;
  }

  std::uint16_t local_port() const noexcept {
    std::lock_guard lock{mutex_};
    return local_port_;
  }

  QuicListenerFailure failure() const noexcept {
    std::lock_guard lock{mutex_};
    return failure_;
  }

  std::uint64_t platform_error() const noexcept {
    std::lock_guard lock{mutex_};
    return platform_error_;
  }

  bool authenticated() const noexcept {
    std::lock_guard lock{mutex_};
    return protocol_.authenticated();
  }

  bool wait_until_authenticated() {
    std::unique_lock lock{mutex_};
    changed_.wait(lock, [this] {
      return protocol_.authenticated() || connection_ == nullptr || shutdown_;
    });
    return protocol_.authenticated();
  }

  bool wait_until_media_ready() {
    std::unique_lock lock{mutex_};
    changed_.wait(lock, [this] {
      return (protocol_.authenticated() &&
              transport_state_.datagram_send_enabled()) ||
             connection_ == nullptr || shutdown_;
    });
    return protocol_.authenticated() &&
           transport_state_.datagram_send_enabled();
  }

  void wait_until_disconnected() {
    std::unique_lock lock{mutex_};
    changed_.wait(lock, [this] { return connection_ == nullptr || shutdown_; });
  }

  bool wait_for_received_packets(std::size_t count) {
    std::unique_lock lock{mutex_};
    changed_.wait(lock, [this, count] {
      return received_packets_.size() >= count || connection_ == nullptr ||
             shutdown_;
    });
    return received_packets_.size() >= count;
  }

  std::vector<stream::TransportPacket> take_received_packets() {
    std::lock_guard lock{mutex_};
    auto result = std::move(received_packets_);
    received_packets_.clear();
    return result;
  }

  QuicListenerMetrics metrics() const noexcept {
    std::lock_guard lock{mutex_};
    auto result = metrics_;
    result.live_datagram_send_contexts =
        live_datagram_send_contexts_.load(std::memory_order_relaxed);
    return result;
  }

  std::vector<stream::MsQuicTransportEvent> take_transport_events() {
    std::lock_guard lock{mutex_};
    return transport_state_.take_events();
  }

  void set_event_sink(QuicListener::EventSink sink) {
    std::lock_guard lock{event_mutex_};
    event_sink_ = std::move(sink);
  }

  bool open_connection() {
    try {
      std::lock_guard lock{mutex_};
      if (!configured_ || listener_ != nullptr || shutdown_) {
        return false;
      }
      if (!initialize_msquic()) {
        return false;
      }
      const auto listener_status = api_->ListenerOpen(
          registration_, listener_callback, this, &listener_);
      if (QUIC_FAILED(listener_status)) {
        listener_ = nullptr;
        set_failure(QuicListenerFailure::listener_open,
                    status_code(listener_status));
        return false;
      }
      const QUIC_BUFFER alpn{static_cast<std::uint32_t>(kBeaconAlpn.size()),
                             reinterpret_cast<std::uint8_t *>(
                                 const_cast<char *>(kBeaconAlpn.data()))};
      const auto start_status =
          api_->ListenerStart(listener_, &alpn, 1, &listen_address_);
      if (QUIC_FAILED(start_status)) {
        api_->ListenerClose(listener_);
        listener_ = nullptr;
        set_failure(QuicListenerFailure::listener_start,
                    status_code(start_status));
        return false;
      }

      QUIC_ADDR actual{};
      std::uint32_t actual_size = sizeof(actual);
      if (QUIC_SUCCEEDED(api_->GetParam(listener_,
                                        QUIC_PARAM_LISTENER_LOCAL_ADDRESS,
                                        &actual_size, &actual))) {
        local_port_ = QuicAddrGetPort(&actual);
      }
      return true;
    } catch (...) {
      return false;
    }
  }

  void close_connection() noexcept {
    try {
      HQUIC listener = nullptr;
      HQUIC connection = nullptr;
      {
        std::unique_lock lock{mutex_};
        closing_ = true;
        listener = std::exchange(listener_, nullptr);
        changed_.wait(lock, [this] { return active_api_calls_ == 0; });
        connection = connection_;
      }
      if (listener != nullptr) {
        api_->ListenerClose(listener);
      }
      if (connection != nullptr) {
        api_->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                                 0);
        std::unique_lock lock{mutex_};
        changed_.wait(lock, [this] { return connection_ == nullptr; });
      }
      std::lock_guard lock{mutex_};
      closing_ = false;
    } catch (...) {
    }
  }

  stream::TransportSendResult send(stream::TransportPacket packet) {
    try {
      std::uint64_t session_generation = 0;
      {
        std::lock_guard lock{mutex_};
        session_generation = current_generation_;
      }
      return send_for_generation(std::move(packet), session_generation);
    } catch (...) {
      record_callback_exception(nullptr, false);
      return stream::TransportSendResult::connection_closed;
    }
  }

  stream::TransportSendResult
  send_for_generation(stream::TransportPacket packet,
                      std::uint64_t session_generation) {
    if (packet.channel != stream::StreamChannel::media ||
        packet.payload.empty()) {
      return stream::TransportSendResult::connection_closed;
    }

    HQUIC connection = nullptr;
    std::uint64_t connection_generation = 0;
    {
      std::lock_guard lock{mutex_};
      if (connection_ == nullptr || closing_ || !protocol_.authenticated() ||
          !transport_state_.datagram_send_enabled() ||
          session_generation == 0 ||
          session_generation != current_generation_ ||
          packet.payload.size() > transport_state_.maximum_datagram_bytes()) {
        return stream::TransportSendResult::connection_closed;
      }
      connection = connection_;
      connection_generation = current_connection_generation_;
      ++active_api_calls_;
    }
    ActiveApiCallGuard active_call{this};

    const auto datagram_bytes =
        static_cast<std::uint32_t>(packet.payload.size());
    const auto sequence = packet.sequence;
    inject_fault(QuicListenerFaultPoint::datagram_context_allocation);
    auto *context = new DatagramSendContext(std::move(packet.payload), sequence,
                                            session_generation,
                                            connection_generation,
                                            live_datagram_send_contexts_);
    const auto status = api_->DatagramSend(connection, &context->buffer, 1,
                                           QUIC_SEND_FLAG_NONE, context);
    {
      std::lock_guard lock{mutex_};
      if (QUIC_SUCCEEDED(status) &&
          connection_ == connection &&
          current_connection_generation_ == connection_generation &&
          session_generation == current_generation_) {
        append_pending_event(make_media_evidence_event(
            current_session_id_, session_generation, sequence,
            kSyntheticPresentationTimeUs, datagram_bytes));
      }
    }
    active_call.release();
    if (QUIC_FAILED(status)) {
      delete context;
      return stream::TransportSendResult::connection_closed;
    }
    drain_pending_events();
    return stream::TransportSendResult::accepted;
  }

  void shutdown() noexcept {
    {
      std::lock_guard lock{mutex_};
      if (shutdown_) {
        return;
      }
    }
    close_connection();
    {
      std::lock_guard lock{mutex_};
      clear_pending_session_bytes();
      shutdown_ = true;
    }
    release_msquic_state();
  }

private:
  void release_msquic_state() noexcept {
    if (configuration_ != nullptr) {
      api_->ConfigurationClose(configuration_);
      configuration_ = nullptr;
    }
    if (registration_ != nullptr) {
      api_->RegistrationClose(registration_);
      registration_ = nullptr;
    }
    if (api_ != nullptr) {
      MsQuicClose(api_);
      api_ = nullptr;
    }
    delete_imported_key();
    if (certificate_ != nullptr) {
      CertFreeCertificateContext(certificate_);
      certificate_ = nullptr;
    }
    if (certificate_store_ != nullptr) {
      CertCloseStore(certificate_store_, 0);
      certificate_store_ = nullptr;
    }
  }

  struct DatagramSendContext {
    explicit DatagramSendContext(std::vector<std::byte> payload,
                                 std::uint64_t value,
                                 std::uint64_t session_value,
                                 std::uint64_t connection_value,
                                 std::atomic_uint64_t &live_contexts_value)
        : bytes(std::move(payload)), sequence(value),
          session_generation(session_value),
          connection_generation(connection_value),
          live_contexts(&live_contexts_value) {
      buffer.Length = static_cast<std::uint32_t>(bytes.size());
      buffer.Buffer = reinterpret_cast<std::uint8_t *>(bytes.data());
      live_contexts->fetch_add(1, std::memory_order_relaxed);
    }

    ~DatagramSendContext() {
      live_contexts->fetch_sub(1, std::memory_order_relaxed);
    }

    std::vector<std::byte> bytes;
    std::uint64_t sequence{};
    std::uint64_t session_generation{};
    std::uint64_t connection_generation{};
    QUIC_BUFFER buffer{};
    std::atomic_uint64_t *live_contexts{};
  };

  struct ActiveApiCallGuard {
    explicit ActiveApiCallGuard(Impl *value) noexcept : owner(value) {}
    ~ActiveApiCallGuard() { release(); }

    ActiveApiCallGuard(const ActiveApiCallGuard &) = delete;
    ActiveApiCallGuard &operator=(const ActiveApiCallGuard &) = delete;

    void release() noexcept {
      if (owner != nullptr) {
        owner->complete_active_api_call();
        owner = nullptr;
      }
    }

    Impl *owner{};
  };

  struct ConnectionContext {
    Impl *owner{};
    std::uint64_t connection_generation{};
  };

  struct ConnectionShutdownCompleteGuard {
    ConnectionShutdownCompleteGuard(Impl *owner_value,
                                    HQUIC connection_value,
                                    ConnectionContext *context_value) noexcept
        : owner(owner_value), connection(connection_value),
          context(context_value) {}

    ~ConnectionShutdownCompleteGuard() noexcept {
      owner->complete_connection_shutdown(connection, context);
    }

    ConnectionShutdownCompleteGuard(
        const ConnectionShutdownCompleteGuard &) = delete;
    ConnectionShutdownCompleteGuard &operator=(
        const ConnectionShutdownCompleteGuard &) = delete;

    Impl *owner{};
    HQUIC connection{};
    ConnectionContext *context{};
  };

  struct PeerStreamContext {
    Impl *owner{};
    HQUIC connection{};
    HQUIC stream{};
    QuicPeerStreamRole role{QuicPeerStreamRole::invalid};
    std::uint64_t connection_generation{};
  };

  struct StreamSendContext {
    explicit StreamSendContext(std::vector<std::byte> payload)
        : bytes(std::move(payload)) {
      buffer.Length = static_cast<std::uint32_t>(bytes.size());
      buffer.Buffer = reinterpret_cast<std::uint8_t *>(bytes.data());
    }

    std::vector<std::byte> bytes;
    QUIC_BUFFER buffer{};
  };

  static std::uint64_t now_unix_ms() noexcept {
    return static_cast<std::uint64_t>(
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::system_clock::now().time_since_epoch())
            .count());
  }

  void inject_fault(QuicListenerFaultPoint point) {
    if (fault_injector_) {
      fault_injector_(point);
    }
  }

  void complete_active_api_call() noexcept {
    try {
      std::lock_guard lock{mutex_};
      if (active_api_calls_ != 0) {
        --active_api_calls_;
      }
    } catch (...) {
    }
    changed_.notify_all();
  }

  void clear_pending_session_bytes() noexcept {
    secure_clear_bytes(pending_session_bytes_);
  }

  void record_listener_callback_exception(HQUIC connection) noexcept {
    try {
      std::lock_guard lock{mutex_};
      set_failure(QuicListenerFailure::callback_exception, 0);
      if (connection_ == connection) {
        clear_pending_session_bytes();
        connection_ = nullptr;
        current_connection_generation_ = 0;
        protocol_.reset();
      }
    } catch (...) {
    }
    changed_.notify_all();
  }

  void record_callback_exception(HQUIC connection,
                                 bool shutdown_connection) noexcept {
    bool current = false;
    try {
      std::lock_guard lock{mutex_};
      set_failure(QuicListenerFailure::callback_exception, 0);
      current = connection != nullptr && connection_ == connection;
    } catch (...) {
    }
    changed_.notify_all();
    if (shutdown_connection && current && api_ != nullptr) {
      api_->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                               5);
    }
  }

  void complete_connection_shutdown(HQUIC connection,
                                    ConnectionContext *context) noexcept {
    if (api_ != nullptr) {
      api_->ConnectionClose(connection);
    }
    try {
      std::lock_guard lock{mutex_};
      ++metrics_.closed_connection_handles;
      if (connection_ == connection) {
        connection_ = nullptr;
      }
    } catch (...) {
    }
    changed_.notify_all();
    delete context;
  }

  void append_pending_event(v1::WorkerIpcEnvelope event) {
    std::lock_guard lock{event_mutex_};
    pending_events_.push_back(std::move(event));
  }

  void drain_pending_events() {
    std::unique_lock lock{event_mutex_};
    if (draining_events_) {
      return;
    }
    draining_events_ = true;
    while (!pending_events_.empty()) {
      auto event = std::move(pending_events_.front());
      pending_events_.pop_front();
      auto sink = event_sink_;
      lock.unlock();
      if (sink) {
        try {
          sink(std::move(event));
        } catch (...) {
          record_callback_exception(nullptr, false);
        }
      }
      lock.lock();
    }
    draining_events_ = false;
  }

  bool send_session_reply(HQUIC stream, std::vector<std::byte> reply,
                          bool shutdown_after_send) {
    if (shutdown_after_send) {
      std::lock_guard lock{mutex_};
      close_after_session_fin_ = true;
    }
    auto *context = new StreamSendContext(std::move(reply));
    const auto status = api_->StreamSend(
        stream, &context->buffer, 1,
        shutdown_after_send ? QUIC_SEND_FLAG_FIN : QUIC_SEND_FLAG_NONE,
        context);
    if (QUIC_FAILED(status)) {
      if (shutdown_after_send) {
        std::lock_guard lock{mutex_};
        close_after_session_fin_ = false;
      }
      delete context;
      return false;
    }
    return true;
  }

  void process_stream_bytes(HQUIC connection, HQUIC stream,
                            QuicPeerStreamRole role,
                            std::uint64_t connection_generation,
                            std::vector<std::byte> bytes) {
    bool pending_invalid = false;
    {
      std::lock_guard lock{mutex_};
      if (connection_ != connection || connection_generation == 0 ||
          connection_generation != current_connection_generation_) {
        if (role == QuicPeerStreamRole::session) {
          secure_clear_bytes(bytes);
        }
        return;
      }
      if (role == QuicPeerStreamRole::session && !protocol_.authenticated() &&
          !transport_state_.datagram_send_enabled()) {
        constexpr std::size_t maximum_pending =
            maximum_stream_message_bytes + 4U;
        if (bytes.size() > maximum_pending ||
            pending_session_bytes_.size() > maximum_pending - bytes.size()) {
          pending_session_invalid_ = true;
          clear_pending_session_bytes();
        } else {
          pending_session_bytes_.insert(pending_session_bytes_.end(),
                                        bytes.begin(), bytes.end());
        }
        secure_clear_bytes(bytes);
        if (!pending_session_invalid_) {
          return;
        }
        pending_invalid = true;
      }
    }
    if (pending_invalid) {
      api_->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                               3);
      return;
    }

    QuicSessionProtocolOutput output;
    std::optional<QuicSessionProtocolOutput::AcceptedStartSession>
        accepted_start;
    {
      std::lock_guard lock{mutex_};
      if (connection_ != connection ||
          connection_generation != current_connection_generation_) {
        if (role == QuicPeerStreamRole::session) {
          secure_clear_bytes(bytes);
        }
        return;
      }
      output = protocol_.receive(connection_generation, role, bytes,
                                 now_unix_ms());
      if (output.accepted_authentication || !output.inputs.empty() ||
          !output.feedback.empty()) {
        inject_fault(QuicListenerFaultPoint::event_serialization);
      }
      if (output.accepted_authentication) {
        current_session_id_ = output.accepted_authentication->session_id;
        current_generation_ =
            output.accepted_authentication->session_generation;
        append_pending_event(make_transport_authenticated_event(
            current_session_id_, current_generation_,
            output.accepted_authentication->maximum_datagram_bytes));
      }
      for (const auto &input : output.inputs) {
        append_pending_event(
            make_input_received_event(input.session_generation, input.input));
      }
      for (const auto &feedback : output.feedback) {
        append_pending_event(make_feedback_received_event(
            feedback.session_generation, feedback.feedback));
      }
      accepted_start = output.accepted_start_session;
      for (auto &packet : output.packets) {
        switch (packet.channel) {
        case stream::StreamChannel::session:
          ++metrics_.session_messages;
          break;
        case stream::StreamChannel::input:
          ++metrics_.input_messages;
          break;
        case stream::StreamChannel::feedback:
          ++metrics_.feedback_messages;
          break;
        case stream::StreamChannel::media:
          break;
        }
        received_packets_.push_back(std::move(packet));
      }
      if (role == QuicPeerStreamRole::session) {
        std::fill(bytes.begin(), bytes.end(), std::byte{});
      }
    }
    changed_.notify_all();
    drain_pending_events();
    bool reply_failed = false;
    for (std::size_t index = 0; index < output.session_replies.size();
         ++index) {
      const bool close_after =
          output.close_connection && index + 1 == output.session_replies.size();
      if (!send_session_reply(stream, std::move(output.session_replies[index]),
                              close_after)) {
        reply_failed = true;
        break;
      }
    }
    if (reply_failed ||
        (output.close_connection && output.session_replies.empty())) {
      api_->ConnectionShutdown(connection, QUIC_CONNECTION_SHUTDOWN_FLAG_NONE,
                               3);
    }
    if (accepted_start) {
      std::uint64_t marker_sequence = 0;
      {
        std::lock_guard lock{mutex_};
        marker_sequence = next_marker_sequence_;
        if (marker_sequence == 0) {
          return;
        }
        ++next_marker_sequence_;
      }
      auto packets = synthetic_media_source_.emit_access_unit_marker(
          marker_sequence, kSyntheticPresentationTimeUs,
          accepted_start->maximum_datagram_bytes);
      if (!packets.empty()) {
        static_cast<void>(send_for_generation(
            std::move(packets.front()), accepted_start->session_generation));
      }
    }
  }

  bool initialize_msquic() {
    if (api_ != nullptr) {
      return true;
    }
    const auto open_status = MsQuicOpen2(&api_);
    if (QUIC_FAILED(open_status)) {
      api_ = nullptr;
      set_failure(QuicListenerFailure::msquic_open, status_code(open_status));
      return false;
    }
    const QUIC_REGISTRATION_CONFIG registration_config{
        "beacon-stream-worker", QUIC_EXECUTION_PROFILE_LOW_LATENCY};
    const auto registration_status =
        api_->RegistrationOpen(&registration_config, &registration_);
    if (QUIC_FAILED(registration_status)) {
      set_failure(QuicListenerFailure::registration_open,
                  status_code(registration_status));
      release_msquic_state();
      return false;
    }

    QUIC_SETTINGS settings{};
    settings.IdleTimeoutMs = 0;
    settings.IsSet.IdleTimeoutMs = true;
    settings.PeerBidiStreamCount = 1;
    settings.IsSet.PeerBidiStreamCount = true;
    settings.PeerUnidiStreamCount = 2;
    settings.IsSet.PeerUnidiStreamCount = true;
    settings.DatagramReceiveEnabled = true;
    settings.IsSet.DatagramReceiveEnabled = true;
    const QUIC_BUFFER alpn{static_cast<std::uint32_t>(kBeaconAlpn.size()),
                           reinterpret_cast<std::uint8_t *>(
                               const_cast<char *>(kBeaconAlpn.data()))};
    const auto configuration_status =
        api_->ConfigurationOpen(registration_, &alpn, 1, &settings,
                                sizeof(settings), nullptr, &configuration_);
    if (QUIC_FAILED(configuration_status)) {
      set_failure(QuicListenerFailure::configuration_open,
                  status_code(configuration_status));
      release_msquic_state();
      return false;
    }
    if (!load_identity()) {
      release_msquic_state();
      return false;
    }
    QUIC_CREDENTIAL_CONFIG credentials{};
    credentials.Type = QUIC_CREDENTIAL_TYPE_CERTIFICATE_CONTEXT;
    credentials.CertificateContext = reinterpret_cast<QUIC_CERTIFICATE *>(
        const_cast<CERT_CONTEXT *>(certificate_));
    const auto credential_status =
        api_->ConfigurationLoadCredential(configuration_, &credentials);
    if (QUIC_FAILED(credential_status)) {
      set_failure(QuicListenerFailure::credential_load,
                  status_code(credential_status));
      release_msquic_state();
      return false;
    }
    failure_ = QuicListenerFailure::none;
    platform_error_ = 0;
    return true;
  }

  bool load_identity() {
    std::ifstream input(identity_path_, std::ios::binary | std::ios::ate);
    if (!input) {
      set_failure(QuicListenerFailure::identity_open, GetLastError());
      return false;
    }
    const auto end = input.tellg();
    if (end <= 0 || static_cast<std::uint64_t>(end) > kMaximumIdentityBytes) {
      set_failure(QuicListenerFailure::identity_open, ERROR_INVALID_DATA);
      return false;
    }
    std::vector<std::uint8_t> pfx(static_cast<std::size_t>(end));
    input.seekg(0, std::ios::beg);
    if (!input.read(reinterpret_cast<char *>(pfx.data()),
                    static_cast<std::streamsize>(pfx.size()))) {
      SecureZeroMemory(pfx.data(), pfx.size());
      set_failure(QuicListenerFailure::identity_open, ERROR_READ_FAULT);
      return false;
    }

    CRYPT_DATA_BLOB blob{static_cast<DWORD>(pfx.size()), pfx.data()};
    certificate_store_ = PFXImportCertStore(
        &blob, L"", CRYPT_USER_KEYSET | PKCS12_PREFER_CNG_KSP);
    SecureZeroMemory(pfx.data(), pfx.size());
    if (certificate_store_ == nullptr) {
      set_failure(QuicListenerFailure::identity_import, GetLastError());
      return false;
    }
    const auto *imported =
        CertEnumCertificatesInStore(certificate_store_, nullptr);
    if (imported == nullptr) {
      set_failure(QuicListenerFailure::identity_import, GetLastError());
      return false;
    }
    certificate_ = CertDuplicateCertificateContext(imported);
    if (certificate_ == nullptr) {
      set_failure(QuicListenerFailure::identity_import, GetLastError());
      return false;
    }
    DWORD provider_info_size = 0;
    if (!CertGetCertificateContextProperty(certificate_,
                                           CERT_KEY_PROV_INFO_PROP_ID, nullptr,
                                           &provider_info_size) ||
        provider_info_size == 0) {
      set_failure(QuicListenerFailure::identity_import, GetLastError());
      return false;
    }
    std::vector<std::byte> provider_info_storage(provider_info_size);
    if (!CertGetCertificateContextProperty(
            certificate_, CERT_KEY_PROV_INFO_PROP_ID,
            provider_info_storage.data(), &provider_info_size)) {
      set_failure(QuicListenerFailure::identity_import, GetLastError());
      return false;
    }
    const auto *provider_info = reinterpret_cast<const CRYPT_KEY_PROV_INFO *>(
        provider_info_storage.data());
    if (provider_info->pwszContainerName == nullptr) {
      set_failure(QuicListenerFailure::identity_import, ERROR_INVALID_DATA);
      return false;
    }
    key_container_name_ = provider_info->pwszContainerName;
    key_provider_name_ = provider_info->pwszProvName == nullptr
                             ? MS_KEY_STORAGE_PROVIDER
                             : provider_info->pwszProvName;
    return true;
  }

  void delete_imported_key() noexcept {
    if (key_container_name_.empty()) {
      return;
    }
    const auto container_name = std::exchange(key_container_name_, {});
    const auto provider_name = std::exchange(key_provider_name_, {});
    NCRYPT_PROV_HANDLE provider = 0;
    if (NCryptOpenStorageProvider(&provider, provider_name.c_str(), 0) !=
        ERROR_SUCCESS) {
      return;
    }
    NCRYPT_KEY_HANDLE key = 0;
    if (NCryptOpenKey(provider, &key, container_name.c_str(), 0, 0) ==
        ERROR_SUCCESS) {
      if (NCryptDeleteKey(key, 0) != ERROR_SUCCESS) {
        NCryptFreeObject(key);
      }
    }
    NCryptFreeObject(provider);
  }

  static std::uint64_t status_code(QUIC_STATUS status) noexcept {
    return static_cast<std::uint64_t>(static_cast<std::uint32_t>(status));
  }

  void set_failure(QuicListenerFailure failure,
                   std::uint64_t platform_error) noexcept {
    failure_ = failure;
    platform_error_ = platform_error;
  }

  static QUIC_STATUS QUIC_API listener_callback(HQUIC, void *context,
                                                QUIC_LISTENER_EVENT *event) noexcept {
    auto &self = *static_cast<Impl *>(context);
    try {
      if (event->Type != QUIC_LISTENER_EVENT_NEW_CONNECTION) {
        return QUIC_STATUS_NOT_SUPPORTED;
      }
      self.inject_fault(
          QuicListenerFaultPoint::connection_context_allocation);
      std::uint64_t connection_generation = 0;
      {
        std::lock_guard lock{self.mutex_};
        if (self.connection_ != nullptr || self.closing_ || self.shutdown_) {
          return QUIC_STATUS_CONNECTION_REFUSED;
        }
        self.connection_ = event->NEW_CONNECTION.Connection;
        connection_generation = ++self.next_connection_generation_;
        self.current_connection_generation_ = connection_generation;
        self.transport_state_ = {};
        self.clear_pending_session_bytes();
        self.protocol_.begin_connection(connection_generation);
        self.received_packets_.clear();
        self.pending_session_invalid_ = false;
        self.close_after_session_fin_ = false;
      }
      self.append_pending_event(
          make_connection_observed_event(connection_generation));
      self.drain_pending_events();
      auto *connection_context = new ConnectionContext{
          .owner = &self, .connection_generation = connection_generation};
      self.api_->SetCallbackHandler(
          event->NEW_CONNECTION.Connection,
          reinterpret_cast<void *>(connection_callback), connection_context);
      const auto status = self.api_->ConnectionSetConfiguration(
          event->NEW_CONNECTION.Connection, self.configuration_);
      if (QUIC_FAILED(status)) {
        {
          std::lock_guard lock{self.mutex_};
          if (self.connection_ == event->NEW_CONNECTION.Connection &&
              self.current_connection_generation_ == connection_generation) {
            self.connection_ = nullptr;
            self.current_connection_generation_ = 0;
            self.clear_pending_session_bytes();
            self.protocol_.reset();
          }
        }
        self.changed_.notify_all();
        self.append_pending_event(make_transport_failed_event(
            connection_generation,
            static_cast<std::uint32_t>(status_code(status))));
      } else {
        self.append_pending_event(
            make_connection_configured_event(connection_generation));
      }
      self.drain_pending_events();
      return status;
    } catch (...) {
      self.record_listener_callback_exception(
          event->Type == QUIC_LISTENER_EVENT_NEW_CONNECTION
              ? event->NEW_CONNECTION.Connection
              : nullptr);
      return QUIC_STATUS_OUT_OF_MEMORY;
    }
  }

  static QUIC_STATUS QUIC_API connection_callback(
      HQUIC connection, void *context,
      QUIC_CONNECTION_EVENT *event) noexcept {
    auto *connection_context = static_cast<ConnectionContext *>(context);
    auto &self = *connection_context->owner;
    const auto connection_generation =
        connection_context->connection_generation;
    try {
      const auto is_current = [&self, connection, connection_generation]() {
        std::lock_guard lock{self.mutex_};
        return self.connection_ == connection &&
               self.current_connection_generation_ == connection_generation;
      };
      if (event->Type != QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE &&
          event->Type != QUIC_CONNECTION_EVENT_DATAGRAM_SEND_STATE_CHANGED &&
          !is_current()) {
        return QUIC_STATUS_SUCCESS;
      }
      switch (event->Type) {
    case QUIC_CONNECTION_EVENT_CONNECTED: {
      const std::string_view alpn{
          reinterpret_cast<const char *>(event->CONNECTED.NegotiatedAlpn),
          event->CONNECTED.NegotiatedAlpnLength};
      bool accepted = false;
      {
        std::lock_guard lock{self.mutex_};
        accepted = self.transport_state_.connected(alpn);
      }
      if (accepted) {
        self.append_pending_event(
            make_transport_connected_event(connection_generation));
        self.drain_pending_events();
      } else {
        self.api_->ConnectionShutdown(connection,
                                      QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 1);
      }
      break;
    }
    case QUIC_CONNECTION_EVENT_DATAGRAM_STATE_CHANGED: {
      std::vector<std::byte> pending;
      HQUIC session_stream = nullptr;
      {
        std::lock_guard lock{self.mutex_};
        self.transport_state_.datagram_state_changed(
            event->DATAGRAM_STATE_CHANGED.SendEnabled != FALSE,
            event->DATAGRAM_STATE_CHANGED.MaxSendLength);
        self.protocol_.set_maximum_datagram_bytes(
            event->DATAGRAM_STATE_CHANGED.SendEnabled != FALSE
                ? event->DATAGRAM_STATE_CHANGED.MaxSendLength
                : 0);
        if (event->DATAGRAM_STATE_CHANGED.SendEnabled != FALSE &&
            !self.pending_session_bytes_.empty()) {
          pending = self.pending_session_bytes_;
          self.clear_pending_session_bytes();
          session_stream = self.session_stream_;
        }
      }
      self.changed_.notify_all();
      if (!pending.empty() && session_stream != nullptr) {
        self.process_stream_bytes(connection, session_stream,
                                  QuicPeerStreamRole::session,
                                  connection_generation, std::move(pending));
      }
      break;
    }
    case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
      QUIC_UINT62 stream_id = 0;
      std::uint32_t stream_id_size = sizeof(stream_id);
      const auto status = self.api_->GetParam(event->PEER_STREAM_STARTED.Stream,
                                              QUIC_PARAM_STREAM_ID,
                                              &stream_id_size, &stream_id);
      const auto role = QUIC_SUCCEEDED(status) ? classify_peer_stream(stream_id)
                                               : QuicPeerStreamRole::invalid;
      bool accepted = role != QuicPeerStreamRole::invalid;
      {
        std::lock_guard lock{self.mutex_};
        if (role == QuicPeerStreamRole::session) {
          accepted = accepted && self.session_stream_ == nullptr;
          if (accepted) {
            self.session_stream_ = event->PEER_STREAM_STARTED.Stream;
          }
        }
      }
      auto *stream_context =
          new PeerStreamContext{.owner = &self,
                                .connection = connection,
                                .stream = event->PEER_STREAM_STARTED.Stream,
                                .role = role,
                                .connection_generation =
                                    connection_generation};
      self.api_->SetCallbackHandler(event->PEER_STREAM_STARTED.Stream,
                                    reinterpret_cast<void *>(stream_callback),
                                    stream_context);
      if (!accepted) {
        self.api_->StreamShutdown(event->PEER_STREAM_STARTED.Stream,
                                  QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 2);
      }
      break;
    }
    case QUIC_CONNECTION_EVENT_DATAGRAM_SEND_STATE_CHANGED: {
      auto *send = static_cast<DatagramSendContext *>(
          event->DATAGRAM_SEND_STATE_CHANGED.ClientContext);
      const bool final_state =
          send != nullptr && QUIC_DATAGRAM_SEND_STATE_IS_FINAL(
                                 event->DATAGRAM_SEND_STATE_CHANGED.State);
      std::unique_ptr<DatagramSendContext> final_send;
      if (final_state) {
        final_send.reset(send);
        self.inject_fault(
            QuicListenerFaultPoint::datagram_final_state_telemetry);
      }
      {
        std::lock_guard lock{self.mutex_};
        const bool current =
            send != nullptr &&
            send->connection_generation == connection_generation &&
            connection_generation == self.current_connection_generation_ &&
            self.connection_ == connection &&
            send->session_generation == self.current_generation_;
        if (current) {
          self.transport_state_.datagram_send_state_changed(
              send->sequence, event->DATAGRAM_SEND_STATE_CHANGED.State);
        }
        switch (current ? event->DATAGRAM_SEND_STATE_CHANGED.State
                        : QUIC_DATAGRAM_SEND_UNKNOWN) {
        case QUIC_DATAGRAM_SEND_SENT:
          ++self.metrics_.sent_datagrams;
          break;
        case QUIC_DATAGRAM_SEND_ACKNOWLEDGED:
        case QUIC_DATAGRAM_SEND_ACKNOWLEDGED_SPURIOUS:
          ++self.metrics_.acknowledged_datagrams;
          break;
        case QUIC_DATAGRAM_SEND_LOST_DISCARDED:
          ++self.metrics_.lost_datagrams;
          break;
        case QUIC_DATAGRAM_SEND_CANCELED:
          ++self.metrics_.canceled_datagrams;
          break;
        case QUIC_DATAGRAM_SEND_UNKNOWN:
        case QUIC_DATAGRAM_SEND_LOST_SUSPECT:
          break;
        }
      }
      QUIC_STATISTICS_V2 statistics{};
      std::uint32_t statistics_size = sizeof(statistics);
      if (is_current() &&
          QUIC_SUCCEEDED(self.api_->GetParam(connection,
                                             QUIC_PARAM_CONN_STATISTICS_V2,
                                             &statistics_size, &statistics))) {
        std::lock_guard lock{self.mutex_};
        if (self.connection_ == connection &&
            self.current_connection_generation_ == connection_generation) {
          self.metrics_.smoothed_rtt_us = statistics.Rtt;
          self.metrics_.path_mtu = statistics.SendPathMtu;
        }
      }
      break;
    }
    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT: {
      {
        std::lock_guard lock{self.mutex_};
        self.transport_state_.transport_failed(
            event->SHUTDOWN_INITIATED_BY_TRANSPORT.Status,
            event->SHUTDOWN_INITIATED_BY_TRANSPORT.ErrorCode);
      }
      self.append_pending_event(make_transport_failed_event(
          connection_generation,
          static_cast<std::uint32_t>(status_code(
              event->SHUTDOWN_INITIATED_BY_TRANSPORT.Status))));
      self.drain_pending_events();
      break;
    }
    case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_PEER: {
      std::lock_guard lock{self.mutex_};
      self.transport_state_.peer_closed(
          event->SHUTDOWN_INITIATED_BY_PEER.ErrorCode);
      break;
    }
    case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
      ConnectionShutdownCompleteGuard completion_guard{
          &self, connection, connection_context};
      try {
        bool current = false;
        std::string disconnected_session_id;
        std::uint64_t disconnected_generation = 0;
        {
          std::lock_guard lock{self.mutex_};
          current = self.connection_ == connection &&
                    self.current_connection_generation_ ==
                        connection_generation;
          if (current) {
            self.session_stream_ = nullptr;
            disconnected_session_id = std::move(self.current_session_id_);
            disconnected_generation = self.current_generation_;
            self.current_generation_ = 0;
            self.clear_pending_session_bytes();
            self.protocol_.reset();
            self.current_connection_generation_ = 0;
            self.transport_state_.closed();
          }
        }
        if (current && disconnected_generation != 0) {
          self.inject_fault(
              QuicListenerFaultPoint::disconnect_event_construction);
          auto disconnected = make_transport_disconnected_event(
              disconnected_session_id, disconnected_generation);
          self.inject_fault(
              QuicListenerFaultPoint::disconnect_event_publication);
          self.append_pending_event(std::move(disconnected));
        }
        if (current) {
          self.drain_pending_events();
        }
      } catch (...) {
        self.record_callback_exception(connection, false);
      }
      break;
    }
      default:
        break;
      }
      return QUIC_STATUS_SUCCESS;
    } catch (...) {
      self.record_callback_exception(
          connection,
          event->Type != QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE);
      return QUIC_STATUS_SUCCESS;
    }
  }

  static QUIC_STATUS QUIC_API stream_callback(HQUIC stream, void *context,
                                              QUIC_STREAM_EVENT *event) noexcept {
    auto *stream_context = static_cast<PeerStreamContext *>(context);
    auto &self = *stream_context->owner;
    try {
      switch (event->Type) {
    case QUIC_STREAM_EVENT_RECEIVE: {
      std::vector<std::byte> bytes;
      bytes.reserve(static_cast<std::size_t>(event->RECEIVE.TotalBufferLength));
      for (std::uint32_t index = 0; index < event->RECEIVE.BufferCount;
           ++index) {
        const auto &buffer = event->RECEIVE.Buffers[index];
        const auto *begin = reinterpret_cast<const std::byte *>(buffer.Buffer);
        bytes.insert(bytes.end(), begin, begin + buffer.Length);
      }
      self.process_stream_bytes(
          stream_context->connection, stream, stream_context->role,
          stream_context->connection_generation, std::move(bytes));
      break;
    }
    case QUIC_STREAM_EVENT_SEND_COMPLETE: {
      auto *send =
          static_cast<StreamSendContext *>(event->SEND_COMPLETE.ClientContext);
      delete send;
      break;
    }
    case QUIC_STREAM_EVENT_SEND_SHUTDOWN_COMPLETE: {
      bool shutdown = false;
      {
        std::lock_guard lock{self.mutex_};
        shutdown =
            self.connection_ == stream_context->connection &&
            self.current_connection_generation_ ==
                stream_context->connection_generation &&
            self.close_after_session_fin_ && self.session_stream_ == stream;
        if (shutdown) {
          self.close_after_session_fin_ = false;
        }
      }
      if (shutdown && event->SEND_SHUTDOWN_COMPLETE.Graceful != FALSE) {
        self.api_->ConnectionShutdown(stream_context->connection,
                                      QUIC_CONNECTION_SHUTDOWN_FLAG_NONE, 4);
      }
      break;
    }
    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
      {
        std::lock_guard lock{self.mutex_};
        if (self.connection_ == stream_context->connection &&
            self.current_connection_generation_ ==
                stream_context->connection_generation &&
            self.session_stream_ == stream) {
          self.session_stream_ = nullptr;
        }
      }
      self.api_->StreamClose(stream);
      delete stream_context;
      break;
    }
      default:
        break;
      }
      return QUIC_STATUS_SUCCESS;
    } catch (...) {
      self.record_callback_exception(
          stream_context->connection,
          event->Type != QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE);
      return QUIC_STATUS_SUCCESS;
    }
  }

  std::wstring identity_path_;
  QuicSessionProtocol protocol_;
  QuicListenerFaultInjector fault_injector_;
  SyntheticMediaSource synthetic_media_source_;
  mutable std::mutex mutex_;
  std::condition_variable changed_;
  std::mutex event_mutex_;
  std::deque<v1::WorkerIpcEnvelope> pending_events_;
  QuicListener::EventSink event_sink_;
  const QUIC_API_TABLE *api_{};
  HQUIC registration_{};
  HQUIC configuration_{};
  HQUIC listener_{};
  HQUIC connection_{};
  HQUIC session_stream_{};
  HCERTSTORE certificate_store_{};
  PCCERT_CONTEXT certificate_{};
  std::wstring key_container_name_;
  std::wstring key_provider_name_;
  QUIC_ADDR listen_address_{};
  stream::MsQuicTransportState transport_state_;
  QuicListenerMetrics metrics_;
  std::atomic_uint64_t live_datagram_send_contexts_{};
  std::vector<stream::TransportPacket> received_packets_;
  std::vector<std::byte> pending_session_bytes_;
  std::string current_session_id_;
  std::uint64_t current_generation_{};
  std::uint64_t next_marker_sequence_{1};
  std::uint64_t current_connection_generation_{};
  std::uint64_t next_connection_generation_{};
  std::uint16_t local_port_{};
  std::size_t active_api_calls_{};
  bool configured_{};
  bool closing_{};
  bool shutdown_{};
  bool pending_session_invalid_{};
  bool close_after_session_fin_{};
  bool draining_events_{};
  QuicListenerFailure failure_{QuicListenerFailure::none};
  std::uint64_t platform_error_{};
};

QuicListener::QuicListener(std::wstring identity_path,
                            AuthorizedQuicTicketStore &authorized_tickets,
                            QuicListenerFaultInjector fault_injector)
    : impl_(std::make_unique<Impl>(std::move(identity_path),
                                    authorized_tickets,
                                    std::move(fault_injector))) {}

QuicListener::~QuicListener() = default;

bool QuicListener::configure_listener(std::string_view listen_address,
                                      std::uint16_t listen_port) {
  return impl_->configure_listener(listen_address, listen_port);
}

std::uint16_t QuicListener::local_port() const noexcept {
  return impl_->local_port();
}

QuicListenerFailure QuicListener::failure() const noexcept {
  return impl_->failure();
}

std::uint64_t QuicListener::platform_error() const noexcept {
  return impl_->platform_error();
}

bool QuicListener::authenticated() const noexcept {
  return impl_->authenticated();
}

bool QuicListener::wait_until_authenticated() {
  return impl_->wait_until_authenticated();
}

bool QuicListener::wait_until_media_ready() {
  return impl_->wait_until_media_ready();
}

void QuicListener::wait_until_disconnected() {
  impl_->wait_until_disconnected();
}

bool QuicListener::wait_for_received_packets(std::size_t count) {
  return impl_->wait_for_received_packets(count);
}

std::vector<stream::TransportPacket> QuicListener::take_received_packets() {
  return impl_->take_received_packets();
}

QuicListenerMetrics QuicListener::metrics() const noexcept {
  return impl_->metrics();
}

std::vector<stream::MsQuicTransportEvent>
QuicListener::take_transport_events() {
  return impl_->take_transport_events();
}

void QuicListener::set_event_sink(EventSink sink) {
  impl_->set_event_sink(std::move(sink));
}

bool QuicListener::open_connection() { return impl_->open_connection(); }

void QuicListener::close_connection() noexcept { impl_->close_connection(); }

stream::TransportSendResult QuicListener::send(stream::TransportPacket packet) {
  return impl_->send(std::move(packet));
}

void QuicListener::shutdown() noexcept { impl_->shutdown(); }

} // namespace beacon::worker
