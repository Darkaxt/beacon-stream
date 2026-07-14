#include "certificate_pin.h"
#include "callback_gate.h"
#include "grant_mapping.h"
#include "deferred_action_queue.h"
#include "lifecycle_generation.h"
#include "msquic_client.h"
#include "session_registry.h"
#include "stream_core.h"
#include "surface_owner.h"

#include <android/native_window.h>
#include <android/native_window_jni.h>
#include <jni.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <stdexcept>
#include <thread>
#include <unordered_map>
#include <utility>
#include <vector>

namespace beacon::android::streamcore {
namespace {

JavaVM *java_vm{};

class PendingJniException final {};

void check_jni(JNIEnv *environment) {
  if (environment->ExceptionCheck()) throw PendingJniException{};
}

template <typename T>
T require_jni_ref(JNIEnv *environment, T value, const char *message) {
  check_jni(environment);
  if (value == nullptr) throw std::invalid_argument(message);
  return value;
}

void throw_java(JNIEnv *environment, const char *class_name,
                const char *message) {
  if (environment == nullptr || environment->ExceptionCheck()) return;
  jclass type = environment->FindClass(class_name);
  if (environment->ExceptionCheck() || type == nullptr) return;
  environment->ThrowNew(type, message);
  environment->DeleteLocalRef(type);
}

std::string java_string(JNIEnv *environment, jobject object,
                        const char *field_name) {
  if (object == nullptr) throw std::invalid_argument("Java object is required.");
  jclass type = require_jni_ref(
      environment, environment->GetObjectClass(object), "Java object class is unavailable.");
  jfieldID field = require_jni_ref(
      environment,
      environment->GetFieldID(type, field_name, "Ljava/lang/String;"),
      "Required Java string field is unavailable.");
  auto value = static_cast<jstring>(environment->GetObjectField(object, field));
  check_jni(environment);
  if (value == nullptr) {
    environment->DeleteLocalRef(type);
    throw std::invalid_argument("Required Java string field is null.");
  }
  const char *characters = environment->GetStringUTFChars(value, nullptr);
  check_jni(environment);
  if (characters == nullptr) {
    environment->DeleteLocalRef(value);
    environment->DeleteLocalRef(type);
    throw std::invalid_argument("Could not read required Java string.");
  }
  std::string result;
  try {
    result.assign(characters);
  } catch (...) {
    environment->ReleaseStringUTFChars(value, characters);
    environment->DeleteLocalRef(value);
    environment->DeleteLocalRef(type);
    throw;
  }
  environment->ReleaseStringUTFChars(value, characters);
  environment->DeleteLocalRef(value);
  environment->DeleteLocalRef(type);
  return result;
}

jint java_int(JNIEnv *environment, jobject object, const char *field_name) {
  if (object == nullptr) throw std::invalid_argument("Java object is required.");
  jclass type = require_jni_ref(
      environment, environment->GetObjectClass(object), "Java object class is unavailable.");
  jfieldID field = require_jni_ref(
      environment, environment->GetFieldID(type, field_name, "I"),
      "Required Java integer field is unavailable.");
  const jint result = environment->GetIntField(object, field);
  check_jni(environment);
  environment->DeleteLocalRef(type);
  return result;
}

jlong java_long(JNIEnv *environment, jobject object, const char *field_name) {
  if (object == nullptr) throw std::invalid_argument("Java object is required.");
  jclass type = require_jni_ref(
      environment, environment->GetObjectClass(object), "Java object class is unavailable.");
  jfieldID field = require_jni_ref(
      environment, environment->GetFieldID(type, field_name, "J"),
      "Required Java long field is unavailable.");
  const jlong result = environment->GetLongField(object, field);
  check_jni(environment);
  environment->DeleteLocalRef(type);
  return result;
}

std::vector<std::byte> java_bytes(JNIEnv *environment, jobject object,
                                  const char *field_name) {
  if (object == nullptr) throw std::invalid_argument("Java object is required.");
  jclass type = require_jni_ref(
      environment, environment->GetObjectClass(object), "Java object class is unavailable.");
  jfieldID field = require_jni_ref(
      environment, environment->GetFieldID(type, field_name, "[B"),
      "Required Java byte-array field is unavailable.");
  auto value = static_cast<jbyteArray>(environment->GetObjectField(object, field));
  check_jni(environment);
  if (value == nullptr) {
    environment->DeleteLocalRef(type);
    throw std::invalid_argument("Required Java byte-array field is null.");
  }
  const jsize size = environment->GetArrayLength(value);
  check_jni(environment);
  std::vector<std::byte> result(static_cast<std::size_t>(size));
  if (size != 0) {
    environment->GetByteArrayRegion(
        value, 0, size, reinterpret_cast<jbyte *>(result.data()));
    check_jni(environment);
  }
  environment->DeleteLocalRef(value);
  environment->DeleteLocalRef(type);
  return result;
}

jobject java_object(JNIEnv *environment, jobject object, const char *field_name,
                    const char *signature) {
  if (object == nullptr) throw std::invalid_argument("Java object is required.");
  jclass type = require_jni_ref(
      environment, environment->GetObjectClass(object), "Java object class is unavailable.");
  jfieldID field = require_jni_ref(
      environment, environment->GetFieldID(type, field_name, signature),
      "Required Java object field is unavailable.");
  jobject result = environment->GetObjectField(object, field);
  check_jni(environment);
  environment->DeleteLocalRef(type);
  if (result == nullptr) throw std::invalid_argument("Required Java object field is null.");
  return result;
}

jobject java_optional_object(JNIEnv *environment, jobject object,
                             const char *field_name,
                             const char *signature) {
  if (object == nullptr) throw std::invalid_argument("Java object is required.");
  jclass type = require_jni_ref(
      environment, environment->GetObjectClass(object),
      "Java object class is unavailable.");
  jfieldID field = require_jni_ref(
      environment, environment->GetFieldID(type, field_name, signature),
      "Required Java object field is unavailable.");
  jobject result = environment->GetObjectField(object, field);
  check_jni(environment);
  environment->DeleteLocalRef(type);
  return result;
}

class JniStreamSession;
using JniStreamSessionRegistry = SessionRegistry<JniStreamSession>;

JniStreamSessionRegistry &sessions() {
  static JniStreamSessionRegistry registry;
  return registry;
}

class JniStreamSession final : public MsQuicClientCallbacks,
                               public FrameSink {
 public:
  JniStreamSession(JNIEnv *environment, jobject callbacks)
      : transport_(*this), core_(transport_, *this),
        surface_([](void *window) {
          ANativeWindow_release(static_cast<ANativeWindow *>(window));
        }) {
    if (callbacks == nullptr) {
      throw std::invalid_argument("Native callbacks are required.");
    }
    callbacks_ = require_jni_ref(
        environment, environment->NewGlobalRef(callbacks),
        "Could not retain native callbacks.");
    try {
      jclass type = require_jni_ref(
          environment, environment->GetObjectClass(callbacks),
          "Native callback class is unavailable.");
      frame_method_ = require_jni_ref(
          environment,
          environment->GetMethodID(
              type, "onFrame", "(Ljava/nio/ByteBuffer;JJJZZ)V"),
          "Native frame callback is unavailable.");
      loss_method_ = require_jni_ref(
          environment,
          environment->GetMethodID(type, "onConnectionLost", "(J)V"),
          "Native connection-loss callback is unavailable.");
      benchmark_method_ = require_jni_ref(
          environment,
          environment->GetMethodID(
              type, "onBenchmarkCompleted", "(D[J[I[J[J[I[ZJ)V"),
          "Native benchmark callback is unavailable.");
      environment->DeleteLocalRef(type);

      jclass local_byte_buffer_class = require_jni_ref(
          environment, environment->FindClass("java/nio/ByteBuffer"),
          "ByteBuffer class is unavailable.");
      byte_buffer_class_ = static_cast<jclass>(require_jni_ref(
          environment, environment->NewGlobalRef(local_byte_buffer_class),
          "Could not retain ByteBuffer class."));
      environment->DeleteLocalRef(local_byte_buffer_class);
      allocate_direct_method_ = require_jni_ref(
          environment,
          environment->GetStaticMethodID(
              byte_buffer_class_, "allocateDirect", "(I)Ljava/nio/ByteBuffer;"),
          "ByteBuffer.allocateDirect is unavailable.");
    } catch (...) {
      if (byte_buffer_class_ != nullptr) {
        environment->DeleteGlobalRef(byte_buffer_class_);
        byte_buffer_class_ = nullptr;
      }
      environment->DeleteGlobalRef(callbacks_);
      callbacks_ = nullptr;
      throw;
    }
  }

  ~JniStreamSession() override {
    surface_.close();
    if (java_vm == nullptr) return;
    JNIEnv *environment = nullptr;
    bool attached = false;
    if (java_vm->GetEnv(reinterpret_cast<void **>(&environment), JNI_VERSION_1_6) != JNI_OK) {
      if (java_vm->AttachCurrentThread(&environment, nullptr) == JNI_OK) attached = true;
    }
    if (environment != nullptr && callbacks_ != nullptr) {
      environment->DeleteGlobalRef(callbacks_);
      callbacks_ = nullptr;
    }
    if (environment != nullptr && byte_buffer_class_ != nullptr) {
      environment->DeleteGlobalRef(byte_buffer_class_);
      byte_buffer_class_ = nullptr;
    }
    if (attached) java_vm->DetachCurrentThread();
  }

  void set_handle(std::uint64_t handle) noexcept { handle_ = handle; }

  bool start(ConnectionGrant grant) {
    std::lock_guard lock(mutex_);
    const auto generation = grant.endpoint.generation;
    if (closed_ || !lifecycle_.can_activate(generation) ||
        !core_.start(std::move(grant))) {
      return false;
    }
    lifecycle_.activate(generation);
    callback_generation_.store(generation, std::memory_order_release);
    return true;
  }

  bool send_input(const stream::v1::InputBatch &input) {
    std::lock_guard lock(mutex_);
    return !closed_ && core_.send_input(input);
  }

  bool send_feedback(std::uint64_t generation,
                     const stream::v1::QueueDepthFeedback &feedback) {
    std::lock_guard lock(mutex_);
    return !closed_ && lifecycle_.is_current(generation) &&
           core_.send_feedback(feedback);
  }

  bool send_feedback(std::uint64_t generation,
                     const stream::v1::DecoderFeedback &feedback) {
    std::lock_guard lock(mutex_);
    return !closed_ && lifecycle_.is_current(generation) &&
           core_.send_feedback(feedback);
  }

  bool send_feedback(
      std::uint64_t generation,
      const stream::v1::RenderedFrameFeedback &feedback) {
    std::lock_guard lock(mutex_);
    return !closed_ && lifecycle_.is_current(generation) &&
           core_.send_feedback(feedback);
  }

  bool request_decoder_idr(std::uint64_t generation,
                           std::uint64_t last_complete_sequence) {
    std::lock_guard lock(mutex_);
    return !closed_ && lifecycle_.is_current(generation) &&
           core_.request_idr(
               stream::v1::IDR_REQUEST_REASON_DECODER_RESET,
               last_complete_sequence);
  }

  void replace_surface(JNIEnv *environment, jobject surface) {
    ANativeWindow *replacement = nullptr;
    if (surface != nullptr) {
      replacement = ANativeWindow_fromSurface(environment, surface);
      check_jni(environment);
      if (replacement == nullptr) {
        throw std::invalid_argument("Could not acquire Android Surface.");
      }
    }
    std::lock_guard lock(mutex_);
    if (closed_) {
      if (replacement != nullptr) ANativeWindow_release(replacement);
      return;
    }
    surface_.replace(replacement);
  }

  void stop() noexcept {
    try {
      std::lock_guard lock(mutex_);
      if (!closed_) core_.stop();
    } catch (...) {
    }
  }

  void close() noexcept {
    try {
      {
        std::lock_guard lock(mutex_);
        if (closed_) return;
        closed_ = true;
        callback_generation_.store(0, std::memory_order_release);
      }
      try {
        java_callbacks_.close_and_wait();
      } catch (...) {
      }
      std::lock_guard lock(mutex_);
      surface_.close();
      core_.release();
    } catch (...) {
      surface_.close();
      core_.release();
    }
  }

  void transport_connected(std::uint64_t generation) override {
    auto retained = sessions().retain(handle_);
    if (!retained) return;
    bool local_failure = false;
    {
      std::lock_guard lock(mutex_);
      if (!closed_ && lifecycle_.is_current(generation)) {
        local_failure = !core_.on_connected();
      }
    }
    if (local_failure) transport_.report_local_failure(generation);
  }

  void session_bytes(std::uint64_t generation,
                     std::vector<std::byte> bytes) override {
    auto retained = sessions().retain(handle_);
    if (!retained) return;
    std::optional<BenchmarkCollectionResult> benchmark_result;
    {
      std::lock_guard lock(mutex_);
      if (!closed_ && lifecycle_.is_current(generation)) {
        core_.receive_session(bytes);
        benchmark_result = core_.take_benchmark_result();
      }
    }
    if (benchmark_result.has_value()) {
      try {
        const auto handle = handle_;
        DeferredActionQueue::instance().enqueue(
            destruction_barrier_,
            [handle, generation,
             result = std::move(*benchmark_result)]() mutable {
              auto session = sessions().retain(handle);
              if (session) {
                session->dispatch_benchmark_to_java(generation,
                                                    std::move(result));
              }
            });
      } catch (...) {
      }
    }
  }

  void media_datagram(std::uint64_t generation,
                      std::vector<std::byte> bytes) override {
    auto retained = sessions().retain(handle_);
    if (!retained) return;
    std::lock_guard lock(mutex_);
    if (!closed_ && lifecycle_.is_current(generation)) {
      core_.receive_datagram(bytes);
    }
  }

  void connection_lost(std::uint64_t generation) override {
    auto retained = sessions().retain(handle_);
    if (!retained) return;
    {
      std::lock_guard lock(mutex_);
      if (closed_ || !lifecycle_.is_current(generation)) return;
      core_.on_connection_lost();
    }
    try {
      const auto handle = handle_;
      DeferredActionQueue::instance().enqueue(
          destruction_barrier_, [handle, generation] {
            auto session = sessions().retain(handle);
            if (session) session->dispatch_loss_to_java(generation);
          });
    } catch (...) {
    }
  }

  void transport_closed(std::uint64_t) noexcept override {
    try {
      const auto handle = handle_;
      DeferredActionQueue::instance().enqueue(
          destruction_barrier_,
          [handle] { sessions().finish_close(handle); });
    } catch (...) {
    }
  }

  void frame(EncodedFrame frame) override {
    try {
      const auto handle = handle_;
      const auto generation = callback_generation_.load(std::memory_order_acquire);
      DeferredActionQueue::instance().enqueue(
          destruction_barrier_,
          [handle, generation, frame = std::move(frame)]() mutable {
            auto session = sessions().retain(handle);
            if (session) {
              session->dispatch_frame_to_java(generation, std::move(frame));
            }
          });
    } catch (...) {
    }
  }

  void state_changed(State) noexcept override {}

#ifndef NDEBUG
  void benchmark_for_test(std::uint64_t generation) {
    callback_generation_.store(generation, std::memory_order_release);
    dispatch_benchmark_to_java(
        generation,
        BenchmarkCollectionResult{
            .sustainable_throughput_mbps = 96.5,
            .received_datagrams = 1,
            .samples = {
                {.sequence = 0,
                 .payload_bytes = 1000,
                 .rtt_us = 2000,
                 .jitter_us = 0,
                 .reorder_distance = 0,
                 .received = true},
                {.sequence = 1,
                 .payload_bytes = 1000,
                 .rtt_us = 2500,
                 .jitter_us = 300,
                 .reorder_distance = 1,
                 .received = false}}});
  }
#endif

 private:
  static void clear_callback_exception(JNIEnv *environment) noexcept {
    if (environment != nullptr && environment->ExceptionCheck()) {
      environment->ExceptionDescribe();
      environment->ExceptionClear();
    }
  }

  void dispatch_frame_to_java(std::uint64_t generation,
                              EncodedFrame frame) noexcept {
    if (callback_generation_.load(std::memory_order_acquire) != generation) return;
    auto callback = java_callbacks_.try_enter();
    if (!callback.has_value()) return;
    JNIEnv *environment = nullptr;
    bool attached = false;
    if (java_vm == nullptr) return;
    if (java_vm->GetEnv(reinterpret_cast<void **>(&environment),
                        JNI_VERSION_1_6) != JNI_OK) {
      if (java_vm->AttachCurrentThread(&environment, nullptr) != JNI_OK) return;
      attached = true;
    }
    if (frame.bytes.size() >
        static_cast<std::size_t>(std::numeric_limits<jint>::max())) {
      if (attached) java_vm->DetachCurrentThread();
      return;
    }
    jobject bytes = environment->CallStaticObjectMethod(
        byte_buffer_class_, allocate_direct_method_,
        static_cast<jint>(frame.bytes.size()));
    if (bytes == nullptr || environment->ExceptionCheck()) {
      clear_callback_exception(environment);
      if (attached) java_vm->DetachCurrentThread();
      return;
    }
    if (!frame.bytes.empty()) {
      void *destination = environment->GetDirectBufferAddress(bytes);
      if (destination == nullptr || environment->ExceptionCheck()) {
        clear_callback_exception(environment);
        environment->DeleteLocalRef(bytes);
        if (attached) java_vm->DetachCurrentThread();
        return;
      }
      std::memcpy(destination, frame.bytes.data(), frame.bytes.size());
    }
    if (!environment->ExceptionCheck()) {
      environment->CallVoidMethod(callbacks_, frame_method_, bytes,
                                  static_cast<jlong>(frame.presentation_time_us),
                                  static_cast<jlong>(frame.sequence),
                                  static_cast<jlong>(generation),
                                  static_cast<jboolean>(frame.idr),
                                  static_cast<jboolean>(frame.codec_configuration));
    }
    clear_callback_exception(environment);
    environment->DeleteLocalRef(bytes);
    if (attached) java_vm->DetachCurrentThread();
  }

  void dispatch_benchmark_to_java(
      std::uint64_t generation,
      BenchmarkCollectionResult result) noexcept {
    if (callback_generation_.load(std::memory_order_acquire) != generation) return;
    auto callback = java_callbacks_.try_enter();
    if (!callback.has_value()) return;
    JNIEnv *environment = nullptr;
    bool attached = false;
    if (java_vm == nullptr) return;
    if (java_vm->GetEnv(reinterpret_cast<void **>(&environment),
                        JNI_VERSION_1_6) != JNI_OK) {
      if (java_vm->AttachCurrentThread(&environment, nullptr) != JNI_OK) return;
      attached = true;
    }

    const auto count = static_cast<jsize>(result.samples.size());
    jlongArray sequences = environment->NewLongArray(count);
    jintArray payload_bytes = environment->NewIntArray(count);
    jlongArray rtt_us = environment->NewLongArray(count);
    jlongArray jitter_us = environment->NewLongArray(count);
    jintArray reorder_distances = environment->NewIntArray(count);
    jbooleanArray received = environment->NewBooleanArray(count);
    if (sequences == nullptr || payload_bytes == nullptr || rtt_us == nullptr ||
        jitter_us == nullptr || reorder_distances == nullptr ||
        received == nullptr || environment->ExceptionCheck()) {
      clear_callback_exception(environment);
    } else {
      std::vector<jlong> sequence_values(result.samples.size());
      std::vector<jint> payload_values(result.samples.size());
      std::vector<jlong> rtt_values(result.samples.size());
      std::vector<jlong> jitter_values(result.samples.size());
      std::vector<jint> reorder_values(result.samples.size());
      std::vector<jboolean> received_values(result.samples.size());
      for (std::size_t index = 0; index < result.samples.size(); ++index) {
        const BenchmarkNetworkSample &sample = result.samples[index];
        sequence_values[index] = static_cast<jlong>(sample.sequence);
        payload_values[index] = static_cast<jint>(sample.payload_bytes);
        rtt_values[index] = static_cast<jlong>(sample.rtt_us);
        jitter_values[index] = static_cast<jlong>(sample.jitter_us);
        reorder_values[index] = static_cast<jint>(sample.reorder_distance);
        received_values[index] = sample.received ? JNI_TRUE : JNI_FALSE;
      }
      environment->SetLongArrayRegion(
          sequences, 0, count, sequence_values.data());
      environment->SetIntArrayRegion(
          payload_bytes, 0, count, payload_values.data());
      environment->SetLongArrayRegion(rtt_us, 0, count, rtt_values.data());
      environment->SetLongArrayRegion(
          jitter_us, 0, count, jitter_values.data());
      environment->SetIntArrayRegion(
          reorder_distances, 0, count, reorder_values.data());
      environment->SetBooleanArrayRegion(
          received, 0, count, received_values.data());
      if (!environment->ExceptionCheck()) {
        environment->CallVoidMethod(
            callbacks_, benchmark_method_,
            static_cast<jdouble>(result.sustainable_throughput_mbps),
            sequences, payload_bytes, rtt_us, jitter_us, reorder_distances,
            received, static_cast<jlong>(generation));
      }
      clear_callback_exception(environment);
    }
    if (sequences != nullptr) environment->DeleteLocalRef(sequences);
    if (payload_bytes != nullptr) environment->DeleteLocalRef(payload_bytes);
    if (rtt_us != nullptr) environment->DeleteLocalRef(rtt_us);
    if (jitter_us != nullptr) environment->DeleteLocalRef(jitter_us);
    if (reorder_distances != nullptr) {
      environment->DeleteLocalRef(reorder_distances);
    }
    if (received != nullptr) environment->DeleteLocalRef(received);
    if (attached) java_vm->DetachCurrentThread();
  }

  void dispatch_loss_to_java(std::uint64_t generation) noexcept {
    if (callback_generation_.load(std::memory_order_acquire) != generation) return;
    auto callback = java_callbacks_.try_enter();
    if (!callback.has_value()) return;
    JNIEnv *environment = nullptr;
    bool attached = false;
    if (java_vm == nullptr) return;
    if (java_vm->GetEnv(reinterpret_cast<void **>(&environment),
                        JNI_VERSION_1_6) != JNI_OK) {
      if (java_vm->AttachCurrentThread(&environment, nullptr) != JNI_OK) return;
      attached = true;
    }
    environment->CallVoidMethod(callbacks_, loss_method_,
                                static_cast<jlong>(generation));
    clear_callback_exception(environment);
    if (attached) java_vm->DetachCurrentThread();
  }

  std::mutex mutex_;
  jobject callbacks_{};
  jclass byte_buffer_class_{};
  jmethodID frame_method_{};
  jmethodID loss_method_{};
  jmethodID benchmark_method_{};
  jmethodID allocate_direct_method_{};
  std::uint64_t handle_{};
  MsQuicClient transport_;
  StreamCore core_;
  SurfaceOwner surface_;
  CallbackGate java_callbacks_;
  LifecycleGeneration lifecycle_;
  std::atomic<std::uint64_t> callback_generation_{};
  std::shared_ptr<CallbackBarrier> destruction_barrier_{
      std::make_shared<CallbackBarrier>()};
  bool closed_{};
};

ConnectionGrant parse_grant(JNIEnv *environment, jobject native_grant) {
  if (native_grant == nullptr) {
    throw std::invalid_argument("Beacon connection grant is required.");
  }
  ConnectionGrant grant;
  grant.endpoint.host = java_string(environment, native_grant, "host");
  const jint port = java_int(environment, native_grant, "port");
  if (port <= 0 || port > 65535) {
    throw std::invalid_argument("Connection grant port is invalid.");
  }
  grant.endpoint.port = static_cast<std::uint16_t>(port);
  const jlong generation = java_long(environment, native_grant, "generation");
  if (generation <= 0) {
    throw std::invalid_argument("Connection generation is invalid.");
  }
  grant.endpoint.generation = static_cast<std::uint64_t>(generation);
  const std::string pin = java_string(environment, native_grant, "publicKeyFingerprint");
  if (!decode_sha256_pin(pin, grant.endpoint.spki_pin)) {
    throw std::invalid_argument("Invalid SHA-256 SPKI pin.");
  }
  grant.client_id = java_string(environment, native_grant, "clientId");
  grant.session_id = java_string(environment, native_grant, "sessionId");
  grant.plan_revision = static_cast<std::uint64_t>(java_long(
      environment, native_grant, "planRevision"));
  grant.plan_explanation = java_string(environment, native_grant, "planExplanation");
  grant.ticket = TicketSecret(java_bytes(environment, native_grant, "ticket"));
  if (grant.ticket.bytes().empty()) {
    throw std::invalid_argument("Connection grant ticket is empty.");
  }
  jobject video = java_optional_object(
      environment, native_grant, "selectedVideo",
      "Ldev/beacon/android/BeaconStreamSession$SelectedVideo;");
  jobject benchmark = java_optional_object(
      environment, native_grant, "benchmark",
      "Ldev/beacon/android/BeaconStreamSession$Benchmark;");
  if ((video == nullptr) == (benchmark == nullptr)) {
    if (video != nullptr) environment->DeleteLocalRef(video);
    if (benchmark != nullptr) environment->DeleteLocalRef(benchmark);
    throw std::invalid_argument(
        "Connection grant must contain exactly one video or benchmark mode.");
  }

  if (video != nullptr) {
    const std::string codec_text = java_string(environment, video, "codec");
    const std::string dynamic_range_text =
        java_string(environment, video, "dynamicRange");
    const jint width = java_int(environment, video, "width");
    const jint height = java_int(environment, video, "height");
    const jint fps_numerator = java_int(
        environment, video, "framesPerSecondNumerator");
    const jint fps_denominator = java_int(
        environment, video, "framesPerSecondDenominator");
    if (width <= 0 || height <= 0 || fps_numerator <= 0 ||
        fps_denominator <= 0) {
      environment->DeleteLocalRef(video);
      throw std::invalid_argument(
          "Connection grant video dimensions or frame rate are invalid.");
    }
    if (!map_selected_video_grant(
            codec_text, static_cast<std::uint32_t>(width),
            static_cast<std::uint32_t>(height),
            static_cast<std::uint32_t>(fps_numerator),
            static_cast<std::uint32_t>(fps_denominator), dynamic_range_text,
            grant.video)) {
      environment->DeleteLocalRef(video);
      throw std::invalid_argument(
          "Connection grant selects a mode absent from the stream protocol contract.");
    }
    environment->DeleteLocalRef(video);
  } else {
    BenchmarkGrant mapped;
    mapped.run_id = java_string(environment, benchmark, "runId");
    const jint schema_version = java_int(environment, benchmark, "schemaVersion");
    jobject reliable = java_object(
        environment, benchmark, "reliableRound",
        "Ldev/beacon/android/BeaconStreamSession$BenchmarkRound;");
    jobject datagram = java_object(
        environment, benchmark, "datagramRound",
        "Ldev/beacon/android/BeaconStreamSession$BenchmarkRound;");
    const jint reliable_packet_count =
        java_int(environment, reliable, "packetCount");
    const jint reliable_payload_bytes =
        java_int(environment, reliable, "payloadBytes");
    const jlong reliable_interval =
        java_long(environment, reliable, "measurementIntervalUs");
    const jint datagram_packet_count =
        java_int(environment, datagram, "packetCount");
    const jint datagram_payload_bytes =
        java_int(environment, datagram, "payloadBytes");
    const jlong datagram_interval =
        java_long(environment, datagram, "measurementIntervalUs");
    auto run_token = java_bytes(environment, benchmark, "runToken");
    environment->DeleteLocalRef(reliable);
    environment->DeleteLocalRef(datagram);
    environment->DeleteLocalRef(benchmark);
    if (mapped.run_id.empty() || schema_version <= 0 ||
        reliable_packet_count <= 0 || reliable_payload_bytes <= 0 ||
        reliable_interval <= 0 || datagram_packet_count <= 0 ||
        datagram_payload_bytes <= 0 || datagram_interval <= 0 ||
        run_token.size() != mapped.run_token.size()) {
      throw std::invalid_argument("Connection grant benchmark plan is invalid.");
    }
    std::copy(run_token.begin(), run_token.end(), mapped.run_token.begin());
    std::fill(run_token.begin(), run_token.end(), std::byte{});
    mapped.schema_version = static_cast<std::uint32_t>(schema_version);
    mapped.reliable_packet_count =
        static_cast<std::uint32_t>(reliable_packet_count);
    mapped.reliable_payload_bytes =
        static_cast<std::uint32_t>(reliable_payload_bytes);
    mapped.reliable_measurement_interval_us =
        static_cast<std::uint64_t>(reliable_interval);
    mapped.datagram_packet_count =
        static_cast<std::uint32_t>(datagram_packet_count);
    mapped.datagram_payload_bytes =
        static_cast<std::uint32_t>(datagram_payload_bytes);
    mapped.datagram_measurement_interval_us =
        static_cast<std::uint64_t>(datagram_interval);
    grant.benchmark = std::move(mapped);
  }
  return grant;
}

stream::v1::InputBatch parse_input(JNIEnv *environment, jobject input) {
  if (input == nullptr) throw std::invalid_argument("Beacon input batch is required.");
  stream::v1::InputBatch result;
  jobject events_object = java_object(
      environment, input, "events", "[Ldev/beacon/android/BeaconApiClient$InputEvent;");
  auto events = static_cast<jobjectArray>(events_object);
  const jsize count = environment->GetArrayLength(events);
  check_jni(environment);
  for (jsize index = 0; index < count; ++index) {
    jobject event = environment->GetObjectArrayElement(events, index);
    check_jni(environment);
    if (event == nullptr) {
      environment->DeleteLocalRef(events);
      throw std::invalid_argument("Beacon input event is null.");
    }
    const std::string type = java_string(environment, event, "type");
    const std::string action = java_string(environment, event, "action");
    if (type == "keyboard" && action == "press") {
      const std::string code = java_string(environment, event, "code");
      if (code != "Escape") {
        environment->DeleteLocalRef(event);
        environment->DeleteLocalRef(events);
        throw std::invalid_argument("Unsupported Beacon keyboard input.");
      }
      auto *pressed = result.add_events()->mutable_keyboard();
      pressed->set_scan_code(1);
      pressed->set_pressed(true);
      auto *released = result.add_events()->mutable_keyboard();
      released->set_scan_code(1);
      released->set_pressed(false);
    } else if (type == "pointer" &&
               (action == "tap" || action == "down" ||
                action == "up" || action == "move")) {
      jclass event_type = environment->GetObjectClass(event);
      event_type = require_jni_ref(
          environment, event_type, "Beacon input event class is unavailable.");
      const jfieldID x_field = require_jni_ref(
          environment,
          environment->GetFieldID(event_type, "x", "Ljava/lang/Double;"),
          "Beacon pointer x field is unavailable.");
      const jfieldID y_field = require_jni_ref(
          environment,
          environment->GetFieldID(event_type, "y", "Ljava/lang/Double;"),
          "Beacon pointer y field is unavailable.");
      jobject x_value = environment->GetObjectField(event, x_field);
      check_jni(environment);
      jobject y_value = environment->GetObjectField(event, y_field);
      check_jni(environment);
      if (x_value == nullptr || y_value == nullptr) {
        if (x_value != nullptr) environment->DeleteLocalRef(x_value);
        if (y_value != nullptr) environment->DeleteLocalRef(y_value);
        environment->DeleteLocalRef(event_type);
        environment->DeleteLocalRef(event);
        environment->DeleteLocalRef(events);
        throw std::invalid_argument("Beacon pointer coordinates are required.");
      }
      jclass double_type = require_jni_ref(
          environment, environment->FindClass("java/lang/Double"),
          "java.lang.Double is unavailable.");
      jmethodID double_value = require_jni_ref(
          environment, environment->GetMethodID(double_type, "doubleValue", "()D"),
          "java.lang.Double.doubleValue is unavailable.");
      const jdouble raw_x = environment->CallDoubleMethod(x_value, double_value);
      check_jni(environment);
      const jdouble raw_y = environment->CallDoubleMethod(y_value, double_value);
      check_jni(environment);
      const auto x = static_cast<std::int32_t>(std::lround(
          std::clamp(raw_x, 0.0, 1.0) * 65535.0));
      const auto y = static_cast<std::int32_t>(std::lround(
          std::clamp(raw_y, 0.0, 1.0) * 65535.0));
      std::vector<stream::v1::PointerAction> actions;
      if (action == "tap") {
        actions = {stream::v1::POINTER_ACTION_BUTTON_DOWN,
                   stream::v1::POINTER_ACTION_BUTTON_UP};
      } else if (action == "down") {
        actions = {stream::v1::POINTER_ACTION_BUTTON_DOWN};
      } else if (action == "up") {
        actions = {stream::v1::POINTER_ACTION_BUTTON_UP};
      } else {
        actions = {stream::v1::POINTER_ACTION_MOVE};
      }
      for (const auto pointer_action : actions) {
        auto *pointer = result.add_events()->mutable_pointer();
        pointer->set_action(pointer_action);
        pointer->set_x(x);
        pointer->set_y(y);
        pointer->set_button(1);
      }
      environment->DeleteLocalRef(double_type);
      environment->DeleteLocalRef(x_value);
      environment->DeleteLocalRef(y_value);
      environment->DeleteLocalRef(event_type);
    } else {
      environment->DeleteLocalRef(event);
      environment->DeleteLocalRef(events);
      throw std::invalid_argument("Unsupported Beacon input event.");
    }
    environment->DeleteLocalRef(event);
  }
  environment->DeleteLocalRef(events);
  return result;
}

}  // namespace
}  // namespace beacon::android::streamcore

using beacon::android::streamcore::ConnectionGrant;
using beacon::android::streamcore::JniStreamSession;
using beacon::android::streamcore::sessions;

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM *vm, void *) {
  if (vm == nullptr) return JNI_ERR;
  beacon::android::streamcore::java_vm = vm;
  return JNI_VERSION_1_6;
}

extern "C" JNIEXPORT jlong JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeCreate(
    JNIEnv *environment, jclass, jobject callbacks) {
  try {
    auto session = std::make_shared<JniStreamSession>(environment, callbacks);
    const auto handle = sessions().add(session);
    session->set_handle(handle);
    return static_cast<jlong>(handle);
  } catch (const beacon::android::streamcore::PendingJniException &) {
    return 0;
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon native StreamCore.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native StreamCore creation failure.");
  }
  return 0;
}

extern "C" JNIEXPORT jboolean JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeStart(
    JNIEnv *environment, jclass, jlong handle, jobject native_grant) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return JNI_FALSE;
  try {
    ConnectionGrant grant = beacon::android::streamcore::parse_grant(environment, native_grant);
    return session->start(std::move(grant)) ? JNI_TRUE : JNI_FALSE;
  } catch (const beacon::android::streamcore::PendingJniException &) {
    return JNI_FALSE;
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon connection grant.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native start failure.");
  }
  return JNI_FALSE;
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeSendInput(
    JNIEnv *environment, jclass, jlong handle, jobject input) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return;
  try {
    auto batch = beacon::android::streamcore::parse_input(environment, input);
    if (!session->send_input(batch)) {
      beacon::android::streamcore::throw_java(
          environment, "java/lang/IllegalStateException", "Beacon StreamCore is not streaming.");
    }
  } catch (const beacon::android::streamcore::PendingJniException &) {
    return;
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon input batch.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native input failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeSendQueueDepthFeedback(
    JNIEnv *environment, jclass, jlong handle, jlong generation,
    jint queued_access_units, jlong dropped_access_units) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return;
  try {
    if (generation <= 0 || queued_access_units < 0 || dropped_access_units < 0) {
      throw std::invalid_argument(
          "Queue-depth feedback generation and values must be valid.");
    }
    beacon::stream::v1::QueueDepthFeedback feedback;
    feedback.set_queued_access_units(
        static_cast<std::uint32_t>(queued_access_units));
    feedback.set_dropped_access_units(
        static_cast<std::uint64_t>(dropped_access_units));
    if (!session->send_feedback(static_cast<std::uint64_t>(generation), feedback)) {
      beacon::android::streamcore::throw_java(
          environment, "java/lang/IllegalStateException",
          "Beacon StreamCore is not streaming.");
    }
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon queue-depth feedback.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native feedback failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeSendDecoderFeedback(
    JNIEnv *environment, jclass, jlong handle, jlong generation,
    jint state, jint platform_error_code) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return;
  try {
    if (generation <= 0 || platform_error_code < 0 || state < 1 || state > 3) {
      throw std::invalid_argument("Decoder feedback values must be valid.");
    }
    beacon::stream::v1::DecoderFeedback feedback;
    feedback.set_state(
        static_cast<beacon::stream::v1::DecoderState>(state));
    feedback.set_platform_error_code(
        static_cast<std::uint32_t>(platform_error_code));
    if (!session->send_feedback(
            static_cast<std::uint64_t>(generation), feedback)) {
      beacon::android::streamcore::throw_java(
          environment, "java/lang/IllegalStateException",
          "Beacon StreamCore is not streaming.");
    }
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon decoder feedback.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native decoder-feedback failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeSendRenderedFrameFeedback(
    JNIEnv *environment, jclass, jlong handle, jlong generation,
    jlong frame_sequence, jlong presentation_time_us, jlong rendered_at_us) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return;
  try {
    if (generation <= 0 || frame_sequence <= 0 ||
        presentation_time_us < 0 || rendered_at_us < 0) {
      throw std::invalid_argument(
          "Rendered-frame feedback values must be valid.");
    }
    beacon::stream::v1::RenderedFrameFeedback feedback;
    feedback.set_frame_sequence(static_cast<std::uint64_t>(frame_sequence));
    feedback.set_presentation_time_us(
        static_cast<std::uint64_t>(presentation_time_us));
    feedback.set_rendered_at_us(static_cast<std::uint64_t>(rendered_at_us));
    if (!session->send_feedback(
            static_cast<std::uint64_t>(generation), feedback)) {
      beacon::android::streamcore::throw_java(
          environment, "java/lang/IllegalStateException",
          "Beacon StreamCore is not streaming.");
    }
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon rendered-frame feedback.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native rendered-frame feedback failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeRequestDecoderIdr(
    JNIEnv *environment, jclass, jlong handle, jlong generation,
    jlong last_complete_sequence) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return;
  try {
    if (generation <= 0 || last_complete_sequence < 0) {
      throw std::invalid_argument("Decoder IDR request values must be valid.");
    }
    if (!session->request_decoder_idr(
            static_cast<std::uint64_t>(generation),
            static_cast<std::uint64_t>(last_complete_sequence))) {
      beacon::android::streamcore::throw_java(
          environment, "java/lang/IllegalStateException",
          "Beacon StreamCore is not streaming.");
    }
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon decoder IDR request.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native decoder IDR request failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeReplaceSurface(
    JNIEnv *environment, jclass, jlong handle, jobject surface) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return;
  try {
    session->replace_surface(environment, surface);
  } catch (const beacon::android::streamcore::PendingJniException &) {
    return;
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon Surface replacement failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeStop(
    JNIEnv *environment, jclass, jlong handle) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) return;
  try {
    session->stop();
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native stop failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeRelease(
    JNIEnv *environment, jclass, jlong handle) {
  try {
    auto session = sessions().begin_close(static_cast<std::uint64_t>(handle));
    if (!session) return;
    session->close();
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native release failure.");
  }
}

#ifndef NDEBUG
extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeTestEmitFrame(
    JNIEnv *environment, jclass, jlong handle, jbyteArray bytes,
    jlong presentation_time_us) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalStateException",
        "Beacon native test handle is unavailable.");
    return;
  }
  try {
    if (bytes == nullptr) {
      throw std::invalid_argument("Beacon native test frame is required.");
    }
    const jsize size = environment->GetArrayLength(bytes);
    beacon::android::streamcore::check_jni(environment);
    std::vector<std::byte> copied(static_cast<std::size_t>(size));
    if (size != 0) {
      environment->GetByteArrayRegion(
          bytes, 0, size, reinterpret_cast<jbyte *>(copied.data()));
      beacon::android::streamcore::check_jni(environment);
    }
    session->frame({.bytes = std::move(copied),
                    .presentation_time_us =
                        static_cast<std::uint64_t>(presentation_time_us),
                    .sequence = 1,
                    .idr = true,
                    .codec_configuration = true});
  } catch (const beacon::android::streamcore::PendingJniException &) {
    return;
  } catch (const std::bad_alloc &) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/OutOfMemoryError",
        "Could not allocate Beacon native test frame.");
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native test callback failure.");
  }
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeTestAwaitRegistryIdle(
    JNIEnv *, jclass) {
  sessions().wait_until_empty();
}

extern "C" JNIEXPORT jint JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeTestRegistrySize(
    JNIEnv *, jclass) {
  return static_cast<jint>(sessions().size());
}

extern "C" JNIEXPORT jboolean JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeTestParseGrant(
    JNIEnv *environment, jclass, jobject native_grant) {
  try {
    ConnectionGrant grant =
        beacon::android::streamcore::parse_grant(environment, native_grant);
    return grant.benchmark.has_value() &&
                   grant.video.codec ==
                       beacon::stream::v1::VIDEO_CODEC_UNSPECIFIED
               ? JNI_TRUE
               : JNI_FALSE;
  } catch (const beacon::android::streamcore::PendingJniException &) {
    return JNI_FALSE;
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native benchmark grant test failure.");
  }
  return JNI_FALSE;
}

extern "C" JNIEXPORT void JNICALL
Java_dev_beacon_android_BeaconStreamCore_nativeTestEmitBenchmarkResult(
    JNIEnv *environment, jclass, jlong handle, jlong generation) {
  auto session = sessions().find_active(static_cast<std::uint64_t>(handle));
  if (!session || generation <= 0) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException",
        "Beacon native benchmark test session is invalid.");
    return;
  }
  try {
    session->benchmark_for_test(static_cast<std::uint64_t>(generation));
  } catch (const std::exception &error) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/IllegalArgumentException", error.what());
  } catch (...) {
    beacon::android::streamcore::throw_java(
        environment, "java/lang/RuntimeException",
        "Unexpected Beacon native benchmark callback test failure.");
  }
}
#endif
