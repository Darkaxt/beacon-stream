#include "opus_audio_decoder.h"

#include <opus.h>

#include <limits>
#include <new>
#include <utility>

namespace beacon::android::streamcore {

OpusAudioDecoder::OpusAudioDecoder() {
  decoder_ = opus_decoder_create(
      static_cast<opus_int32>(opus_sample_rate_hz),
      static_cast<int>(opus_channel_count), &opus_error_);
  if (decoder_ == nullptr || opus_error_ != OPUS_OK) {
    failure_ = OpusAudioDecoderFailure::create_failed;
  }
}

OpusAudioDecoder::~OpusAudioDecoder() {
  if (decoder_ != nullptr) opus_decoder_destroy(decoder_);
}

bool OpusAudioDecoder::ready() const noexcept {
  return decoder_ != nullptr &&
         failure_ != OpusAudioDecoderFailure::create_failed;
}

AudioDecodeResult OpusAudioDecoder::decode(
    std::span<const std::byte> encoded_packet, std::uint64_t sequence,
    std::uint64_t presentation_time_us) noexcept {
  if (!ready() || sequence == 0 || encoded_packet.empty() ||
      encoded_packet.size() >
          static_cast<std::size_t>(std::numeric_limits<opus_int32>::max())) {
    failure_ = OpusAudioDecoderFailure::invalid_packet;
    return {.status = AudioDecodeStatus::failed, .frames = {}};
  }
  if (last_sequence_ != 0 && sequence <= last_sequence_) {
    failure_ = OpusAudioDecoderFailure::none;
    opus_error_ = OPUS_OK;
    return {.status = AudioDecodeStatus::ignored_stale, .frames = {}};
  }

  try {
    AudioDecodeResult result{
        .status = AudioDecodeStatus::decoded, .frames = {}};
    if (last_sequence_ != 0 && sequence > last_sequence_ + 2) {
      if (!reset()) {
        return {.status = AudioDecodeStatus::failed, .frames = {}};
      }
      result.status = AudioDecodeStatus::reset_after_gap;
    } else if (last_sequence_ != 0 && sequence == last_sequence_ + 2) {
      DecodedAudioFrame concealed;
      const auto concealed_time =
          presentation_time_us >= opus_frame_duration_us
              ? presentation_time_us - opus_frame_duration_us
              : 0;
      if (!decode_frame({}, sequence - 1, concealed_time, true, concealed)) {
        static_cast<void>(reset());
        return {.status = AudioDecodeStatus::failed, .frames = {}};
      }
      result.frames.push_back(std::move(concealed));
    }

    DecodedAudioFrame current;
    if (!decode_frame(encoded_packet, sequence, presentation_time_us, false,
                      current)) {
      static_cast<void>(reset());
      return {.status = AudioDecodeStatus::failed, .frames = {}};
    }
    result.frames.push_back(std::move(current));
    last_sequence_ = sequence;
    failure_ = OpusAudioDecoderFailure::none;
    opus_error_ = OPUS_OK;
    return result;
  } catch (const std::bad_alloc &) {
    failure_ = OpusAudioDecoderFailure::resource_exhausted;
  } catch (...) {
    failure_ = OpusAudioDecoderFailure::decode_failed;
  }
  return {.status = AudioDecodeStatus::failed, .frames = {}};
}

bool OpusAudioDecoder::reset() noexcept {
  if (!ready()) return false;
  opus_error_ = opus_decoder_ctl(decoder_, OPUS_RESET_STATE);
  if (opus_error_ != OPUS_OK) {
    failure_ = OpusAudioDecoderFailure::reset_failed;
    return false;
  }
  last_sequence_ = 0;
  failure_ = OpusAudioDecoderFailure::none;
  return true;
}

OpusAudioDecoderFailure OpusAudioDecoder::failure() const noexcept {
  return failure_;
}

int OpusAudioDecoder::opus_error() const noexcept { return opus_error_; }

bool OpusAudioDecoder::decode_frame(
    std::span<const std::byte> encoded_packet, std::uint64_t sequence,
    std::uint64_t presentation_time_us, bool concealed,
    DecodedAudioFrame &frame) {
  frame.interleaved_pcm.resize(opus_frame_interleaved_samples);
  const auto *packet = encoded_packet.empty()
                           ? nullptr
                           : reinterpret_cast<const unsigned char *>(
                                 encoded_packet.data());
  const int decoded_samples = opus_decode_float(
      decoder_, packet, static_cast<opus_int32>(encoded_packet.size()),
      frame.interleaved_pcm.data(),
      static_cast<int>(opus_frame_samples_per_channel), 0);
  if (decoded_samples != static_cast<int>(opus_frame_samples_per_channel)) {
    opus_error_ = decoded_samples;
    failure_ = OpusAudioDecoderFailure::decode_failed;
    return false;
  }
  frame.presentation_time_us = presentation_time_us;
  frame.sequence = sequence;
  frame.concealed = concealed;
  return true;
}

}  // namespace beacon::android::streamcore
