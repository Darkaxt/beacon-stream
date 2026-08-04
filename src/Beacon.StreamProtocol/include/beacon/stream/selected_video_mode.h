#pragma once

#include "stream_control.pb.h"

namespace beacon::stream {

enum class SelectedVideoModeKind {
  invalid,
  h264_sdr,
  hevc_main10_hdr10,
};

[[nodiscard]] SelectedVideoModeKind classify_selected_video_mode(
    const v1::SelectedVideoMode& mode) noexcept;

[[nodiscard]] bool valid_selected_video_mode(
    const v1::SelectedVideoMode& mode) noexcept;

}  // namespace beacon::stream
