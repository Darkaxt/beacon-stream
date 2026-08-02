#include "grant_mapping.h"

#include <algorithm>
#include <cctype>
#include <string>

namespace beacon::android::streamcore {

namespace {

std::string protocol_enum_name(std::string_view prefix,
                               std::string_view grant_value) {
  std::string result(prefix);
  result.reserve(prefix.size() + grant_value.size());
  std::transform(grant_value.begin(), grant_value.end(),
                 std::back_inserter(result), [](unsigned char value) {
                   return static_cast<char>(std::toupper(value));
                 });
  return result;
}

}  // namespace

std::string grant_video_codec_enum_name(std::string_view codec) {
  return protocol_enum_name("VIDEO_CODEC_", codec);
}

std::string grant_dynamic_range_enum_name(std::string_view dynamic_range) {
  return protocol_enum_name("DYNAMIC_RANGE_", dynamic_range);
}

std::string grant_audio_codec_enum_name(std::string_view codec) {
  return protocol_enum_name("AUDIO_CODEC_", codec);
}

bool map_grant_video_codec(std::string_view codec,
                           stream::v1::VideoCodec &mapped) {
  return stream::v1::VideoCodec_Parse(grant_video_codec_enum_name(codec),
                                      &mapped);
}

bool map_grant_dynamic_range(std::string_view dynamic_range,
                             stream::v1::DynamicRange &mapped) {
  return stream::v1::DynamicRange_Parse(
      grant_dynamic_range_enum_name(dynamic_range), &mapped);
}

bool map_grant_audio_codec(std::string_view codec,
                           stream::v1::AudioCodec &mapped) {
  return stream::v1::AudioCodec_Parse(grant_audio_codec_enum_name(codec),
                                      &mapped);
}

bool map_selected_video_grant(
    std::string_view codec, std::uint32_t width, std::uint32_t height,
    std::uint32_t fps_numerator, std::uint32_t fps_denominator,
    std::string_view dynamic_range, SelectedVideo &mapped) {
  stream::v1::VideoCodec mapped_codec{};
  stream::v1::DynamicRange mapped_dynamic_range{};
  if (!map_grant_video_codec(codec, mapped_codec) ||
      !map_grant_dynamic_range(dynamic_range, mapped_dynamic_range) ||
      width == 0 || height == 0 || fps_numerator == 0 || fps_denominator == 0) {
    return false;
  }
  mapped = {.codec = mapped_codec,
            .width = width,
            .height = height,
            .fps_numerator = fps_numerator,
            .fps_denominator = fps_denominator,
            .dynamic_range = mapped_dynamic_range};
  return true;
}

bool map_selected_audio_grant(
    std::string_view codec, std::uint32_t sample_rate_hz,
    std::uint32_t channel_count, std::uint32_t frame_duration_us,
    std::uint32_t bitrate_bps, SelectedAudio &mapped) {
  stream::v1::AudioCodec mapped_codec{};
  if (!map_grant_audio_codec(codec, mapped_codec) || sample_rate_hz == 0 ||
      channel_count == 0 || frame_duration_us == 0 || bitrate_bps == 0) {
    return false;
  }
  mapped = {.codec = mapped_codec,
            .sample_rate_hz = sample_rate_hz,
            .channel_count = channel_count,
            .frame_duration_us = frame_duration_us,
            .bitrate_bps = bitrate_bps};
  return true;
}

}  // namespace beacon::android::streamcore
