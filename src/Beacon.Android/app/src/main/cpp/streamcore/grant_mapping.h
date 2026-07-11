#pragma once

#include "stream_core.h"

#include <string>
#include <string_view>

namespace beacon::android::streamcore {

[[nodiscard]] std::string grant_video_codec_enum_name(
    std::string_view codec);
[[nodiscard]] std::string grant_dynamic_range_enum_name(
    std::string_view dynamic_range);
[[nodiscard]] bool map_grant_video_codec(
    std::string_view codec, stream::v1::VideoCodec &mapped);
[[nodiscard]] bool map_grant_dynamic_range(
    std::string_view dynamic_range, stream::v1::DynamicRange &mapped);
[[nodiscard]] bool map_selected_video_grant(
    std::string_view codec, std::uint32_t width, std::uint32_t height,
    std::uint32_t fps_numerator, std::uint32_t fps_denominator,
    std::string_view dynamic_range, SelectedVideo &mapped);

}  // namespace beacon::android::streamcore
