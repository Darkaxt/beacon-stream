#include "beacon/worker/audio/opus_audio_encoder.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <opus.h>

#include <cmath>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace {

namespace audio = beacon::worker::audio;

std::vector<float> sine_frame() {
  constexpr std::size_t samples_per_channel = 960;
  constexpr std::size_t channels = 2;
  constexpr double pi = 3.14159265358979323846;
  std::vector<float> result(samples_per_channel * channels);
  for (std::size_t frame = 0; frame < samples_per_channel; ++frame) {
    const auto value = static_cast<float>(
        std::sin(2.0 * pi * 440.0 * static_cast<double>(frame) / 48'000.0) *
        0.25);
    result[frame * channels] = value;
    result[frame * channels + 1] = value;
  }
  return result;
}

void fixed_r2_frame_encodes_and_decodes_as_opus() {
  audio::OpusAudioEncoder encoder;
  BEACON_TEST_REQUIRE(encoder.ready());

  const auto encoded = encoder.encode(sine_frame());

  BEACON_TEST_REQUIRE(encoded.has_value());
  BEACON_TEST_REQUIRE(!encoded->empty());
  BEACON_TEST_REQUIRE(encoded->size() <= audio::maximum_opus_packet_bytes);
  int opus_error = OPUS_OK;
  OpusDecoder *decoder = opus_decoder_create(48'000, 2, &opus_error);
  BEACON_TEST_REQUIRE(decoder != nullptr);
  BEACON_TEST_REQUIRE(opus_error == OPUS_OK);
  std::vector<float> decoded(960U * 2U);
  const int decoded_frames = opus_decode_float(
      decoder, encoded->data(), static_cast<opus_int32>(encoded->size()),
      decoded.data(), 960, 0);
  opus_decoder_destroy(decoder);
  BEACON_TEST_REQUIRE(decoded_frames == 960);
}

void partial_pcm_frames_are_rejected_without_codec_state_corruption() {
  audio::OpusAudioEncoder encoder;
  std::vector<float> partial(959U * 2U);

  BEACON_TEST_REQUIRE(!encoder.encode(partial).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      audio::OpusAudioEncoderFailure::invalid_pcm_frame);
  BEACON_TEST_REQUIRE(encoder.encode(sine_frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      audio::OpusAudioEncoderFailure::none);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    fixed_r2_frame_encodes_and_decodes_as_opus();
    partial_pcm_frames_are_rejected_without_codec_state_corruption();
  });
}
