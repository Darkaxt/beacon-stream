#pragma once

#include "benchmark_collector.h"
#include "opus_audio_decoder.h"
#include "stream_control.pb.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <span>
#include <string>
#include <vector>

namespace beacon::android::streamcore {

inline constexpr std::uint32_t maximum_planned_frame_bytes = 16U * 1024U * 1024U;
inline constexpr std::uint32_t minimum_planned_frame_bytes = 1024U * 1024U;

[[nodiscard]] std::uint32_t derive_maximum_frame_bytes(
    std::uint32_t width, std::uint32_t height) noexcept;

enum class StreamRole { session, input, feedback };
enum class State {
  idle,
  connecting,
  authenticating,
  benchmarking,
  streaming,
  stopped,
  failed,
  released
};

#ifndef NDEBUG
enum class StartFaultPoint {
  assembler_allocation,
};
using StartFaultHook = void (*)(StartFaultPoint);
void set_start_fault_hook_for_test(StartFaultHook hook) noexcept;

enum class CloseFaultPoint {
  stop_envelope_allocation,
  stop_serialization,
};
using CloseFaultHook = void (*)(CloseFaultPoint);
void set_close_fault_hook_for_test(CloseFaultHook hook) noexcept;
#endif

class TicketSecret {
 public:
  TicketSecret() = default;
  explicit TicketSecret(std::vector<std::byte> bytes);
  TicketSecret(TicketSecret &&other) noexcept;
  TicketSecret &operator=(TicketSecret &&other) noexcept;
  TicketSecret(const TicketSecret &) = delete;
  TicketSecret &operator=(const TicketSecret &) = delete;
  ~TicketSecret();

  [[nodiscard]] std::span<const std::byte> bytes() const noexcept;
  [[nodiscard]] bool consumed() const noexcept;
  void clear() noexcept;

 private:
  std::vector<std::byte> bytes_;
  bool consumed_{};
};

struct Endpoint {
  std::string host;
  std::uint16_t port{};
  std::array<std::byte, 32> spki_pin{};
  std::uint64_t generation{};

  bool operator==(const Endpoint &) const = default;
};

struct SelectedVideo {
  stream::v1::VideoCodec codec{stream::v1::VIDEO_CODEC_UNSPECIFIED};
  std::uint32_t width{};
  std::uint32_t height{};
  std::uint32_t fps_numerator{};
  std::uint32_t fps_denominator{};
  stream::v1::DynamicRange dynamic_range{stream::v1::DYNAMIC_RANGE_UNSPECIFIED};
};

struct SelectedAudio {
  stream::v1::AudioCodec codec{stream::v1::AUDIO_CODEC_UNSPECIFIED};
  std::uint32_t sample_rate_hz{};
  std::uint32_t channel_count{};
  std::uint32_t frame_duration_us{};
  std::uint32_t bitrate_bps{};
};

struct BenchmarkGrant {
  std::string run_id;
  std::array<std::byte, 16> run_token{};
  std::uint32_t schema_version{};
  std::uint32_t reliable_packet_count{};
  std::uint32_t reliable_payload_bytes{};
  std::uint64_t reliable_measurement_interval_us{};
  std::uint32_t datagram_packet_count{};
  std::uint32_t datagram_payload_bytes{};
  std::uint64_t datagram_measurement_interval_us{};
};

struct ConnectionGrant {
  Endpoint endpoint;
  std::string client_id;
  std::string session_id;
  std::uint64_t plan_revision{};
  std::string plan_explanation;
  TicketSecret ticket;
  SelectedVideo video;
  SelectedAudio audio;
  std::optional<BenchmarkGrant> benchmark;
};

struct EncodedFrame {
  std::vector<std::byte> bytes;
  std::uint64_t presentation_time_us{};
  std::uint64_t sequence{};
  bool idr{};
  bool codec_configuration{};
};

class Transport {
 public:
  virtual ~Transport() = default;
  virtual bool connect(const Endpoint &endpoint) = 0;
  virtual bool open_stream(StreamRole role) = 0;
  virtual bool send(StreamRole role, std::vector<std::byte> bytes) = 0;
  virtual bool send_final(StreamRole role, std::vector<std::byte> bytes) = 0;
  virtual void shutdown() = 0;
  virtual void release() = 0;
};

class FrameSink {
 public:
  virtual ~FrameSink() = default;
  virtual void frame(EncodedFrame frame) = 0;
  virtual void audio(DecodedAudioFrame frame) = 0;
  virtual void state_changed(State state) = 0;
};

class StreamCore {
 public:
  StreamCore(Transport &transport, FrameSink &sink,
             std::uint32_t maximum_frame_bytes = maximum_planned_frame_bytes);
  ~StreamCore();

  bool start(ConnectionGrant grant);
  bool on_connected();
  bool receive_session(std::span<const std::byte> bytes);
  bool receive_session(std::span<const std::byte> bytes,
                       std::uint64_t received_at_us);
  bool receive_datagram(std::span<const std::byte> bytes);
  bool receive_datagram(std::span<const std::byte> bytes,
                        std::uint64_t received_at_us);
  bool send_input(const stream::v1::InputBatch &input);
  bool send_feedback(const stream::v1::QueueDepthFeedback &feedback);
  bool send_feedback(const stream::v1::DecoderFeedback &feedback);
  bool send_feedback(const stream::v1::RenderedFrameFeedback &feedback);
  bool request_idr(stream::v1::IdrRequestReason reason,
                   std::uint64_t last_complete_sequence);
  void on_connection_lost();
  void stop() noexcept;
  void release() noexcept;

  [[nodiscard]] State state() const noexcept;
  [[nodiscard]] bool ticket_consumed() const noexcept;
  [[nodiscard]] std::optional<BenchmarkCollectionResult>
  take_benchmark_result();

 private:
  template <typename Message>
  static std::vector<std::byte> frame_message(const Message &message);
  bool send_authenticate();
  bool send_start();
  bool send_start_benchmark();
  bool flush_pending_decoder_feedback();
  bool drain_assembler_events();
  bool send_request_idr();
  bool send_benchmark_echo(const stream::BenchmarkDatagramHeader &header);
  void transition(State state);
  void fail();

  Transport &transport_;
  FrameSink &sink_;
  class FrameAssemblerHolder;
  std::unique_ptr<FrameAssemblerHolder> assembler_;
  std::unique_ptr<OpusAudioDecoder> audio_decoder_;
  BenchmarkCollector benchmark_collector_;
  std::uint32_t maximum_frame_bytes_{};
  ConnectionGrant grant_;
  std::vector<std::byte> session_bytes_;
  std::uint64_t session_sequence_{};
  std::uint64_t input_sequence_{};
  std::uint64_t feedback_sequence_{};
  std::uint64_t last_complete_sequence_{};
  std::uint64_t last_server_sequence_{};
  std::optional<stream::v1::DecoderFeedback> pending_decoder_feedback_;
  std::optional<BenchmarkCollectionResult> benchmark_result_;
  State state_{State::idle};
  bool shutdown_{};
  bool released_{};
};

}  // namespace beacon::android::streamcore
