#pragma once

#include <cstddef>
#include <cstdint>
#include <memory>
#include <optional>
#include <span>
#include <vector>

struct OpusEncoder;

namespace beacon::worker::audio {

inline constexpr std::uint32_t opus_sample_rate_hz = 48'000;
inline constexpr std::uint32_t opus_channel_count = 2;
inline constexpr std::uint32_t opus_frame_duration_us = 20'000;
inline constexpr std::uint32_t opus_bitrate_bps = 96'000;
inline constexpr std::size_t opus_frame_samples_per_channel = 960;
inline constexpr std::size_t opus_frame_interleaved_samples =
    opus_frame_samples_per_channel * opus_channel_count;
inline constexpr std::size_t maximum_opus_packet_bytes = 1275;

enum class OpusAudioEncoderFailure {
  none,
  create_failed,
  configure_failed,
  invalid_pcm_frame,
  encode_failed,
};

class OpusAudioEncoder final {
public:
  OpusAudioEncoder();
  ~OpusAudioEncoder();

  OpusAudioEncoder(const OpusAudioEncoder &) = delete;
  OpusAudioEncoder &operator=(const OpusAudioEncoder &) = delete;

  [[nodiscard]] bool ready() const noexcept;
  [[nodiscard]] std::optional<std::vector<std::uint8_t>>
  encode(std::span<const float> interleaved_pcm) noexcept;
  [[nodiscard]] OpusAudioEncoderFailure failure() const noexcept;
  [[nodiscard]] int opus_error() const noexcept;

private:
  OpusEncoder *encoder_{};
  OpusAudioEncoderFailure failure_{OpusAudioEncoderFailure::none};
  int opus_error_{};
};

} // namespace beacon::worker::audio
