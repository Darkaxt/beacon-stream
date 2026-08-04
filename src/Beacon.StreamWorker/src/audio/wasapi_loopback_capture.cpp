#include "beacon/worker/audio/wasapi_loopback_capture.h"

#include <Windows.h>
#include <audioclient.h>
#include <ks.h>
#include <ksmedia.h>
#include <mmdeviceapi.h>
#include <wrl/client.h>

#include <algorithm>
#include <future>
#include <limits>
#include <mutex>
#include <stdexcept>
#include <thread>
#include <utility>

namespace beacon::worker::audio {
namespace {

using Microsoft::WRL::ComPtr;

constexpr REFERENCE_TIME capture_buffer_duration_100ns{1'000'000};
constexpr DWORD capture_stream_flags{
    AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK |
    AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM |
    AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY | AUDCLNT_STREAMFLAGS_NOPERSIST};

WAVEFORMATEXTENSIBLE capture_format() noexcept {
  WAVEFORMATEXTENSIBLE format{};
  format.Format.wFormatTag = WAVE_FORMAT_EXTENSIBLE;
  format.Format.nChannels = static_cast<WORD>(opus_channel_count);
  format.Format.nSamplesPerSec = opus_sample_rate_hz;
  format.Format.wBitsPerSample = 32;
  format.Format.nBlockAlign = static_cast<WORD>(
      format.Format.nChannels * format.Format.wBitsPerSample / 8U);
  format.Format.nAvgBytesPerSec =
      format.Format.nSamplesPerSec * format.Format.nBlockAlign;
  format.Format.cbSize =
      static_cast<WORD>(sizeof(WAVEFORMATEXTENSIBLE) - sizeof(WAVEFORMATEX));
  format.Samples.wValidBitsPerSample = 32;
  format.dwChannelMask = SPEAKER_FRONT_LEFT | SPEAKER_FRONT_RIGHT;
  format.SubFormat = KSDATAFORMAT_SUBTYPE_IEEE_FLOAT;
  return format;
}

std::uint32_t native_code(HRESULT result) noexcept {
  return static_cast<std::uint32_t>(result);
}

} // namespace

const char *wasapi_loopback_stage_name(WasapiLoopbackStage stage) noexcept {
  switch (stage) {
  case WasapiLoopbackStage::none:
    return "none";
  case WasapiLoopbackStage::start_validation:
    return "start-validation";
  case WasapiLoopbackStage::stop_event_creation:
    return "stop-event-create";
  case WasapiLoopbackStage::audio_event_creation:
    return "audio-event-create";
  case WasapiLoopbackStage::capture_thread_creation:
    return "capture-thread-create";
  case WasapiLoopbackStage::com_initialization:
    return "com-initialize";
  case WasapiLoopbackStage::device_enumerator_creation:
    return "device-enumerator-create";
  case WasapiLoopbackStage::default_endpoint_lookup:
    return "default-endpoint-lookup";
  case WasapiLoopbackStage::audio_client_activation:
    return "audio-client-activate";
  case WasapiLoopbackStage::audio_client_initialization:
    return "audio-client-initialize";
  case WasapiLoopbackStage::event_registration:
    return "event-register";
  case WasapiLoopbackStage::capture_client_activation:
    return "capture-client-activate";
  case WasapiLoopbackStage::capture_start:
    return "capture-start";
  case WasapiLoopbackStage::capture_wait:
    return "capture-wait";
  case WasapiLoopbackStage::packet_query:
    return "packet-query";
  case WasapiLoopbackStage::packet_acquisition:
    return "packet-acquire";
  case WasapiLoopbackStage::packet_accumulation:
    return "packet-accumulate";
  case WasapiLoopbackStage::packet_release:
    return "packet-release";
  case WasapiLoopbackStage::frame_callback:
    return "frame-callback";
  }
  return "unknown";
}

AudioFrameAccumulator::AudioFrameAccumulator(AudioFrameSink sink)
    : sink_(std::move(sink)) {
  if (!sink_) {
    throw std::invalid_argument("Audio frame sink must be provided.");
  }
  pending_pcm_.reserve(opus_frame_interleaved_samples * 2);
}

bool AudioFrameAccumulator::push(std::span<const float> interleaved_pcm,
                                 std::uint32_t frame_count,
                                 std::uint64_t presentation_time_us,
                                 bool silent, bool discontinuity) {
  if (frame_count == 0 ||
      frame_count >
          std::numeric_limits<std::size_t>::max() / opus_channel_count) {
    return false;
  }
  const auto sample_count =
      static_cast<std::size_t>(frame_count) * opus_channel_count;
  if ((!silent && interleaved_pcm.size() != sample_count) ||
      (silent && !interleaved_pcm.empty())) {
    return false;
  }

  if (discontinuity) {
    reset();
  }
  if (pending_pcm_.empty()) {
    pending_presentation_time_us_ = presentation_time_us;
  }

  if (silent) {
    pending_pcm_.insert(pending_pcm_.end(), sample_count, 0.0F);
  } else {
    pending_pcm_.insert(pending_pcm_.end(), interleaved_pcm.begin(),
                        interleaved_pcm.end());
  }

  while (pending_pcm_.size() >= opus_frame_interleaved_samples) {
    CapturedAudioFrame frame{
        .interleaved_pcm = std::vector<float>(
            pending_pcm_.begin(),
            pending_pcm_.begin() + opus_frame_interleaved_samples),
        .presentation_time_us = pending_presentation_time_us_,
    };
    pending_pcm_.erase(pending_pcm_.begin(),
                       pending_pcm_.begin() + opus_frame_interleaved_samples);
    pending_presentation_time_us_ += opus_frame_duration_us;
    sink_(std::move(frame));
  }
  return true;
}

void AudioFrameAccumulator::reset() noexcept {
  pending_pcm_.clear();
  pending_presentation_time_us_ = 0;
}

class WasapiLoopbackCapture::Impl final {
public:
  ~Impl() { stop(); }

  [[nodiscard]] bool start(AudioFrameSink sink,
                           AudioCaptureFailureSink failure_sink) {
    if (!sink) {
      set_failure(WasapiLoopbackStage::start_validation,
                  static_cast<std::uint32_t>(E_INVALIDARG));
      return false;
    }

    std::future<bool> initialized;
    {
      std::lock_guard lock{mutex_};
      if (thread_.joinable()) {
        failure_ = {.stage = WasapiLoopbackStage::start_validation,
                    .native_code = static_cast<std::uint32_t>(
                        HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS))};
        return false;
      }
      failure_ = {};
      failure_reported_ = false;
      sink_ = std::move(sink);
      failure_sink_ = std::move(failure_sink);
      stop_event_ = CreateEventW(nullptr, TRUE, FALSE, nullptr);
      if (stop_event_ == nullptr) {
        failure_ = {.stage = WasapiLoopbackStage::stop_event_creation,
                    .native_code = GetLastError()};
        clear_callbacks_locked();
        return false;
      }
      audio_event_ = CreateEventW(nullptr, FALSE, FALSE, nullptr);
      if (audio_event_ == nullptr) {
        failure_ = {.stage = WasapiLoopbackStage::audio_event_creation,
                    .native_code = GetLastError()};
        close_handles_locked();
        clear_callbacks_locked();
        return false;
      }

      std::promise<bool> promise;
      initialized = promise.get_future();
      try {
        thread_ = std::thread([this, promise = std::move(promise)]() mutable {
          run(std::move(promise));
        });
      } catch (...) {
        failure_ = {.stage = WasapiLoopbackStage::capture_thread_creation,
                    .native_code = static_cast<std::uint32_t>(E_FAIL)};
        close_handles_locked();
        clear_callbacks_locked();
        return false;
      }
    }

    const bool started = initialized.get();
    if (!started) {
      join_thread();
      std::lock_guard lock{mutex_};
      close_handles_locked();
      clear_callbacks_locked();
    }
    return started;
  }

  void stop() noexcept {
    HANDLE stop_event = nullptr;
    {
      std::lock_guard lock{mutex_};
      stop_event = stop_event_;
    }
    if (stop_event != nullptr) {
      static_cast<void>(SetEvent(stop_event));
    }
    join_thread();
    std::lock_guard lock{mutex_};
    active_ = false;
    close_handles_locked();
    clear_callbacks_locked();
  }

  [[nodiscard]] bool active() const noexcept {
    std::lock_guard lock{mutex_};
    return active_;
  }

  [[nodiscard]] WasapiLoopbackFailure failure() const noexcept {
    std::lock_guard lock{mutex_};
    return failure_;
  }

private:
  void run(std::promise<bool> initialized) noexcept {
    bool initialization_reported = false;
    const auto report_initialization = [&initialized,
                                        &initialization_reported](bool result) {
      if (!initialization_reported) {
        initialized.set_value(result);
        initialization_reported = true;
      }
    };
    HRESULT com_result = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(com_result)) {
      set_failure(WasapiLoopbackStage::com_initialization,
                  native_code(com_result));
      report_initialization(false);
      return;
    }

    {
      ComPtr<IMMDeviceEnumerator> enumerator;
      ComPtr<IMMDevice> endpoint;
      ComPtr<IAudioClient> audio_client;
      ComPtr<IAudioCaptureClient> capture_client;
      bool capture_started = false;

      const auto fail_initialization =
          [this, &report_initialization](WasapiLoopbackStage stage,
                                         HRESULT result) {
            set_failure(stage, native_code(result));
            report_initialization(false);
          };

      HRESULT result =
          CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL,
                           IID_PPV_ARGS(enumerator.ReleaseAndGetAddressOf()));
      if (FAILED(result)) {
        fail_initialization(WasapiLoopbackStage::device_enumerator_creation,
                            result);
      } else if (FAILED(result = enumerator->GetDefaultAudioEndpoint(
                            eRender, eConsole,
                            endpoint.ReleaseAndGetAddressOf()))) {
        fail_initialization(WasapiLoopbackStage::default_endpoint_lookup,
                            result);
      } else if (FAILED(result = endpoint->Activate(
                            __uuidof(IAudioClient), CLSCTX_ALL, nullptr,
                            reinterpret_cast<void **>(
                                audio_client.ReleaseAndGetAddressOf())))) {
        fail_initialization(WasapiLoopbackStage::audio_client_activation,
                            result);
      } else {
        auto format = capture_format();
        result = audio_client->Initialize(
            AUDCLNT_SHAREMODE_SHARED, capture_stream_flags,
            capture_buffer_duration_100ns, 0, &format.Format, nullptr);
        if (FAILED(result)) {
          fail_initialization(WasapiLoopbackStage::audio_client_initialization,
                              result);
        } else if (FAILED(result =
                              audio_client->SetEventHandle(audio_event_))) {
          fail_initialization(WasapiLoopbackStage::event_registration, result);
        } else if (FAILED(result = audio_client->GetService(IID_PPV_ARGS(
                              capture_client.ReleaseAndGetAddressOf())))) {
          fail_initialization(WasapiLoopbackStage::capture_client_activation,
                              result);
        } else if (FAILED(result = audio_client->Start())) {
          fail_initialization(WasapiLoopbackStage::capture_start, result);
        } else {
          capture_started = true;
          {
            std::lock_guard lock{mutex_};
            active_ = true;
          }
          report_initialization(true);
          capture_loop(*capture_client.Get());
        }
      }

      if (capture_started) {
        static_cast<void>(audio_client->Stop());
      }
    }

    {
      std::lock_guard lock{mutex_};
      active_ = false;
    }
    report_initialization(false);
    CoUninitialize();
  }

  void capture_loop(IAudioCaptureClient &capture_client) noexcept {
    AudioFrameAccumulator accumulator([this](CapturedAudioFrame frame) {
      AudioFrameSink sink;
      {
        std::lock_guard lock{mutex_};
        sink = sink_;
      }
      if (!sink) {
        return;
      }
      try {
        sink(std::move(frame));
      } catch (...) {
        fail_runtime(WasapiLoopbackStage::frame_callback,
                     static_cast<std::uint32_t>(E_FAIL));
        throw;
      }
    });

    const HANDLE events[]{stop_event_, audio_event_};
    bool running = true;
    while (running) {
      const DWORD wait_result =
          WaitForMultipleObjects(2, events, FALSE, INFINITE);
      if (wait_result == WAIT_OBJECT_0) {
        break;
      }
      if (wait_result != WAIT_OBJECT_0 + 1U) {
        fail_runtime(WasapiLoopbackStage::capture_wait,
                     wait_result == WAIT_FAILED ? GetLastError() : wait_result);
        break;
      }

      while (running) {
        UINT32 next_packet_frames = 0;
        HRESULT result = capture_client.GetNextPacketSize(&next_packet_frames);
        if (FAILED(result)) {
          fail_runtime(WasapiLoopbackStage::packet_query, native_code(result));
          break;
        }
        if (next_packet_frames == 0) {
          break;
        }

        BYTE *data = nullptr;
        UINT32 frame_count = 0;
        DWORD flags = 0;
        UINT64 device_position = 0;
        UINT64 qpc_position_100ns = 0;
        result = capture_client.GetBuffer(
            &data, &frame_count, &flags, &device_position, &qpc_position_100ns);
        if (FAILED(result)) {
          fail_runtime(WasapiLoopbackStage::packet_acquisition,
                       native_code(result));
          break;
        }

        const bool silent = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;
        const bool discontinuity =
            (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) != 0;
        bool accumulated = false;
        try {
          const auto samples = silent
                                   ? std::span<const float>{}
                                   : std::span<const float>{
                                         reinterpret_cast<const float *>(data),
                                         static_cast<std::size_t>(frame_count) *
                                             opus_channel_count};
          accumulated =
              accumulator.push(samples, frame_count, qpc_position_100ns / 10U,
                               silent, discontinuity);
        } catch (...) {
          accumulated = false;
        }

        result = capture_client.ReleaseBuffer(frame_count);
        if (FAILED(result)) {
          fail_runtime(WasapiLoopbackStage::packet_release,
                       native_code(result));
          running = false;
        } else if (!accumulated) {
          fail_runtime(WasapiLoopbackStage::packet_accumulation,
                       static_cast<std::uint32_t>(E_INVALIDARG));
          running = false;
        }
      }
      if (failure().stage != WasapiLoopbackStage::none) {
        running = false;
      }
    }
  }

  void set_failure(WasapiLoopbackStage stage, std::uint32_t code) noexcept {
    std::lock_guard lock{mutex_};
    failure_ = {.stage = stage, .native_code = code};
  }

  void fail_runtime(WasapiLoopbackStage stage, std::uint32_t code) noexcept {
    AudioCaptureFailureSink failure_sink;
    {
      std::lock_guard lock{mutex_};
      if (failure_reported_) {
        return;
      }
      failure_reported_ = true;
      failure_ = {.stage = stage, .native_code = code};
      failure_sink = failure_sink_;
    }
    try {
      if (failure_sink) {
        failure_sink({.stage = stage, .native_code = code});
      }
    } catch (...) {
    }
  }

  void join_thread() noexcept {
    try {
      if (thread_.joinable() &&
          thread_.get_id() != std::this_thread::get_id()) {
        thread_.join();
      }
    } catch (...) {
    }
  }

  void close_handles_locked() noexcept {
    if (audio_event_ != nullptr) {
      static_cast<void>(CloseHandle(audio_event_));
      audio_event_ = nullptr;
    }
    if (stop_event_ != nullptr) {
      static_cast<void>(CloseHandle(stop_event_));
      stop_event_ = nullptr;
    }
  }

  void clear_callbacks_locked() {
    sink_ = {};
    failure_sink_ = {};
  }

  mutable std::mutex mutex_;
  std::thread thread_;
  HANDLE stop_event_{};
  HANDLE audio_event_{};
  AudioFrameSink sink_;
  AudioCaptureFailureSink failure_sink_;
  WasapiLoopbackFailure failure_{};
  bool active_{};
  bool failure_reported_{};
};

WasapiLoopbackCapture::WasapiLoopbackCapture()
    : impl_(std::make_unique<Impl>()) {}

WasapiLoopbackCapture::~WasapiLoopbackCapture() = default;

bool WasapiLoopbackCapture::start(AudioFrameSink sink,
                                  AudioCaptureFailureSink failure_sink) {
  return impl_->start(std::move(sink), std::move(failure_sink));
}

void WasapiLoopbackCapture::stop() noexcept { impl_->stop(); }

bool WasapiLoopbackCapture::active() const noexcept { return impl_->active(); }

WasapiLoopbackFailure WasapiLoopbackCapture::failure() const noexcept {
  return impl_->failure();
}

} // namespace beacon::worker::audio
