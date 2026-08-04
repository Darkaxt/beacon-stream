#include "grant_mapping.h"

#include "beacon/stream/hdr_static_metadata.h"
#include "beacon/stream/selected_video_mode.h"

#include <algorithm>
#include <cctype>
#include <string>

namespace beacon::android::streamcore {

namespace {

std::string protocol_enum_name(std::string_view prefix,
                               std::string_view grant_value) {
  std::string result(prefix);
  result.reserve(prefix.size() + grant_value.size() + 4U);
  for (const unsigned char value : grant_value) {
    if (std::isupper(value) && !result.empty() && result.back() != '_') {
      result.push_back('_');
    }
    result.push_back(static_cast<char>(std::toupper(value)));
  }
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
    std::string_view dynamic_range, std::string_view profile,
    std::uint32_t bit_depth, std::string_view color_primaries,
    std::string_view transfer_function, std::string_view matrix_coefficients,
    std::string_view color_range, const std::vector<std::byte> &hdr_static_info,
    bool hdr_static_info_in_bitstream, SelectedVideo &mapped) {
  stream::v1::VideoCodec mapped_codec{};
  stream::v1::DynamicRange mapped_dynamic_range{};
  stream::v1::VideoProfile mapped_profile{};
  stream::v1::ColorPrimaries mapped_color_primaries{};
  stream::v1::TransferFunction mapped_transfer_function{};
  stream::v1::MatrixCoefficients mapped_matrix_coefficients{};
  stream::v1::ColorRange mapped_color_range{};
  if (!map_grant_video_codec(codec, mapped_codec) ||
      !map_grant_dynamic_range(dynamic_range, mapped_dynamic_range) ||
      !stream::v1::VideoProfile_Parse(
          protocol_enum_name("VIDEO_PROFILE_", profile), &mapped_profile) ||
      !stream::v1::ColorPrimaries_Parse(
          protocol_enum_name("COLOR_PRIMARIES_", color_primaries),
          &mapped_color_primaries) ||
      !stream::v1::TransferFunction_Parse(
          protocol_enum_name("TRANSFER_FUNCTION_", transfer_function),
          &mapped_transfer_function) ||
      !stream::v1::MatrixCoefficients_Parse(
          protocol_enum_name("MATRIX_COEFFICIENTS_", matrix_coefficients),
          &mapped_matrix_coefficients) ||
      !stream::v1::ColorRange_Parse(
          protocol_enum_name("COLOR_RANGE_", color_range),
          &mapped_color_range) ||
      width == 0 || height == 0 || fps_numerator == 0 || fps_denominator == 0) {
    return false;
  }

  stream::v1::SelectedVideoMode candidate;
  candidate.set_codec(mapped_codec);
  candidate.set_width(width);
  candidate.set_height(height);
  candidate.set_frames_per_second_numerator(fps_numerator);
  candidate.set_frames_per_second_denominator(fps_denominator);
  candidate.set_dynamic_range(mapped_dynamic_range);
  candidate.set_profile(mapped_profile);
  candidate.set_bit_depth(bit_depth);
  candidate.set_color_primaries(mapped_color_primaries);
  candidate.set_transfer_function(mapped_transfer_function);
  candidate.set_matrix_coefficients(mapped_matrix_coefficients);
  candidate.set_color_range(mapped_color_range);
  candidate.set_hdr_static_info(hdr_static_info.data(), hdr_static_info.size());
  candidate.set_hdr_static_info_in_bitstream(hdr_static_info_in_bitstream);
  if (mapped_dynamic_range == stream::v1::DYNAMIC_RANGE_HDR10) {
    const std::string_view encoded{
        reinterpret_cast<const char *>(hdr_static_info.data()),
        hdr_static_info.size()};
    if (!beacon::stream::parse_cta861_3_hdr_static_info(encoded).has_value()) {
      return false;
    }
  }
  if (!beacon::stream::valid_selected_video_mode(candidate)) return false;

  mapped = {.codec = mapped_codec,
            .width = width,
            .height = height,
            .fps_numerator = fps_numerator,
            .fps_denominator = fps_denominator,
            .dynamic_range = mapped_dynamic_range,
            .profile = mapped_profile,
            .bit_depth = bit_depth,
            .color_primaries = mapped_color_primaries,
            .transfer_function = mapped_transfer_function,
            .matrix_coefficients = mapped_matrix_coefficients,
            .color_range = mapped_color_range,
            .hdr_static_info = hdr_static_info,
            .hdr_static_info_in_bitstream = hdr_static_info_in_bitstream};
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
