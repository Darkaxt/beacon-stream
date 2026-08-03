#include "beacon/worker/audio/opus_audio_encoder.h"

#include <opus.h>

#include <limits>

namespace beacon::worker::audio {

OpusAudioEncoder::OpusAudioEncoder() {
  encoder_ =
      opus_encoder_create(static_cast<opus_int32>(opus_sample_rate_hz),
                          static_cast<int>(opus_channel_count),
                          OPUS_APPLICATION_RESTRICTED_LOWDELAY, &opus_error_);
  if (encoder_ == nullptr || opus_error_ != OPUS_OK) {
    failure_ = OpusAudioEncoderFailure::create_failed;
    return;
  }
  if (opus_encoder_ctl(encoder_, OPUS_SET_BITRATE(opus_bitrate_bps)) !=
          OPUS_OK ||
      opus_encoder_ctl(encoder_, OPUS_SET_VBR(0)) != OPUS_OK ||
      opus_encoder_ctl(encoder_, OPUS_SET_SIGNAL(OPUS_SIGNAL_MUSIC)) !=
          OPUS_OK) {
    failure_ = OpusAudioEncoderFailure::configure_failed;
  }
}

OpusAudioEncoder::~OpusAudioEncoder() {
  if (encoder_ != nullptr) {
    opus_encoder_destroy(encoder_);
  }
}

bool OpusAudioEncoder::ready() const noexcept {
  return encoder_ != nullptr &&
         failure_ != OpusAudioEncoderFailure::create_failed &&
         failure_ != OpusAudioEncoderFailure::configure_failed;
}

std::optional<std::vector<std::uint8_t>>
OpusAudioEncoder::encode(std::span<const float> interleaved_pcm) noexcept {
  if (!ready()) {
    return std::nullopt;
  }
  if (interleaved_pcm.size() != opus_frame_interleaved_samples) {
    failure_ = OpusAudioEncoderFailure::invalid_pcm_frame;
    return std::nullopt;
  }
  try {
    std::vector<std::uint8_t> encoded(maximum_opus_packet_bytes);
    const int encoded_bytes = opus_encode_float(
        encoder_, interleaved_pcm.data(),
        static_cast<int>(opus_frame_samples_per_channel), encoded.data(),
        static_cast<opus_int32>(encoded.size()));
    if (encoded_bytes <= 0) {
      opus_error_ = encoded_bytes;
      failure_ = OpusAudioEncoderFailure::encode_failed;
      return std::nullopt;
    }
    encoded.resize(static_cast<std::size_t>(encoded_bytes));
    opus_error_ = OPUS_OK;
    failure_ = OpusAudioEncoderFailure::none;
    return encoded;
  } catch (...) {
    opus_error_ = OPUS_ALLOC_FAIL;
    failure_ = OpusAudioEncoderFailure::encode_failed;
    return std::nullopt;
  }
}

OpusAudioEncoderFailure OpusAudioEncoder::failure() const noexcept {
  return failure_;
}

int OpusAudioEncoder::opus_error() const noexcept { return opus_error_; }

} // namespace beacon::worker::audio
