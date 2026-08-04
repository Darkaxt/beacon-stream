#include "beacon/worker/video/nvenc_h264_encoder.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <ffnvcodec/nvEncodeAPI.h>

#include <algorithm>
#include <cstdint>
#include <limits>
#include <memory>
#include <optional>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace {

using beacon::worker::capture::D3d11Texture;
using beacon::worker::video::classify_nvenc_runtime_preflight;
using beacon::worker::video::ConvertedD3d11Frame;
using beacon::worker::video::EncodedH264AccessUnit;
using beacon::worker::video::INvencH264Api;
using beacon::worker::video::NvencH264ApiCapabilities;
using beacon::worker::video::NvencH264ApiHandleResult;
using beacon::worker::video::NvencH264ApiLockResult;
using beacon::worker::video::NvencH264Configuration;
using beacon::worker::video::NvencH264Encoder;
using beacon::worker::video::NvencH264Failure;
using beacon::worker::video::NvencH264LockedBitstream;
using beacon::worker::video::NvencH264Plan;
using beacon::worker::video::NvencH264Submit;
using beacon::worker::video::NvencVideoCodec;
using beacon::worker::video::nvenc_h264_native_contract;
using beacon::worker::video::validate_nvenc_h264_capabilities;
using beacon::worker::video::VideoColorMatrix;
using beacon::worker::video::VideoPixelFormat;
using beacon::worker::video::VideoRange;
using beacon::worker::video::VideoTransferFunction;

class FakeTexture final : public D3d11Texture {
 public:
  explicit FakeTexture(void* value) : value_(value) {}

  [[nodiscard]] void* native_texture() const noexcept override {
    return value_;
  }

 private:
  void* value_{};
};

struct FakeTrace {
  std::vector<std::string> calls;
  NvencH264Configuration configuration{};
  std::vector<NvencH264Submit> submits;
  std::vector<std::uint32_t> bitrates;
  void* poisoned_texture{};
};

class FakeApi final : public INvencH264Api {
 public:
  explicit FakeApi(std::shared_ptr<FakeTrace> trace)
      : trace_(std::move(trace)) {}

  void* identity{reinterpret_cast<void*>(0x1000)};
  NvencH264Failure open_result{NvencH264Failure::none};
  NvencH264Failure create_result{NvencH264Failure::none};
  NvencH264Failure register_result{NvencH264Failure::none};
  NvencH264Failure map_result{NvencH264Failure::none};
  NvencH264Failure submit_result{NvencH264Failure::none};
  NvencH264Failure lock_result{NvencH264Failure::none};
  NvencH264Failure unlock_result{NvencH264Failure::none};
  NvencH264Failure unmap_result{NvencH264Failure::none};
  NvencH264Failure unregister_result{NvencH264Failure::none};
  NvencH264Failure reconfigure_result{NvencH264Failure::none};
  NvencH264Failure destroy_bitstream_result{NvencH264Failure::none};
  NvencH264Failure destroy_session_result{NvencH264Failure::none};
  bool omit_parameter_sets{};
  bool mapped_handle_on_failure{};
  std::optional<std::size_t> locked_size_override;

  [[nodiscard]] void* device_identity(
      const D3d11Texture&) noexcept override {
    trace_->calls.emplace_back("identity");
    return identity;
  }

  [[nodiscard]] NvencH264Failure open(
      const D3d11Texture&,
      const NvencH264Configuration& configuration) noexcept override {
    trace_->calls.emplace_back("open");
    trace_->configuration = configuration;
    return open_result;
  }

  [[nodiscard]] NvencH264ApiHandleResult create_bitstream() noexcept override {
    trace_->calls.emplace_back("create_bitstream");
    return {.handle = create_result == NvencH264Failure::none ? 10U : 0U,
            .failure = create_result};
  }

  [[nodiscard]] NvencH264ApiHandleResult register_input(
      const D3d11Texture&, std::uint32_t,
      std::uint32_t) noexcept override {
    trace_->calls.emplace_back("register");
    return {.handle = register_result == NvencH264Failure::none
                          ? 20U + register_count_++
                          : 0U,
            .failure = register_result};
  }

  [[nodiscard]] NvencH264ApiHandleResult map_input(
      std::uintptr_t registered) noexcept override {
    trace_->calls.emplace_back("map");
    return {.handle = map_result == NvencH264Failure::none ||
                              mapped_handle_on_failure
                          ? registered + 100U
                          : 0U,
            .failure = map_result};
  }

  [[nodiscard]] NvencH264Failure submit(
      const NvencH264Submit& value) noexcept override {
    trace_->calls.emplace_back("submit");
    trace_->submits.push_back(value);
    return submit_result;
  }

  [[nodiscard]] NvencH264ApiLockResult lock_bitstream(
      std::uintptr_t) noexcept override {
    trace_->calls.emplace_back("lock");
    if (lock_result != NvencH264Failure::none) {
      return {.failure = lock_result};
    }
    const auto& submit = trace_->submits.back();
    if (submit.force_idr) {
      if (trace_->configuration.codec == NvencVideoCodec::hevc_main10) {
        idr_bytes_ = {0, 0, 0, 1, static_cast<std::uint8_t>(32U << 1U), 1,
                      0, 0, 1, static_cast<std::uint8_t>(33U << 1U), 1,
                      0, 0, 1, static_cast<std::uint8_t>(34U << 1U), 1,
                      0, 0, 1, static_cast<std::uint8_t>(39U << 1U), 1,
                      137, 1, 0, 0x80,
                      0, 0, 1, static_cast<std::uint8_t>(39U << 1U), 1,
                      144, 1, 0, 0x80,
                      0, 0, 1, static_cast<std::uint8_t>(19U << 1U), 1};
        return {.bitstream = {.data = idr_bytes_.data(),
                              .size = idr_bytes_.size(),
                              .qpc_timestamp = submit.qpc_timestamp}};
      }
      idr_bytes_ = omit_parameter_sets
                       ? std::vector<std::uint8_t>{0, 0, 0, 1, 0x65, 0xaa}
                       : std::vector<std::uint8_t>{
                             0, 0, 0, 1, 0x67, 0x64, 0, 0, 0, 1,
                             0x68, 0xee, 0, 0, 1, 0x65, 0xaa};
      return {.bitstream = {.data = idr_bytes_.data(),
                            .size = locked_size_override.value_or(
                                idr_bytes_.size()),
                            .qpc_timestamp = submit.qpc_timestamp},
              .failure = NvencH264Failure::none};
    }
    p_bytes_ = {0, 0, 1, 0x61, 0xbb};
    return {.bitstream = {.data = p_bytes_.data(),
                          .size = locked_size_override.value_or(
                              p_bytes_.size()),
                          .qpc_timestamp = submit.qpc_timestamp},
            .failure = NvencH264Failure::none};
  }

  [[nodiscard]] NvencH264Failure unlock_bitstream(
      std::uintptr_t) noexcept override {
    trace_->calls.emplace_back("unlock");
    return unlock_result;
  }

  [[nodiscard]] NvencH264Failure unmap_input(
      std::uintptr_t) noexcept override {
    trace_->calls.emplace_back("unmap");
    return unmap_result;
  }

  [[nodiscard]] NvencH264Failure unregister_input(
      std::uintptr_t) noexcept override {
    trace_->calls.emplace_back("unregister");
    return unregister_result;
  }

  [[nodiscard]] NvencH264Failure reconfigure_bitrate(
      std::uint32_t bitrate) noexcept override {
    trace_->calls.emplace_back("reconfigure");
    trace_->bitrates.push_back(bitrate);
    return reconfigure_result;
  }

  [[nodiscard]] NvencH264Failure destroy_bitstream(
      std::uintptr_t) noexcept override {
    trace_->calls.emplace_back("destroy_bitstream");
    return destroy_bitstream_result;
  }

  [[nodiscard]] NvencH264Failure destroy_session() noexcept override {
    trace_->calls.emplace_back("destroy_session");
    return destroy_session_result;
  }

  void poison_session(const D3d11Texture* texture) noexcept override {
    trace_->calls.emplace_back("poison_session");
    trace_->poisoned_texture =
        texture == nullptr ? nullptr : texture->native_texture();
  }

  void unload() noexcept override { trace_->calls.emplace_back("unload"); }

 private:
  std::shared_ptr<FakeTrace> trace_;
  std::uintptr_t register_count_{};
  std::vector<std::uint8_t> idr_bytes_;
  std::vector<std::uint8_t> p_bytes_;
};

NvencH264Plan plan() {
  return {.width = 2560,
          .height = 1600,
          .frame_rate_numerator = 120,
          .frame_rate_denominator = 1,
          .bitrate_bps = 35'000'000};
}

std::vector<std::uint8_t> hdr_static_info() {
  std::vector<std::uint8_t> metadata(25);
  metadata[17] = 0xe8;
  metadata[18] = 0x03;
  return metadata;
}

ConvertedD3d11Frame frame(std::uintptr_t texture = 0x2000,
                          std::int64_t timestamp = 100);

void hevc_main10_encoder_requests_p010_hdr_configuration() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  auto hevc_plan = plan();
  hevc_plan.codec = NvencVideoCodec::hevc_main10;
  hevc_plan.hdr_static_info = hdr_static_info();
  NvencH264Encoder encoder{std::move(api), std::move(hevc_plan)};
  auto p010 = frame();
  p010.format = VideoPixelFormat::p010;
  p010.matrix = VideoColorMatrix::bt2020_non_constant_luminance;
  p010.transfer_function = VideoTransferFunction::pq;

  const auto encoded = encoder.encode(p010);

  BEACON_TEST_REQUIRE(encoded.has_value());
  BEACON_TEST_REQUIRE(encoded->idr && encoded->has_vps && encoded->has_sps &&
                      encoded->has_pps &&
                      encoded->has_mastering_display_sei &&
                      encoded->has_content_light_level_sei);
  BEACON_TEST_REQUIRE(trace->configuration.codec ==
                      NvencVideoCodec::hevc_main10);
  BEACON_TEST_REQUIRE(trace->configuration.input_format ==
                      VideoPixelFormat::p010);
  BEACON_TEST_REQUIRE(trace->configuration.hdr_static_info ==
                      hdr_static_info());
  BEACON_TEST_REQUIRE(encoder.reconfigure_bitrate(20'000'000));
  p010.qpc_timestamp = 101;
  const auto recovery = encoder.encode(p010, true);
  BEACON_TEST_REQUIRE(recovery.has_value());
  BEACON_TEST_REQUIRE(recovery->idr && recovery->has_vps && recovery->has_sps &&
                      recovery->has_pps &&
                      recovery->has_mastering_display_sei &&
                      recovery->has_content_light_level_sei);
  BEACON_TEST_REQUIRE(trace->bitrates ==
                      std::vector<std::uint32_t>{20'000'000});
}

ConvertedD3d11Frame frame(std::uintptr_t texture,
                          std::int64_t timestamp) {
  return {.texture = std::make_shared<FakeTexture>(
              reinterpret_cast<void*>(texture)),
          .width = 2560,
          .height = 1600,
          .qpc_timestamp = timestamp,
          .format = VideoPixelFormat::nv12,
          .range = VideoRange::limited,
          .matrix = VideoColorMatrix::bt709};
}

std::size_t call_index(const std::vector<std::string>& calls,
                       const std::string& value, std::size_t start = 0) {
  const auto found =
      std::find(calls.begin() + static_cast<std::ptrdiff_t>(start),
                calls.end(), value);
  BEACON_TEST_REQUIRE(found != calls.end());
  return static_cast<std::size_t>(std::distance(calls.begin(), found));
}

void require_poisoned_encoder_is_not_reused(
    NvencH264Encoder& encoder, const std::shared_ptr<FakeTrace>& trace,
    std::int64_t timestamp) {
  const auto calls_before_retry = trace->calls.size();
  BEACON_TEST_REQUIRE(!encoder.encode(frame(0x2000, timestamp)).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::session_poisoned);
  BEACON_TEST_REQUIRE(trace->calls.size() == calls_before_retry);
  BEACON_TEST_REQUIRE(!encoder.reconfigure_bitrate(20'000'000));
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::session_poisoned);
  BEACON_TEST_REQUIRE(trace->calls.size() == calls_before_retry);
}

void native_contract_is_fixed_for_sdr_low_latency_h264() {
  const auto contract = nvenc_h264_native_contract();
  BEACON_TEST_REQUIRE(contract.api_version == NVENCAPI_VERSION);
  BEACON_TEST_REQUIRE(contract.device_type == NV_ENC_DEVICE_TYPE_DIRECTX);
  BEACON_TEST_REQUIRE(contract.buffer_format == NV_ENC_BUFFER_FORMAT_NV12);
  BEACON_TEST_REQUIRE(contract.tuning_info ==
                      NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY);
  BEACON_TEST_REQUIRE(contract.rate_control_mode == NV_ENC_PARAMS_RC_CBR);
  BEACON_TEST_REQUIRE(contract.frame_interval_p == 1);
  BEACON_TEST_REQUIRE(contract.gop_length == NVENC_INFINITE_GOPLENGTH);
  BEACON_TEST_REQUIRE(contract.repeat_sps_pps);
  BEACON_TEST_REQUIRE(!contract.enable_encode_async);
  BEACON_TEST_REQUIRE(contract.enable_picture_type_decision);
  BEACON_TEST_REQUIRE(
      contract.first_frame_flags ==
      (NV_ENC_PIC_FLAG_FORCEIDR | NV_ENC_PIC_FLAG_OUTPUT_SPSPPS));
}

void runtime_and_capability_preflight_failures_are_typed() {
  constexpr std::uint32_t driver_api_version =
      (NVENCAPI_MAJOR_VERSION << 4U) | NVENCAPI_MINOR_VERSION;
  BEACON_TEST_REQUIRE(
      classify_nvenc_runtime_preflight(false, false, 0) ==
      NvencH264Failure::runtime_unavailable);
  BEACON_TEST_REQUIRE(
      classify_nvenc_runtime_preflight(true, false, driver_api_version) ==
      NvencH264Failure::api_unavailable);
  BEACON_TEST_REQUIRE(
      classify_nvenc_runtime_preflight(true, true, driver_api_version - 1) ==
      NvencH264Failure::api_incompatible);
  BEACON_TEST_REQUIRE(
      classify_nvenc_runtime_preflight(true, true, driver_api_version) ==
      NvencH264Failure::none);

  NvencH264ApiCapabilities capabilities{
      .h264 = true,
      .nv12 = true,
      .dynamic_bitrate = true,
      .max_width = 8192,
      .max_height = 8192,
  };
  BEACON_TEST_REQUIRE(validate_nvenc_h264_capabilities(capabilities, plan()) ==
                      NvencH264Failure::none);
  capabilities.h264 = false;
  BEACON_TEST_REQUIRE(validate_nvenc_h264_capabilities(capabilities, plan()) ==
                      NvencH264Failure::h264_unsupported);
  capabilities.h264 = true;
  capabilities.nv12 = false;
  BEACON_TEST_REQUIRE(validate_nvenc_h264_capabilities(capabilities, plan()) ==
                      NvencH264Failure::nv12_unsupported);
  capabilities.nv12 = true;
  capabilities.dynamic_bitrate = false;
  BEACON_TEST_REQUIRE(validate_nvenc_h264_capabilities(capabilities, plan()) ==
                      NvencH264Failure::bitrate_reconfiguration_unsupported);
  capabilities.dynamic_bitrate = true;
  capabilities.max_width = 1920;
  BEACON_TEST_REQUIRE(validate_nvenc_h264_capabilities(capabilities, plan()) ==
                      NvencH264Failure::dimensions_unsupported);
}

void invalid_input_fails_before_native_api_calls() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  NvencH264Encoder encoder{std::move(api), plan()};

  auto invalid = frame();
  invalid.format = VideoPixelFormat::bgra8;
  BEACON_TEST_REQUIRE(!encoder.encode(invalid).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() == NvencH264Failure::invalid_frame);
  BEACON_TEST_REQUIRE(trace->calls.empty());
}

void poisoned_native_open_is_not_retried() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->open_result = NvencH264Failure::session_poisoned;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::session_poisoned);
  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "open") == 1);
  const auto calls_before_retry = trace->calls.size();
  BEACON_TEST_REQUIRE(!encoder.encode(frame(0x2000, 101)).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::session_poisoned);
  BEACON_TEST_REQUIRE(trace->calls.size() == calls_before_retry);
}

void first_frame_opens_the_fixed_contract_and_emits_parameterized_idr() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  NvencH264Encoder encoder{std::move(api), plan()};

  const auto encoded = encoder.encode(frame());

  BEACON_TEST_REQUIRE(encoded.has_value());
  BEACON_TEST_REQUIRE(encoder.failure() == NvencH264Failure::none);
  BEACON_TEST_REQUIRE(trace->configuration.width == 2560);
  BEACON_TEST_REQUIRE(trace->configuration.height == 1600);
  BEACON_TEST_REQUIRE(trace->configuration.frame_rate_numerator == 120);
  BEACON_TEST_REQUIRE(trace->configuration.frame_rate_denominator == 1);
  BEACON_TEST_REQUIRE(trace->configuration.bitrate_bps == 35'000'000);
  BEACON_TEST_REQUIRE(trace->configuration.input_format ==
                      VideoPixelFormat::nv12);
  BEACON_TEST_REQUIRE(trace->configuration.input_range == VideoRange::limited);
  BEACON_TEST_REQUIRE(trace->configuration.matrix == VideoColorMatrix::bt709);
  BEACON_TEST_REQUIRE(trace->configuration.low_latency);
  BEACON_TEST_REQUIRE(trace->configuration.b_frame_count == 0);
  BEACON_TEST_REQUIRE(trace->configuration.repeat_parameter_sets);
  BEACON_TEST_REQUIRE(trace->submits.size() == 1);
  BEACON_TEST_REQUIRE(trace->submits[0].force_idr);
  BEACON_TEST_REQUIRE(trace->submits[0].output_parameter_sets);
  BEACON_TEST_REQUIRE(trace->submits[0].qpc_timestamp == 100);
  BEACON_TEST_REQUIRE(encoded->qpc_timestamp == 100);
  BEACON_TEST_REQUIRE(encoded->idr);
  BEACON_TEST_REQUIRE(encoded->has_sps);
  BEACON_TEST_REQUIRE(encoded->has_pps);
  BEACON_TEST_REQUIRE(!encoded->annex_b.empty());
}

void timestamp_and_forced_idr_rules_are_enforced() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(encoder.encode(frame()).has_value());
  const auto second = encoder.encode(frame(0x2000, 101));
  BEACON_TEST_REQUIRE(second.has_value());
  BEACON_TEST_REQUIRE(!second->idr);
  BEACON_TEST_REQUIRE(!trace->submits[1].force_idr);
  BEACON_TEST_REQUIRE(!trace->submits[1].output_parameter_sets);

  BEACON_TEST_REQUIRE(!encoder.encode(frame(0x2000, 101)).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::timestamp_not_monotonic);
  BEACON_TEST_REQUIRE(trace->submits.size() == 2);

  const auto forced = encoder.encode(frame(0x2000, 102), true);
  BEACON_TEST_REQUIRE(forced.has_value());
  BEACON_TEST_REQUIRE(forced->idr);
  BEACON_TEST_REQUIRE(forced->has_sps);
  BEACON_TEST_REQUIRE(forced->has_pps);
  BEACON_TEST_REQUIRE(trace->submits[2].force_idr);
  BEACON_TEST_REQUIRE(trace->submits[2].output_parameter_sets);
}

void encode_and_reconfigure_are_owned_by_one_media_thread() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  NvencH264Encoder encoder{std::move(api), plan()};
  BEACON_TEST_REQUIRE(encoder.encode(frame()).has_value());
  bool cross_thread_result = true;

  std::thread other([&encoder, &cross_thread_result] {
    cross_thread_result = encoder.reconfigure_bitrate(20'000'000);
  });
  other.join();

  BEACON_TEST_REQUIRE(!cross_thread_result);
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::thread_ownership_violation);
  BEACON_TEST_REQUIRE(trace->bitrates.empty());
  BEACON_TEST_REQUIRE(encoder.reconfigure_bitrate(20'000'000));
  BEACON_TEST_REQUIRE(trace->bitrates ==
                      std::vector<std::uint32_t>{20'000'000});
}

void texture_registrations_are_released_after_each_frame() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(encoder.encode(frame(0x2000, 100)).has_value());
  BEACON_TEST_REQUIRE(encoder.encode(frame(0x2000, 101)).has_value());
  BEACON_TEST_REQUIRE(encoder.encode(frame(0x3000, 102)).has_value());
  BEACON_TEST_REQUIRE(encoder.encode(frame(0x4000, 103)).has_value());
  BEACON_TEST_REQUIRE(encoder.encode(frame(0x5000, 104)).has_value());

  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "register") == 5);
  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "unregister") == 5);
  std::size_t cursor{};
  for (int frame_index = 0; frame_index < 5; ++frame_index) {
    const auto registration = call_index(trace->calls, "register", cursor);
    const auto unmap = call_index(trace->calls, "unmap", registration + 1);
    const auto unregistration =
        call_index(trace->calls, "unregister", unmap + 1);
    BEACON_TEST_REQUIRE(registration < unmap);
    BEACON_TEST_REQUIRE(unmap < unregistration);
    cursor = unregistration + 1;
  }
}

void bitrate_reconfiguration_is_session_bound_and_typed() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  auto* observed = api.get();
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.reconfigure_bitrate(20'000'000));
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::session_unavailable);
  BEACON_TEST_REQUIRE(encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.reconfigure_bitrate(20'000'000));
  BEACON_TEST_REQUIRE(trace->bitrates ==
                      std::vector<std::uint32_t>{20'000'000});

  observed->reconfigure_result = NvencH264Failure::reconfigure_failed;
  BEACON_TEST_REQUIRE(!encoder.reconfigure_bitrate(18'000'000));
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::reconfigure_failed);
}

void submit_failures_release_unaccepted_input() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  auto* observed = api.get();
  observed->submit_result = NvencH264Failure::encode_failed;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() == NvencH264Failure::encode_failed);
  const auto submit = call_index(trace->calls, "submit");
  const auto unmap = call_index(trace->calls, "unmap", submit + 1);
  BEACON_TEST_REQUIRE(submit < unmap);
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin(), trace->calls.end(),
                                 "lock") == trace->calls.end());
}

void failed_map_cleanup_poisons_the_registered_input() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->map_result = NvencH264Failure::input_mapping_failed;
  api->unregister_result = NvencH264Failure::input_unregistration_failed;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::input_unregistration_failed);
  const auto map = call_index(trace->calls, "map");
  const auto unregister = call_index(trace->calls, "unregister", map + 1);
  const auto poison =
      call_index(trace->calls, "poison_session", unregister + 1);
  BEACON_TEST_REQUIRE(map < unregister);
  BEACON_TEST_REQUIRE(unregister < poison);
  BEACON_TEST_REQUIRE(trace->poisoned_texture ==
                      reinterpret_cast<void*>(0x2000));
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin(), trace->calls.end(),
                                "submit") == trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin(), trace->calls.end(),
                                "destroy_session") == trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin(), trace->calls.end(),
                                "unload") == trace->calls.end());
  require_poisoned_encoder_is_not_reused(encoder, trace, 101);
}

void failed_map_with_live_handle_is_not_unregistered() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->map_result = NvencH264Failure::input_unmapping_failed;
  api->mapped_handle_on_failure = true;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::input_unmapping_failed);
  const auto map = call_index(trace->calls, "map");
  const auto poison = call_index(trace->calls, "poison_session", map + 1);
  BEACON_TEST_REQUIRE(map < poison);
  BEACON_TEST_REQUIRE(trace->poisoned_texture ==
                      reinterpret_cast<void*>(0x2000));
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + map,
                                trace->calls.end(), "unregister") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + map,
                                trace->calls.end(), "submit") ==
                      trace->calls.end());
  require_poisoned_encoder_is_not_reused(encoder, trace, 101);
}

void lock_failure_poisons_without_releasing_in_flight_input() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->lock_result = NvencH264Failure::bitstream_lock_failed;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::bitstream_lock_failed);
  const auto lock = call_index(trace->calls, "lock");
  const auto poison = call_index(trace->calls, "poison_session", lock + 1);
  BEACON_TEST_REQUIRE(lock < poison);
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + lock,
                                trace->calls.end(), "unmap") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + lock,
                                trace->calls.end(), "unregister") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + lock,
                                trace->calls.end(), "destroy_bitstream") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + lock,
                                trace->calls.end(), "destroy_session") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + lock,
                                trace->calls.end(), "unload") ==
                      trace->calls.end());
  require_poisoned_encoder_is_not_reused(encoder, trace, 101);
}

void unlock_failure_poisons_without_releasing_locked_input() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->unlock_result = NvencH264Failure::bitstream_unlock_failed;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::bitstream_unlock_failed);
  const auto lock = call_index(trace->calls, "lock");
  const auto unlock = call_index(trace->calls, "unlock", lock + 1);
  const auto poison = call_index(trace->calls, "poison_session", unlock + 1);
  BEACON_TEST_REQUIRE(lock < unlock);
  BEACON_TEST_REQUIRE(unlock < poison);
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unlock,
                                trace->calls.end(), "unmap") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unlock,
                                trace->calls.end(), "unregister") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unlock,
                                trace->calls.end(), "destroy_bitstream") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unlock,
                                trace->calls.end(), "destroy_session") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unlock,
                                trace->calls.end(), "unload") ==
                      trace->calls.end());
  require_poisoned_encoder_is_not_reused(encoder, trace, 101);
}

void unmap_failure_does_not_unregister_still_mapped_input() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->unmap_result = NvencH264Failure::input_unmapping_failed;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::input_unmapping_failed);
  const auto unlock = call_index(trace->calls, "unlock");
  const auto unmap = call_index(trace->calls, "unmap", unlock + 1);
  const auto poison = call_index(trace->calls, "poison_session", unmap + 1);
  BEACON_TEST_REQUIRE(unlock < unmap);
  BEACON_TEST_REQUIRE(unmap < poison);
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unmap,
                                trace->calls.end(), "unregister") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unmap,
                                trace->calls.end(), "destroy_bitstream") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unmap,
                                trace->calls.end(), "destroy_session") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + unmap,
                                trace->calls.end(), "unload") ==
                      trace->calls.end());
  require_poisoned_encoder_is_not_reused(encoder, trace, 101);
}

void unregister_failure_poisons_after_successful_unmap() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->unregister_result = NvencH264Failure::input_unregistration_failed;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::input_unregistration_failed);
  const auto unmap = call_index(trace->calls, "unmap");
  const auto unregister = call_index(trace->calls, "unregister", unmap + 1);
  const auto poison =
      call_index(trace->calls, "poison_session", unregister + 1);
  BEACON_TEST_REQUIRE(unmap < unregister);
  BEACON_TEST_REQUIRE(unregister < poison);
  require_poisoned_encoder_is_not_reused(encoder, trace, 101);
}

void rejected_copy_forces_the_next_delivered_frame_to_idr() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  auto* observed = api.get();
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(encoder.encode(frame()).has_value());
  observed->locked_size_override = std::numeric_limits<std::size_t>::max();
  BEACON_TEST_REQUIRE(!encoder.encode(frame(0x2000, 101)).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::invalid_bitstream);
  const auto first_lock = call_index(trace->calls, "lock");
  const auto lock = call_index(trace->calls, "lock", first_lock + 1);
  const auto unlock = call_index(trace->calls, "unlock", lock + 1);
  const auto unmap = call_index(trace->calls, "unmap", unlock + 1);
  const auto unregister = call_index(trace->calls, "unregister", unmap + 1);
  BEACON_TEST_REQUIRE(lock < unlock);
  BEACON_TEST_REQUIRE(unlock < unmap);
  BEACON_TEST_REQUIRE(unmap < unregister);
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin(), trace->calls.end(),
                                "poison_session") == trace->calls.end());

  observed->locked_size_override.reset();
  const auto recovered = encoder.encode(frame(0x2000, 102));
  BEACON_TEST_REQUIRE(recovered.has_value());
  BEACON_TEST_REQUIRE(recovered->idr);
  BEACON_TEST_REQUIRE(recovered->has_sps);
  BEACON_TEST_REQUIRE(recovered->has_pps);
  BEACON_TEST_REQUIRE(trace->submits.size() == 3);
  BEACON_TEST_REQUIRE(!trace->submits[1].force_idr);
  BEACON_TEST_REQUIRE(trace->submits[2].force_idr);
  BEACON_TEST_REQUIRE(trace->submits[2].output_parameter_sets);
}

void missing_first_frame_parameter_sets_is_rejected() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  api->omit_parameter_sets = true;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::invalid_bitstream);
}

void device_loss_discards_the_session_and_reopens_on_retry() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  auto* observed = api.get();
  observed->submit_result = NvencH264Failure::device_lost;
  NvencH264Encoder encoder{std::move(api), plan()};

  BEACON_TEST_REQUIRE(!encoder.encode(frame()).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() == NvencH264Failure::device_lost);
  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "destroy_session") == 1);
  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "unload") == 1);

  observed->submit_result = NvencH264Failure::none;
  const auto retried = encoder.encode(frame(0x2000, 101));
  BEACON_TEST_REQUIRE(retried.has_value());
  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "open") == 2);
}

void failed_output_destruction_poisons_normal_shutdown() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  auto* observed = api.get();
  NvencH264Encoder encoder{std::move(api), plan()};
  BEACON_TEST_REQUIRE(encoder.encode(frame()).has_value());

  observed->identity = reinterpret_cast<void*>(0x9000);
  observed->destroy_bitstream_result =
      NvencH264Failure::bitstream_destruction_failed;
  BEACON_TEST_REQUIRE(!encoder.encode(frame(0x3000, 101)).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::bitstream_destruction_failed);
  const auto destroy_bitstream = call_index(trace->calls, "destroy_bitstream");
  const auto poison =
      call_index(trace->calls, "poison_session", destroy_bitstream + 1);
  BEACON_TEST_REQUIRE(destroy_bitstream < poison);
  BEACON_TEST_REQUIRE(trace->poisoned_texture == nullptr);
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + destroy_bitstream,
                                trace->calls.end(), "destroy_session") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + destroy_bitstream,
                                trace->calls.end(), "unload") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "open") == 1);
  require_poisoned_encoder_is_not_reused(encoder, trace, 102);
}

void failed_encoder_destruction_poisons_normal_shutdown() {
  auto trace = std::make_shared<FakeTrace>();
  auto api = std::make_unique<FakeApi>(trace);
  auto* observed = api.get();
  NvencH264Encoder encoder{std::move(api), plan()};
  BEACON_TEST_REQUIRE(encoder.encode(frame()).has_value());

  observed->identity = reinterpret_cast<void*>(0x9000);
  observed->destroy_session_result =
      NvencH264Failure::session_destruction_failed;
  BEACON_TEST_REQUIRE(!encoder.encode(frame(0x3000, 101)).has_value());
  BEACON_TEST_REQUIRE(encoder.failure() ==
                      NvencH264Failure::session_destruction_failed);
  const auto destroy_bitstream = call_index(trace->calls, "destroy_bitstream");
  const auto destroy_session =
      call_index(trace->calls, "destroy_session", destroy_bitstream + 1);
  const auto poison =
      call_index(trace->calls, "poison_session", destroy_session + 1);
  BEACON_TEST_REQUIRE(destroy_bitstream < destroy_session);
  BEACON_TEST_REQUIRE(destroy_session < poison);
  BEACON_TEST_REQUIRE(trace->poisoned_texture == nullptr);
  BEACON_TEST_REQUIRE(std::find(trace->calls.begin() + destroy_session,
                                trace->calls.end(), "unload") ==
                      trace->calls.end());
  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "open") == 1);
  require_poisoned_encoder_is_not_reused(encoder, trace, 102);
}

void device_changes_and_destruction_follow_exact_cleanup_order() {
  auto trace = std::make_shared<FakeTrace>();
  {
    auto api = std::make_unique<FakeApi>(trace);
    auto* observed = api.get();
    NvencH264Encoder encoder{std::move(api), plan()};
    BEACON_TEST_REQUIRE(encoder.encode(frame()).has_value());
    observed->identity = reinterpret_cast<void*>(0x9000);
    BEACON_TEST_REQUIRE(encoder.encode(frame(0x3000, 101)).has_value());
  }

  BEACON_TEST_REQUIRE(std::count(trace->calls.begin(), trace->calls.end(),
                                 "open") == 2);
  const auto first_unregister = call_index(trace->calls, "unregister");
  const auto first_destroy_bitstream =
      call_index(trace->calls, "destroy_bitstream", first_unregister + 1);
  const auto first_destroy_session =
      call_index(trace->calls, "destroy_session", first_destroy_bitstream + 1);
  const auto first_unload =
      call_index(trace->calls, "unload", first_destroy_session + 1);
  const auto second_open = call_index(trace->calls, "open", first_unload + 1);
  BEACON_TEST_REQUIRE(first_unregister < first_destroy_bitstream);
  BEACON_TEST_REQUIRE(first_destroy_bitstream < first_destroy_session);
  BEACON_TEST_REQUIRE(first_destroy_session < first_unload);
  BEACON_TEST_REQUIRE(first_unload < second_open);

  const auto final_unregister = call_index(trace->calls, "unregister",
                                           second_open + 1);
  const auto final_destroy_bitstream =
      call_index(trace->calls, "destroy_bitstream", final_unregister + 1);
  const auto final_destroy_session =
      call_index(trace->calls, "destroy_session", final_destroy_bitstream + 1);
  const auto final_unload =
      call_index(trace->calls, "unload", final_destroy_session + 1);
  BEACON_TEST_REQUIRE(final_unregister < final_destroy_bitstream);
  BEACON_TEST_REQUIRE(final_destroy_bitstream < final_destroy_session);
  BEACON_TEST_REQUIRE(final_destroy_session < final_unload);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    native_contract_is_fixed_for_sdr_low_latency_h264();
    hevc_main10_encoder_requests_p010_hdr_configuration();
    runtime_and_capability_preflight_failures_are_typed();
    invalid_input_fails_before_native_api_calls();
    poisoned_native_open_is_not_retried();
    first_frame_opens_the_fixed_contract_and_emits_parameterized_idr();
    timestamp_and_forced_idr_rules_are_enforced();
    encode_and_reconfigure_are_owned_by_one_media_thread();
    texture_registrations_are_released_after_each_frame();
    bitrate_reconfiguration_is_session_bound_and_typed();
    submit_failures_release_unaccepted_input();
    failed_map_cleanup_poisons_the_registered_input();
    failed_map_with_live_handle_is_not_unregistered();
    lock_failure_poisons_without_releasing_in_flight_input();
    unlock_failure_poisons_without_releasing_locked_input();
    unmap_failure_does_not_unregister_still_mapped_input();
    unregister_failure_poisons_after_successful_unmap();
    rejected_copy_forces_the_next_delivered_frame_to_idr();
    missing_first_frame_parameter_sets_is_rejected();
    device_loss_discards_the_session_and_reopens_on_retry();
    failed_output_destruction_poisons_normal_shutdown();
    failed_encoder_destruction_poisons_normal_shutdown();
    device_changes_and_destruction_follow_exact_cleanup_order();
  });
}
