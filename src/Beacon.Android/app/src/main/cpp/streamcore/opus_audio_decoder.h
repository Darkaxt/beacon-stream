#pragma once

#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

struct OpusDecoder;

namespace beacon::android::streamcore {

inline constexpr std::uint32_t opus_sample_rate_hz = 48'000;
inline constexpr std::uint32_t opus_channel_count = 2;
inline constexpr std::uint32_t opus_frame_duration_us = 20'000;
inline constexpr std::size_t opus_frame_samples_per_channel = 960;
inline constexpr std::size_t opus_frame_interleaved_samples =
    opus_frame_samples_per_channel * opus_channel_count;

enum class AudioDecodeStatus {
  decoded,
  ignored_stale,
  reset_after_gap,
  failed,
};

enum class OpusAudioDecoderFailure {
  none,
  create_failed,
  invalid_packet,
  reset_failed,
  decode_failed,
  resource_exhausted,
};

struct DecodedAudioFrame {
  std::vector<float> interleaved_pcm;
  std::uint64_t presentation_time_us{};
  std::uint64_t sequence{};
  bool concealed{};
};

struct AudioDecodeResult {
  AudioDecodeStatus status{AudioDecodeStatus::failed};
  std::vector<DecodedAudioFrame> frames;
};

class OpusAudioDecoder final {
 public:
  OpusAudioDecoder();
  ~OpusAudioDecoder();

  OpusAudioDecoder(const OpusAudioDecoder &) = delete;
  OpusAudioDecoder &operator=(const OpusAudioDecoder &) = delete;

  [[nodiscard]] bool ready() const noexcept;
  [[nodiscard]] AudioDecodeResult
  decode(std::span<const std::byte> encoded_packet, std::uint64_t sequence,
         std::uint64_t presentation_time_us) noexcept;
  [[nodiscard]] bool reset() noexcept;
  [[nodiscard]] OpusAudioDecoderFailure failure() const noexcept;
  [[nodiscard]] int opus_error() const noexcept;

 private:
  [[nodiscard]] bool decode_frame(std::span<const std::byte> encoded_packet,
                                  std::uint64_t sequence,
                                  std::uint64_t presentation_time_us,
                                  bool concealed,
                                  DecodedAudioFrame &frame);

  OpusDecoder *decoder_{};
  std::uint64_t last_sequence_{};
  OpusAudioDecoderFailure failure_{OpusAudioDecoderFailure::none};
  int opus_error_{};
};

}  // namespace beacon::android::streamcore
