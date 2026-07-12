#include "callback_gate.h"
#include "deferred_action_queue.h"
#include "msquic_client.h"
#include "grant_mapping.h"
#include "lifecycle_generation.h"
#include "session_registry.h"
#include "surface_owner.h"
#include "test_failure.h"

#include <algorithm>
#include <atomic>
#include <condition_variable>
#include <functional>
#include <memory>
#include <mutex>
#include <thread>
#include <vector>

namespace beacon::android::streamcore {

class MsQuicClientTestAccess {
 public:
  static QUIC_STATUS receive_session(MsQuicClient &client,
                                     std::span<const std::byte> bytes) {
    QUIC_BUFFER buffer{
        static_cast<std::uint32_t>(bytes.size()),
        reinterpret_cast<std::uint8_t *>(
            const_cast<std::byte *>(bytes.data()))};
    QUIC_STREAM_EVENT event{};
    event.Type = QUIC_STREAM_EVENT_RECEIVE;
    event.RECEIVE.TotalBufferLength = bytes.size();
    event.RECEIVE.BufferCount = 1;
    event.RECEIVE.Buffers = &buffer;
    return MsQuicClient::stream_callback(
        nullptr, &client.session_context_, &event);
  }

  static QUIC_STATUS shutdown_complete_for_release(
      MsQuicClient &client, std::atomic<bool> &callback_returned) {
    auto outer_callback = client.callback_barrier_->enter();
    (void)outer_callback;
    client.release_requested_ = true;
    QUIC_CONNECTION_EVENT event{};
    event.Type = QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE;
    const QUIC_STATUS status = MsQuicClient::connection_callback(
        nullptr, &client, &event);
    callback_returned = true;
    return status;
  }

  static bool send_context_clear_wipes_bytes_and_buffer() {
    MsQuicClient::SendContext context;
    context.bytes = {std::byte{1}, std::byte{2}, std::byte{3}};
    context.buffer.Length = static_cast<std::uint32_t>(context.bytes.size());
    context.buffer.Buffer = reinterpret_cast<std::uint8_t *>(context.bytes.data());
    context.clear();
    return std::ranges::all_of(context.bytes, [](std::byte value) {
             return value == std::byte{};
           }) &&
           context.buffer.Length == 0 && context.buffer.Buffer == nullptr;
  }

#ifndef NDEBUG
  static void prime_connection(
      MsQuicClient &client, std::uint64_t generation, bool shutdown_started,
      std::function<bool(const Endpoint &)> reconnect_hook) {
    std::lock_guard lock(client.mutex_);
    client.connection_ = reinterpret_cast<HQUIC>(1);
    client.connection_generation_ = generation;
    client.shutdown_started_ = shutdown_started;
    client.loss_reported_ = false;
    client.test_skip_msquic_handle_cleanup_ = true;
    client.test_connect_hook_ = std::move(reconnect_hook);
  }

  static QUIC_STATUS shutdown_initiated_by_transport(MsQuicClient &client) {
    QUIC_CONNECTION_EVENT event{};
    event.Type = QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT;
    return MsQuicClient::connection_callback(client.connection_, &client, &event);
  }

  static QUIC_STATUS shutdown_initiated_by_peer(MsQuicClient &client) {
    QUIC_CONNECTION_EVENT event{};
    event.Type = QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_PEER;
    return MsQuicClient::connection_callback(client.connection_, &client, &event);
  }

  static QUIC_STATUS shutdown_complete(MsQuicClient &client) {
    QUIC_CONNECTION_EVENT event{};
    event.Type = QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE;
    return MsQuicClient::connection_callback(client.connection_, &client, &event);
  }

  static bool shutdown_started(MsQuicClient &client) {
    std::lock_guard lock(client.mutex_);
    return client.shutdown_started_;
  }

  static void reject_stream_start(
      MsQuicClient &client, StreamRole role, QUIC_STATUS status,
      std::uint64_t id, std::uint64_t generation) {
    if (validate_stream_start(role, status, id) ==
        StreamStartValidation::accepted) {
      BEACON_TEST_REQUIRE(false);
    }
    client.fail_stream_start(generation);
  }
#endif
};

}  // namespace beacon::android::streamcore

namespace {

namespace android_stream = beacon::android::streamcore;

#define require(expression) BEACON_TEST_REQUIRE(expression)

void cleanup_runs_only_after_callback_scope_exits() {
  auto barrier = std::make_shared<android_stream::CallbackBarrier>();
  std::mutex mutex;
  std::condition_variable changed;
  bool callback_exited = false;
  bool cleanup_ran = false;
  bool cleanup_observed_exit = false;
  const std::thread::id callback_thread = std::this_thread::get_id();
  std::thread::id cleanup_thread;
  {
    auto callback = barrier->enter();
    android_stream::DeferredActionQueue::instance().enqueue(
        barrier,
        [&] {
          std::lock_guard lock(mutex);
          cleanup_observed_exit = callback_exited;
          cleanup_thread = std::this_thread::get_id();
          cleanup_ran = true;
          changed.notify_all();
        });
    require(!cleanup_ran);
    callback_exited = true;
  }
  std::unique_lock lock(mutex);
  changed.wait(lock, [&] { return cleanup_ran; });
  require(cleanup_observed_exit);
  require(cleanup_thread != callback_thread);
}

void destruction_action_runs_after_cleanup_action_returns() {
  auto barrier = std::make_shared<android_stream::CallbackBarrier>();
  std::mutex mutex;
  std::condition_variable changed;
  bool cleanup_returned = false;
  bool destruction_ran = false;
  bool destruction_observed_cleanup_return = false;
  android_stream::DeferredActionQueue::instance().enqueue(
      barrier,
      [&, barrier] {
        android_stream::DeferredActionQueue::instance().enqueue(
            barrier,
            [&] {
              std::lock_guard lock(mutex);
              destruction_observed_cleanup_return = cleanup_returned;
              destruction_ran = true;
              changed.notify_all();
            });
        cleanup_returned = true;
      });
  std::unique_lock lock(mutex);
  changed.wait(lock, [&] { return destruction_ran; });
  require(destruction_observed_cleanup_return);
}

void close_waits_for_inflight_native_callback() {
  android_stream::CallbackGate gate;
  auto callback = gate.try_enter();
  require(callback.has_value());
  std::mutex mutex;
  std::condition_variable changed;
  bool close_started = false;
  bool close_finished = false;
  std::thread closer([&] {
    {
      std::lock_guard lock(mutex);
      close_started = true;
      changed.notify_all();
    }
    gate.close_and_wait();
    {
      std::lock_guard lock(mutex);
      close_finished = true;
      changed.notify_all();
    }
  });
  {
    std::unique_lock lock(mutex);
    changed.wait(lock, [&] { return close_started; });
    require(!close_finished);
  }
  callback.reset();
  {
    std::unique_lock lock(mutex);
    changed.wait(lock, [&] { return close_finished; });
  }
  closer.join();
  require(!gate.try_enter().has_value());
}

void surface_replacement_releases_every_acquisition_once() {
  int acquired = 0;
  int released = 0;
  android_stream::SurfaceOwner owner([&](void *) { ++released; });
  auto acquire = [&](std::uintptr_t value) {
    ++acquired;
    return reinterpret_cast<void *>(value);
  };
  owner.replace(acquire(1));
  owner.replace(acquire(2));
  owner.replace(nullptr);
  owner.replace(acquire(3));
  owner.close();
  owner.close();
  require(acquired == 3);
  require(released == 3);
}

void production_msquic_settings_use_liveness_heartbeat_without_idle_ownership() {
  const QUIC_SETTINGS settings = android_stream::make_msquic_client_settings();
  require(settings.IsSet.IdleTimeoutMs == TRUE);
  require(settings.IdleTimeoutMs == 0);
  require(settings.IsSet.KeepAliveIntervalMs == TRUE);
  require(settings.KeepAliveIntervalMs == 1000);
}

void production_receive_uses_synchronous_ownership() {
  require(android_stream::msquic_receive_is_synchronous());
  require(!android_stream::msquic_receive_requires_completion());
}

void send_buffers_are_securely_cleared_before_destruction() {
  std::vector<std::byte> bytes{
      std::byte{1}, std::byte{2}, std::byte{3}, std::byte{4}};
  android_stream::secure_clear_send_bytes(bytes);
  require(std::ranges::all_of(bytes, [](std::byte value) {
    return value == std::byte{};
  }));
  require(android_stream::MsQuicClientTestAccess::
              send_context_clear_wipes_bytes_and_buffer());
}

class ProductionCallbacks final : public android_stream::MsQuicClientCallbacks {
 public:
  void transport_connected(std::uint64_t) override {}
  void session_bytes(std::uint64_t generation,
                     std::vector<std::byte> value) override {
    std::lock_guard lock(mutex);
    received_generation = generation;
    received = std::move(value);
  }
  void media_datagram(std::uint64_t, std::vector<std::byte>) override {}
  void connection_lost(std::uint64_t generation) override {
    std::lock_guard lock(mutex);
    loss_generations.push_back(generation);
    changed.notify_all();
  }
  void transport_closed(std::uint64_t) override {
    std::lock_guard lock(mutex);
    cleanup_observed_callback_return = callback_returned->load();
    closed = true;
    changed.notify_all();
  }

  std::mutex mutex;
  std::condition_variable changed;
  std::vector<std::byte> received;
  std::vector<std::uint64_t> loss_generations;
  std::uint64_t received_generation{};
  std::atomic<bool> *callback_returned{};
  bool cleanup_observed_callback_return{};
  bool closed{};
};

#ifndef NDEBUG
android_stream::Endpoint endpoint(std::uint64_t generation) {
  android_stream::Endpoint result;
  result.host = "beacon.example";
  result.port = 47990;
  result.generation = generation;
  return result;
}

void transport_shutdown_accepts_pending_generation_before_completion() {
  ProductionCallbacks callbacks;
  android_stream::MsQuicClient client(callbacks);
  std::mutex mutex;
  std::condition_variable changed;
  bool reconnect_started = false;
  std::uint64_t reconnect_generation = 0;
  android_stream::MsQuicClientTestAccess::prime_connection(
      client, 1, false, [&](const android_stream::Endpoint &replacement) {
        std::lock_guard lock(mutex);
        reconnect_generation = replacement.generation;
        reconnect_started = true;
        changed.notify_all();
        return true;
      });

  require(android_stream::MsQuicClientTestAccess::shutdown_initiated_by_transport(
              client) == QUIC_STATUS_SUCCESS);
  require(android_stream::MsQuicClientTestAccess::shutdown_started(client));
  require(client.connect(endpoint(2)));
  require(android_stream::MsQuicClientTestAccess::shutdown_complete(client) ==
          QUIC_STATUS_SUCCESS);
  {
    std::unique_lock lock(mutex);
    changed.wait(lock, [&] { return reconnect_started; });
  }
  require(reconnect_generation == 2);
  require(callbacks.loss_generations == std::vector<std::uint64_t>{1});
}

void explicit_shutdown_stale_loss_is_generation_filtered() {
  ProductionCallbacks callbacks;
  android_stream::MsQuicClient client(callbacks);
  android_stream::LifecycleGeneration lifecycle;
  require(lifecycle.activate(1));
  android_stream::MsQuicClientTestAccess::prime_connection(
      client, 1, true, [](const android_stream::Endpoint &) { return true; });
  require(client.connect(endpoint(2)));
  require(lifecycle.activate(2));
  require(android_stream::MsQuicClientTestAccess::shutdown_initiated_by_peer(
              client) == QUIC_STATUS_SUCCESS);
  require(callbacks.loss_generations == std::vector<std::uint64_t>{1});
  require(!lifecycle.is_current(callbacks.loss_generations.front()));
}

void current_generation_receives_one_production_loss() {
  ProductionCallbacks callbacks;
  android_stream::MsQuicClient client(callbacks);
  android_stream::MsQuicClientTestAccess::prime_connection(
      client, 7, false, [](const android_stream::Endpoint &) { return true; });
  require(android_stream::MsQuicClientTestAccess::shutdown_initiated_by_transport(
              client) == QUIC_STATUS_SUCCESS);
  require(android_stream::MsQuicClientTestAccess::shutdown_initiated_by_peer(
              client) == QUIC_STATUS_SUCCESS);
  require(callbacks.loss_generations == std::vector<std::uint64_t>{7});
}

void local_stream_start_rejections_report_one_production_loss() {
  ProductionCallbacks unexpected_id_callbacks;
  android_stream::MsQuicClient unexpected_id_client(unexpected_id_callbacks);
  android_stream::MsQuicClientTestAccess::prime_connection(
      unexpected_id_client, 11, false,
      [](const android_stream::Endpoint &) { return true; });
  android_stream::MsQuicClientTestAccess::reject_stream_start(
      unexpected_id_client, android_stream::StreamRole::input,
      QUIC_STATUS_SUCCESS, 6, 11);
  android_stream::MsQuicClientTestAccess::reject_stream_start(
      unexpected_id_client, android_stream::StreamRole::input,
      QUIC_STATUS_SUCCESS, 6, 11);
  require(unexpected_id_callbacks.loss_generations ==
          std::vector<std::uint64_t>{11});

  ProductionCallbacks failed_status_callbacks;
  android_stream::MsQuicClient failed_status_client(failed_status_callbacks);
  android_stream::MsQuicClientTestAccess::prime_connection(
      failed_status_client, 12, false,
      [](const android_stream::Endpoint &) { return true; });
  android_stream::MsQuicClientTestAccess::reject_stream_start(
      failed_status_client, android_stream::StreamRole::feedback,
      QUIC_STATUS_CONNECTION_REFUSED, 6, 12);
  require(failed_status_callbacks.loss_generations ==
          std::vector<std::uint64_t>{12});
}
#endif

void production_callbacks_copy_receive_and_defer_shutdown_cleanup() {
  ProductionCallbacks callbacks;
  android_stream::MsQuicClient client(callbacks);
  constexpr std::array bytes{std::byte{1}, std::byte{2}, std::byte{3}};
  require(android_stream::MsQuicClientTestAccess::receive_session(
              client, bytes) == QUIC_STATUS_SUCCESS);
  require(callbacks.received == std::vector<std::byte>(bytes.begin(), bytes.end()));

  std::atomic<bool> callback_returned{false};
  callbacks.callback_returned = &callback_returned;
  require(android_stream::MsQuicClientTestAccess::shutdown_complete_for_release(
              client, callback_returned) == QUIC_STATUS_SUCCESS);
  std::unique_lock lock(callbacks.mutex);
  callbacks.changed.wait(lock, [&] { return callbacks.closed; });
  require(callbacks.cleanup_observed_callback_return);
}

void production_stream_ids_are_exact() {
  require(android_stream::expected_stream_id(android_stream::StreamRole::session) == 0);
  require(android_stream::expected_stream_id(android_stream::StreamRole::input) == 2);
  require(android_stream::expected_stream_id(android_stream::StreamRole::feedback) == 6);
  require(android_stream::stream_id_matches(android_stream::StreamRole::session, 0));
  require(android_stream::stream_id_matches(android_stream::StreamRole::input, 2));
  require(android_stream::stream_id_matches(android_stream::StreamRole::feedback, 6));
  require(!android_stream::stream_id_matches(android_stream::StreamRole::feedback, 2));
  require(android_stream::validate_stream_start(
              android_stream::StreamRole::session, QUIC_STATUS_SUCCESS, 0) ==
          android_stream::StreamStartValidation::accepted);
  require(android_stream::validate_stream_start(
              android_stream::StreamRole::input, QUIC_STATUS_SUCCESS, 6) ==
          android_stream::StreamStartValidation::unexpected_id);
  require(android_stream::validate_stream_start(
              android_stream::StreamRole::feedback,
              QUIC_STATUS_CONNECTION_REFUSED, 6) ==
          android_stream::StreamStartValidation::failed_status);
}

void deferred_shutdown_covers_release_and_pending_reconnect() {
  android_stream::Endpoint endpoint;
  endpoint.host = "beacon.example";
  endpoint.port = 47990;
  const auto release = android_stream::select_shutdown_cleanup_action(true, endpoint);
  require(release.notify_closed);
  require(!release.reconnect.has_value());
  const auto reconnect = android_stream::select_shutdown_cleanup_action(false, endpoint);
  require(!reconnect.notify_closed);
  require(reconnect.reconnect.has_value());
  require(reconnect.reconnect->host == "beacon.example");
  const auto idle = android_stream::select_shutdown_cleanup_action(false, std::nullopt);
  require(!idle.notify_closed);
  require(!idle.reconnect.has_value());
}

void grant_mapping_matches_every_current_protocol_mode() {
  beacon::stream::v1::VideoCodec codec{};
  beacon::stream::v1::DynamicRange dynamic_range{};
  require(android_stream::grant_video_codec_enum_name("h264") ==
          "VIDEO_CODEC_H264");
  require(android_stream::grant_video_codec_enum_name("av1") ==
          "VIDEO_CODEC_AV1");
  require(android_stream::grant_dynamic_range_enum_name("sdr") ==
          "DYNAMIC_RANGE_SDR");
  require(android_stream::grant_dynamic_range_enum_name("hdr10") ==
          "DYNAMIC_RANGE_HDR10");
  require(android_stream::map_grant_video_codec("h264", codec));
  require(codec == beacon::stream::v1::VIDEO_CODEC_H264);
  require(android_stream::map_grant_video_codec("hevc", codec));
  require(codec == beacon::stream::v1::VIDEO_CODEC_HEVC);
  require(android_stream::map_grant_video_codec("av1", codec));
  require(codec == beacon::stream::v1::VIDEO_CODEC_AV1);
  require(android_stream::map_grant_dynamic_range("sdr", dynamic_range));
  require(dynamic_range == beacon::stream::v1::DYNAMIC_RANGE_SDR);
  require(android_stream::map_grant_dynamic_range("hdr10", dynamic_range));
  require(dynamic_range == beacon::stream::v1::DYNAMIC_RANGE_HDR10);
  require(!android_stream::map_grant_video_codec("vp9", codec));
  require(!android_stream::map_grant_dynamic_range("dolby_vision", dynamic_range));
  android_stream::SelectedVideo selected;
  require(android_stream::map_selected_video_grant(
      "av1", 2560, 1600, 120, 1, "hdr10", selected));
  require(selected.codec == beacon::stream::v1::VIDEO_CODEC_AV1);
  require(selected.width == 2560);
  require(selected.height == 1600);
  require(selected.fps_numerator == 120);
  require(selected.fps_denominator == 1);
  require(selected.dynamic_range == beacon::stream::v1::DYNAMIC_RANGE_HDR10);
}

void closing_registry_retains_until_callback_completion() {
  struct TestSession {};
  android_stream::SessionRegistry<TestSession> registry;
  auto session = std::make_shared<TestSession>();
  const auto handle = registry.add(session);

  require(registry.begin_close(handle) == session);
  require(!registry.find_active(handle));
  require(registry.retain(handle) == session);
  require(registry.size() == 1);

  std::mutex mutex;
  std::condition_variable changed;
  bool callback_entered = false;
  bool callback_may_complete = false;
  bool callback_completed = false;
  std::thread callback([&] {
    auto retained = registry.retain(handle);
    require(retained == session);
    {
      std::unique_lock lock(mutex);
      callback_entered = true;
      changed.notify_all();
      changed.wait(lock, [&] { return callback_may_complete; });
    }
    registry.finish_close(handle);
    {
      std::lock_guard lock(mutex);
      callback_completed = true;
      changed.notify_all();
    }
  });

  {
    std::unique_lock lock(mutex);
    changed.wait(lock, [&] { return callback_entered; });
    require(registry.size() == 1);
    require(registry.retain(handle) == session);
    callback_may_complete = true;
    changed.notify_all();
    changed.wait(lock, [&] { return callback_completed; });
  }
  callback.join();
  require(registry.size() == 0);
  require(!registry.retain(handle));
}

}  // namespace

int main() {
  cleanup_runs_only_after_callback_scope_exits();
  destruction_action_runs_after_cleanup_action_returns();
  close_waits_for_inflight_native_callback();
  surface_replacement_releases_every_acquisition_once();
  production_msquic_settings_use_liveness_heartbeat_without_idle_ownership();
  production_receive_uses_synchronous_ownership();
  send_buffers_are_securely_cleared_before_destruction();
  production_callbacks_copy_receive_and_defer_shutdown_cleanup();
#ifndef NDEBUG
  transport_shutdown_accepts_pending_generation_before_completion();
  explicit_shutdown_stale_loss_is_generation_filtered();
  current_generation_receives_one_production_loss();
  local_stream_start_rejections_report_one_production_loss();
#endif
  production_stream_ids_are_exact();
  deferred_shutdown_covers_release_and_pending_reconnect();
  grant_mapping_matches_every_current_protocol_mode();
  closing_registry_retains_until_callback_completion();
  return 0;
}
