#include "beacon/stream/selected_video_mode.h"

#include "beacon/stream/hdr_static_metadata.h"

namespace beacon::stream {

SelectedVideoModeKind classify_selected_video_mode(
    const v1::SelectedVideoMode& mode) noexcept {
  const bool common = mode.width() != 0 && mode.height() != 0 &&
                      mode.frames_per_second_numerator() != 0 &&
                      mode.frames_per_second_denominator() != 0 &&
                      mode.color_range() == v1::COLOR_RANGE_LIMITED;
  if (!common) {
    return SelectedVideoModeKind::invalid;
  }

  if (mode.codec() == v1::VIDEO_CODEC_H264 &&
      mode.dynamic_range() == v1::DYNAMIC_RANGE_SDR &&
      mode.profile() == v1::VIDEO_PROFILE_H264_HIGH &&
      mode.bit_depth() == 8 &&
      mode.color_primaries() == v1::COLOR_PRIMARIES_BT709 &&
      mode.transfer_function() == v1::TRANSFER_FUNCTION_BT709 &&
      mode.matrix_coefficients() == v1::MATRIX_COEFFICIENTS_BT709 &&
      mode.hdr_static_info().empty() &&
      !mode.hdr_static_info_in_bitstream()) {
    return SelectedVideoModeKind::h264_sdr;
  }

  if (mode.codec() == v1::VIDEO_CODEC_HEVC &&
      mode.dynamic_range() == v1::DYNAMIC_RANGE_HDR10 &&
      mode.profile() == v1::VIDEO_PROFILE_HEVC_MAIN10 &&
      mode.bit_depth() == 10 &&
      mode.color_primaries() == v1::COLOR_PRIMARIES_BT2020 &&
      mode.transfer_function() == v1::TRANSFER_FUNCTION_PQ &&
      mode.matrix_coefficients() ==
          v1::MATRIX_COEFFICIENTS_BT2020_NON_CONSTANT_LUMINANCE &&
      parse_cta861_3_hdr_static_info(mode.hdr_static_info()).has_value() &&
      mode.hdr_static_info_in_bitstream()) {
    return SelectedVideoModeKind::hevc_main10_hdr10;
  }

  return SelectedVideoModeKind::invalid;
}

bool valid_selected_video_mode(const v1::SelectedVideoMode& mode) noexcept {
  return classify_selected_video_mode(mode) != SelectedVideoModeKind::invalid;
}

}  // namespace beacon::stream
