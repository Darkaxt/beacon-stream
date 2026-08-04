#include "opus_audio_decoder.h"

#include "test_failure.h"

#include <opus.h>

#include <array>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace {

namespace android_stream = beacon::android::streamcore;

class TestEncoder final {
 public:
  TestEncoder() {
    int error = OPUS_OK;
    encoder_ = opus_encoder_create(
        static_cast<opus_int32>(android_stream::opus_sample_rate_hz),
        static_cast<int>(android_stream::opus_channel_count),
        OPUS_APPLICATION_RESTRICTED_LOWDELAY, &error);
    BEACON_TEST_REQUIRE(encoder_ != nullptr);
    BEACON_TEST_REQUIRE(error == OPUS_OK);
  }

  ~TestEncoder() {
    if (encoder_ != nullptr) opus_encoder_destroy(encoder_);
  }

  TestEncoder(const TestEncoder &) = delete;
  TestEncoder &operator=(const TestEncoder &) = delete;

  std::vector<std::byte> encode(float phase) {
    std::array<float, android_stream::opus_frame_interleaved_samples> pcm{};
    for (std::size_t frame = 0;
         frame < android_stream::opus_frame_samples_per_channel; ++frame) {
      const float sample = 0.2F * std::sin(
          phase + static_cast<float>(frame) * 0.03F);
      pcm[frame * 2] = sample;
      pcm[frame * 2 + 1] = sample;
    }
    std::array<unsigned char, 1275> packet{};
    const int bytes = opus_encode_float(
        encoder_, pcm.data(),
        static_cast<int>(android_stream::opus_frame_samples_per_channel),
        packet.data(), static_cast<opus_int32>(packet.size()));
    BEACON_TEST_REQUIRE(bytes > 0);
    const auto begin = reinterpret_cast<const std::byte *>(packet.data());
    return {begin, begin + bytes};
  }

 private:
  OpusEncoder *encoder_{};
};

void decodes_one_opus_packet_to_fixed_float_pcm() {
  TestEncoder encoder;
  android_stream::OpusAudioDecoder decoder;
  BEACON_TEST_REQUIRE(decoder.ready());

  const auto packet = encoder.encode(0.0F);
  const auto result = decoder.decode(packet, 7, 140'000);

  BEACON_TEST_REQUIRE(
      result.status == android_stream::AudioDecodeStatus::decoded);
  BEACON_TEST_REQUIRE(result.frames.size() == 1);
  BEACON_TEST_REQUIRE(result.frames[0].sequence == 7);
  BEACON_TEST_REQUIRE(result.frames[0].presentation_time_us == 140'000);
  BEACON_TEST_REQUIRE(!result.frames[0].concealed);
  BEACON_TEST_REQUIRE(
      result.frames[0].interleaved_pcm.size() ==
      android_stream::opus_frame_interleaved_samples);
}

void one_missing_packet_emits_plc_before_the_current_packet() {
  TestEncoder encoder;
  android_stream::OpusAudioDecoder decoder;
  BEACON_TEST_REQUIRE(
      decoder.decode(encoder.encode(0.0F), 1, 20'000).frames.size() == 1);

  const auto result = decoder.decode(encoder.encode(0.5F), 3, 60'000);

  BEACON_TEST_REQUIRE(
      result.status == android_stream::AudioDecodeStatus::decoded);
  BEACON_TEST_REQUIRE(result.frames.size() == 2);
  BEACON_TEST_REQUIRE(result.frames[0].sequence == 2);
  BEACON_TEST_REQUIRE(result.frames[0].presentation_time_us == 40'000);
  BEACON_TEST_REQUIRE(result.frames[0].concealed);
  BEACON_TEST_REQUIRE(result.frames[1].sequence == 3);
  BEACON_TEST_REQUIRE(!result.frames[1].concealed);
}

void a_larger_gap_resets_decoder_state_and_resumes_with_current_packet() {
  TestEncoder encoder;
  android_stream::OpusAudioDecoder decoder;
  BEACON_TEST_REQUIRE(
      decoder.decode(encoder.encode(0.0F), 1, 20'000).frames.size() == 1);

  const auto result = decoder.decode(encoder.encode(0.5F), 4, 80'000);

  BEACON_TEST_REQUIRE(
      result.status == android_stream::AudioDecodeStatus::reset_after_gap);
  BEACON_TEST_REQUIRE(result.frames.size() == 1);
  BEACON_TEST_REQUIRE(result.frames[0].sequence == 4);
  BEACON_TEST_REQUIRE(!result.frames[0].concealed);
}

void duplicate_and_out_of_order_packets_are_ignored() {
  TestEncoder encoder;
  android_stream::OpusAudioDecoder decoder;
  const auto first = encoder.encode(0.0F);
  BEACON_TEST_REQUIRE(decoder.decode(first, 3, 60'000).frames.size() == 1);

  const auto duplicate = decoder.decode(first, 3, 60'000);
  const auto older = decoder.decode(first, 2, 40'000);

  BEACON_TEST_REQUIRE(
      duplicate.status == android_stream::AudioDecodeStatus::ignored_stale);
  BEACON_TEST_REQUIRE(duplicate.frames.empty());
  BEACON_TEST_REQUIRE(
      older.status == android_stream::AudioDecodeStatus::ignored_stale);
  BEACON_TEST_REQUIRE(older.frames.empty());
}

void reset_forgets_sequence_ownership() {
  TestEncoder encoder;
  android_stream::OpusAudioDecoder decoder;
  const auto packet = encoder.encode(0.0F);
  BEACON_TEST_REQUIRE(decoder.decode(packet, 9, 180'000).frames.size() == 1);

  BEACON_TEST_REQUIRE(decoder.reset());
  const auto replayed = decoder.decode(packet, 9, 180'000);

  BEACON_TEST_REQUIRE(
      replayed.status == android_stream::AudioDecodeStatus::decoded);
  BEACON_TEST_REQUIRE(replayed.frames.size() == 1);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    decodes_one_opus_packet_to_fixed_float_pcm();
    one_missing_packet_emits_plc_before_the_current_packet();
    a_larger_gap_resets_decoder_state_and_resumes_with_current_packet();
    duplicate_and_out_of_order_packets_are_ignored();
    reset_forgets_sequence_ownership();
  });
}
