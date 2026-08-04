#pragma once

#include "beacon/worker/audio/opus_audio_encoder.h"

#include <cstdint>
#include <functional>
#include <memory>
#include <span>
#include <vector>

namespace beacon::worker::audio {

struct CapturedAudioFrame {
  std::vector<float> interleaved_pcm;
  std::uint64_t presentation_time_us{};
};

using AudioFrameSink = std::function<void(CapturedAudioFrame)>;

enum class WasapiLoopbackStage {
  none,
  start_validation,
  stop_event_creation,
  audio_event_creation,
  capture_thread_creation,
  com_initialization,
  device_enumerator_creation,
  default_endpoint_lookup,
  audio_client_activation,
  audio_client_initialization,
  event_registration,
  capture_client_activation,
  capture_start,
  capture_wait,
  packet_query,
  packet_acquisition,
  packet_accumulation,
  packet_release,
  frame_callback,
};

struct WasapiLoopbackFailure {
  WasapiLoopbackStage stage{WasapiLoopbackStage::none};
  std::uint32_t native_code{};
};

using AudioCaptureFailureSink = std::function<void(WasapiLoopbackFailure)>;

[[nodiscard]] const char *
wasapi_loopback_stage_name(WasapiLoopbackStage stage) noexcept;

class AudioFrameAccumulator final {
public:
  explicit AudioFrameAccumulator(AudioFrameSink sink);

  [[nodiscard]] bool push(std::span<const float> interleaved_pcm,
                          std::uint32_t frame_count,
                          std::uint64_t presentation_time_us, bool silent,
                          bool discontinuity);
  void reset() noexcept;

private:
  AudioFrameSink sink_;
  std::vector<float> pending_pcm_;
  std::uint64_t pending_presentation_time_us_{};
};

class IAudioFrameCapture {
public:
  virtual ~IAudioFrameCapture() = default;

  [[nodiscard]] virtual bool
  start(AudioFrameSink sink, AudioCaptureFailureSink failure_sink = {}) = 0;
  virtual void stop() noexcept = 0;
  [[nodiscard]] virtual bool active() const noexcept = 0;
  [[nodiscard]] virtual WasapiLoopbackFailure failure() const noexcept = 0;
};

class WasapiLoopbackCapture final : public IAudioFrameCapture {
public:
  WasapiLoopbackCapture();
  ~WasapiLoopbackCapture();

  WasapiLoopbackCapture(const WasapiLoopbackCapture &) = delete;
  WasapiLoopbackCapture &operator=(const WasapiLoopbackCapture &) = delete;

  [[nodiscard]] bool start(AudioFrameSink sink,
                           AudioCaptureFailureSink failure_sink = {}) override;
  void stop() noexcept override;
  [[nodiscard]] bool active() const noexcept override;
  [[nodiscard]] WasapiLoopbackFailure failure() const noexcept override;

private:
  class Impl;
  std::unique_ptr<Impl> impl_;
};

} // namespace beacon::worker::audio
