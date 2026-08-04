#include "beacon/worker/video/nvenc_h264_encoder.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <Windows.h>
#include <ffnvcodec/nvEncodeAPI.h>

#include <cstdint>
#include <vector>

namespace {

namespace video = beacon::worker::video;

void main10_native_contract_is_exact() {
  const auto contract = video::nvenc_hevc_main10_native_contract();
  BEACON_TEST_REQUIRE(contract.buffer_format ==
                      NV_ENC_BUFFER_FORMAT_YUV420_10BIT);
  BEACON_TEST_REQUIRE(contract.input_bit_depth == NV_ENC_BIT_DEPTH_10);
  BEACON_TEST_REQUIRE(contract.output_bit_depth == NV_ENC_BIT_DEPTH_10);
  BEACON_TEST_REQUIRE(contract.repeat_vps_sps_pps);
  BEACON_TEST_REQUIRE(contract.colour_primaries == 9);
  BEACON_TEST_REQUIRE(contract.transfer_characteristics == 16);
  BEACON_TEST_REQUIRE(contract.colour_matrix == 9);
}

void capability_validation_requires_hevc_p010_ten_bit_and_dynamic_bitrate() {
  const video::NvencH264Plan plan{
      .width = 3840,
      .height = 2160,
      .frame_rate_numerator = 60,
      .frame_rate_denominator = 1,
      .bitrate_bps = 50'000'000,
      .codec = video::NvencVideoCodec::hevc_main10,
  };
  video::NvencH264ApiCapabilities caps{
      .hevc = true,
      .p010 = true,
      .ten_bit = true,
      .dynamic_bitrate = true,
      .max_width = 8192,
      .max_height = 8192,
  };
  BEACON_TEST_REQUIRE(video::validate_nvenc_hevc_main10_capabilities(caps, plan) ==
                      video::NvencH264Failure::none);
  caps.ten_bit = false;
  BEACON_TEST_REQUIRE(video::validate_nvenc_hevc_main10_capabilities(caps, plan) ==
                      video::NvencH264Failure::ten_bit_unsupported);
}

void annex_b_parser_accepts_vps_sps_pps_and_both_idr_types() {
  const std::vector<std::uint8_t> idr19{
      0, 0, 0, 1, static_cast<std::uint8_t>(32U << 1U), 1,
      0, 0, 1, static_cast<std::uint8_t>(33U << 1U), 1,
      0, 0, 1, static_cast<std::uint8_t>(34U << 1U), 1,
      0, 0, 1, static_cast<std::uint8_t>(39U << 1U), 1, 137, 1, 0, 0x80,
      0, 0, 1, static_cast<std::uint8_t>(39U << 1U), 1, 144, 1, 0, 0x80,
      0, 0, 1, static_cast<std::uint8_t>(19U << 1U), 1};
  const auto first = video::inspect_hevc_annex_b(idr19);
  BEACON_TEST_REQUIRE(first.has_start_code && first.has_vps && first.has_sps &&
                      first.has_pps && first.idr &&
                      first.has_mastering_display_sei &&
                      first.has_content_light_level_sei);

  const std::vector<std::uint8_t> idr20{
      0, 0, 1, static_cast<std::uint8_t>(20U << 1U), 1};
  BEACON_TEST_REQUIRE(video::inspect_hevc_annex_b(idr20).idr);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    main10_native_contract_is_exact();
    capability_validation_requires_hevc_p010_ten_bit_and_dynamic_bitrate();
    annex_b_parser_accepts_vps_sps_pps_and_both_idr_types();
  });
}
