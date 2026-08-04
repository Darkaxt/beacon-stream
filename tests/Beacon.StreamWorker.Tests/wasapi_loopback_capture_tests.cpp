#include "beacon/worker/audio/wasapi_loopback_capture.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <Windows.h>

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace {

namespace audio = beacon::worker::audio;

std::vector<float> pcm(std::size_t frame_count, float value) {
  return std::vector<float>(frame_count * audio::opus_channel_count, value);
}

void arbitrary_capture_packets_form_exact_twenty_millisecond_frames() {
  std::vector<audio::CapturedAudioFrame> frames;
  audio::AudioFrameAccumulator accumulator(
      [&frames](audio::CapturedAudioFrame frame) {
        frames.push_back(std::move(frame));
      });

  const auto first = pcm(480, 0.25F);
  const auto second = pcm(480, 0.5F);
  BEACON_TEST_REQUIRE(accumulator.push(first, 480, 1'000'000, false, false));
  BEACON_TEST_REQUIRE(frames.empty());
  BEACON_TEST_REQUIRE(accumulator.push(second, 480, 1'010'000, false, false));

  BEACON_TEST_REQUIRE(frames.size() == 1);
  BEACON_TEST_REQUIRE(frames[0].presentation_time_us == 1'000'000);
  BEACON_TEST_REQUIRE(frames[0].interleaved_pcm.size() ==
                      audio::opus_frame_interleaved_samples);
  BEACON_TEST_REQUIRE(
      std::ranges::all_of(frames[0].interleaved_pcm.begin(),
                          frames[0].interleaved_pcm.begin() + 960,
                          [](float value) { return value == 0.25F; }));
  BEACON_TEST_REQUIRE(std::ranges::all_of(
      frames[0].interleaved_pcm.begin() + 960, frames[0].interleaved_pcm.end(),
      [](float value) { return value == 0.5F; }));
}

void one_large_capture_packet_emits_monotonic_frame_timestamps() {
  std::vector<audio::CapturedAudioFrame> frames;
  audio::AudioFrameAccumulator accumulator(
      [&frames](audio::CapturedAudioFrame frame) {
        frames.push_back(std::move(frame));
      });
  const auto samples = pcm(1'920, 0.125F);

  BEACON_TEST_REQUIRE(
      accumulator.push(samples, 1'920, 2'000'000, false, false));

  BEACON_TEST_REQUIRE(frames.size() == 2);
  BEACON_TEST_REQUIRE(frames[0].presentation_time_us == 2'000'000);
  BEACON_TEST_REQUIRE(frames[1].presentation_time_us == 2'020'000);
}

void silence_and_discontinuity_never_leak_stale_pcm() {
  std::vector<audio::CapturedAudioFrame> frames;
  audio::AudioFrameAccumulator accumulator(
      [&frames](audio::CapturedAudioFrame frame) {
        frames.push_back(std::move(frame));
      });
  const auto partial = pcm(480, 0.75F);
  BEACON_TEST_REQUIRE(accumulator.push(partial, 480, 3'000'000, false, false));
  BEACON_TEST_REQUIRE(accumulator.push({}, 960, 4'000'000, true, true));

  BEACON_TEST_REQUIRE(frames.size() == 1);
  BEACON_TEST_REQUIRE(frames[0].presentation_time_us == 4'000'000);
  BEACON_TEST_REQUIRE(std::ranges::all_of(
      frames[0].interleaved_pcm, [](float value) { return value == 0.0F; }));
}

void malformed_capture_packets_are_rejected() {
  audio::AudioFrameAccumulator accumulator([](audio::CapturedAudioFrame) {});
  const std::vector<float> wrong_size(7, 0.0F);

  BEACON_TEST_REQUIRE(!accumulator.push(wrong_size, 4, 1, false, false));
  BEACON_TEST_REQUIRE(!accumulator.push({}, 4, 1, false, false));
}

void opt_in_production_capture_starts_and_stops_without_a_deadline() {
  if (GetEnvironmentVariableW(L"BEACON_WASAPI_PROBE", nullptr, 0) == 0) {
    return;
  }

  audio::WasapiLoopbackCapture capture;
  BEACON_TEST_REQUIRE(capture.start([](audio::CapturedAudioFrame) {}));
  BEACON_TEST_REQUIRE(capture.active());
  capture.stop();
  BEACON_TEST_REQUIRE(!capture.active());
  BEACON_TEST_REQUIRE(capture.failure().stage ==
                      audio::WasapiLoopbackStage::none);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    arbitrary_capture_packets_form_exact_twenty_millisecond_frames();
    one_large_capture_packet_emits_monotonic_frame_timestamps();
    silence_and_discontinuity_never_leak_stale_pcm();
    malformed_capture_packets_are_rejected();
    opt_in_production_capture_starts_and_stops_without_a_deadline();
  });
}
