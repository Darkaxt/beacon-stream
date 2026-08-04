#pragma once

#include "stream_core.h"

#include <string>
#include <string_view>
#include <vector>

namespace beacon::android::streamcore {

[[nodiscard]] std::string grant_video_codec_enum_name(
    std::string_view codec);
[[nodiscard]] std::string grant_dynamic_range_enum_name(
    std::string_view dynamic_range);
[[nodiscard]] std::string grant_audio_codec_enum_name(
    std::string_view codec);
[[nodiscard]] bool map_grant_video_codec(
    std::string_view codec, stream::v1::VideoCodec &mapped);
[[nodiscard]] bool map_grant_dynamic_range(
    std::string_view dynamic_range, stream::v1::DynamicRange &mapped);
[[nodiscard]] bool map_grant_audio_codec(
    std::string_view codec, stream::v1::AudioCodec &mapped);
[[nodiscard]] bool map_selected_video_grant(
    std::string_view codec, std::uint32_t width, std::uint32_t height,
    std::uint32_t fps_numerator, std::uint32_t fps_denominator,
    std::string_view dynamic_range, std::string_view profile,
    std::uint32_t bit_depth, std::string_view color_primaries,
    std::string_view transfer_function, std::string_view matrix_coefficients,
    std::string_view color_range, const std::vector<std::byte> &hdr_static_info,
    bool hdr_static_info_in_bitstream, SelectedVideo &mapped);
[[nodiscard]] bool map_selected_audio_grant(
    std::string_view codec, std::uint32_t sample_rate_hz,
    std::uint32_t channel_count, std::uint32_t frame_duration_us,
    std::uint32_t bitrate_bps, SelectedAudio &mapped);

}  // namespace beacon::android::streamcore
