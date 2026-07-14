#include "beacon/worker/video/nvenc_h264_encoder.h"

#include <Windows.h>
#include <d3d11.h>
#include <ffnvcodec/nvEncodeAPI.h>
#include <winrt/base.h>

#include <algorithm>
#include <cstring>
#include <limits>
#include <new>
#include <utility>

namespace beacon::worker::video {
namespace {

constexpr std::uint32_t required_driver_api_version =
    (NVENCAPI_MAJOR_VERSION << 4U) | NVENCAPI_MINOR_VERSION;

using NvencGetMaxSupportedVersion =
    NVENCSTATUS(NVENCAPI*)(std::uint32_t* version);
using NvencCreateInstance =
    NVENCSTATUS(NVENCAPI*)(NV_ENCODE_API_FUNCTION_LIST* functions);

bool same_guid(const GUID& left, const GUID& right) noexcept {
  return InlineIsEqualGUID(left, right) != FALSE;
}

bool device_lost_status(NVENCSTATUS status) noexcept {
  return status == NV_ENC_ERR_NO_ENCODE_DEVICE ||
         status == NV_ENC_ERR_UNSUPPORTED_DEVICE ||
         status == NV_ENC_ERR_INVALID_ENCODERDEVICE ||
         status == NV_ENC_ERR_INVALID_DEVICE ||
         status == NV_ENC_ERR_DEVICE_NOT_EXIST;
}

NvencH264Failure operation_failure(
    NVENCSTATUS status, NvencH264Failure fallback) noexcept {
  if (status == NV_ENC_SUCCESS) {
    return NvencH264Failure::none;
  }
  return device_lost_status(status) ? NvencH264Failure::device_lost
                                    : fallback;
}

template <typename Function>
Function load_function(HMODULE library, const char* name) noexcept {
  const FARPROC address = GetProcAddress(library, name);
  Function function{};
  static_assert(sizeof(function) == sizeof(address));
  std::memcpy(&function, &address, sizeof(function));
  return function;
}

std::uint32_t one_frame_vbv(const NvencH264Configuration& configuration) {
  const std::uint64_t numerator =
      static_cast<std::uint64_t>(configuration.bitrate_bps) *
      configuration.frame_rate_denominator;
  const std::uint64_t value =
      std::max<std::uint64_t>(1, numerator / configuration.frame_rate_numerator);
  return static_cast<std::uint32_t>(std::min<std::uint64_t>(
      value, std::numeric_limits<std::uint32_t>::max()));
}

struct H264AccessUnitDescription {
  bool has_start_code{};
  bool idr{};
  bool sps{};
  bool pps{};
};

H264AccessUnitDescription inspect_annex_b(
    const std::vector<std::uint8_t>& bytes) noexcept {
  H264AccessUnitDescription result;
  std::size_t index{};
  while (index + 3U < bytes.size()) {
    std::size_t header{};
    if (bytes[index] == 0 && bytes[index + 1U] == 0 &&
        bytes[index + 2U] == 1) {
      header = index + 3U;
    } else if (index + 4U < bytes.size() && bytes[index] == 0 &&
               bytes[index + 1U] == 0 && bytes[index + 2U] == 0 &&
               bytes[index + 3U] == 1) {
      header = index + 4U;
    } else {
      ++index;
      continue;
    }
    if (header >= bytes.size()) {
      break;
    }
    result.has_start_code = true;
    const auto type = static_cast<std::uint8_t>(bytes[header] & 0x1FU);
    result.idr = result.idr || type == 5U;
    result.sps = result.sps || type == 7U;
    result.pps = result.pps || type == 8U;
    index = header + 1U;
  }
  return result;
}

class WindowsNvencH264Api final : public INvencH264Api {
 public:
  ~WindowsNvencH264Api() override {
    if (poisoned_) {
      return;
    }
    if (encoder_ != nullptr) {
      if (destroy_session() != NvencH264Failure::none) {
        poison_session(nullptr);
        return;
      }
    }
    unload();
  }

  [[nodiscard]] void* device_identity(
      const capture::D3d11Texture& input) noexcept override {
    try {
      auto* texture = static_cast<ID3D11Texture2D*>(input.native_texture());
      if (texture == nullptr) {
        return nullptr;
      }
      winrt::com_ptr<ID3D11Device> device;
      texture->GetDevice(device.put());
      return device.get();
    } catch (...) {
      return nullptr;
    }
  }

  [[nodiscard]] NvencH264Failure open(
      const capture::D3d11Texture& input,
      const NvencH264Configuration& configuration) noexcept override {
    if (poisoned_) {
      return NvencH264Failure::session_poisoned;
    }
    if (encoder_ != nullptr || library_ != nullptr) {
      return NvencH264Failure::session_unavailable;
    }
    try {
      auto* texture = static_cast<ID3D11Texture2D*>(input.native_texture());
      if (texture == nullptr) {
        return NvencH264Failure::device_unavailable;
      }
      D3D11_TEXTURE2D_DESC description{};
      texture->GetDesc(&description);
      if (description.Format != DXGI_FORMAT_NV12 ||
          description.Width != configuration.width ||
          description.Height != configuration.height) {
        return NvencH264Failure::invalid_frame;
      }
      texture->GetDevice(device_.put());
      if (!device_) {
        return NvencH264Failure::device_unavailable;
      }
      if (FAILED(device_->GetDeviceRemovedReason())) {
        device_ = nullptr;
        return NvencH264Failure::device_lost;
      }

      library_ = LoadLibraryExW(L"nvEncodeAPI64.dll", nullptr,
                                LOAD_LIBRARY_SEARCH_SYSTEM32);
      max_supported_version_ =
          load_function<NvencGetMaxSupportedVersion>(
              library_, "NvEncodeAPIGetMaxSupportedVersion");
      create_instance_ = load_function<NvencCreateInstance>(
          library_, "NvEncodeAPICreateInstance");
      std::uint32_t max_version{};
      const bool entry_points = max_supported_version_ != nullptr &&
                                create_instance_ != nullptr;
      if (library_ != nullptr && entry_points &&
          max_supported_version_(&max_version) != NV_ENC_SUCCESS) {
        max_version = 0;
      }
      const auto runtime_failure = classify_nvenc_runtime_preflight(
          library_ != nullptr, entry_points, max_version);
      if (runtime_failure != NvencH264Failure::none) {
        return cleanup_open_failure(runtime_failure);
      }

      functions_ = {};
      functions_.version = NV_ENCODE_API_FUNCTION_LIST_VER;
      if (create_instance_(&functions_) != NV_ENC_SUCCESS ||
          !required_functions_available()) {
        return cleanup_open_failure(NvencH264Failure::api_unavailable);
      }

      NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS open_params{};
      open_params.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
      open_params.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX;
      open_params.device = device_.get();
      open_params.apiVersion = NVENCAPI_VERSION;
      const auto open_status =
          functions_.nvEncOpenEncodeSessionEx(&open_params, &encoder_);
      if (open_status != NV_ENC_SUCCESS || encoder_ == nullptr) {
        const auto failure = operation_failure(
            open_status, NvencH264Failure::session_open_failed);
        return cleanup_open_failure(failure);
      }

      const auto capabilities = query_capabilities();
      const auto capability_failure =
          validate_nvenc_h264_capabilities(capabilities, {
              .width = configuration.width,
              .height = configuration.height,
              .frame_rate_numerator = configuration.frame_rate_numerator,
              .frame_rate_denominator = configuration.frame_rate_denominator,
              .bitrate_bps = configuration.bitrate_bps,
          });
      if (capability_failure != NvencH264Failure::none) {
        return cleanup_open_failure(capability_failure);
      }

      NV_ENC_PRESET_CONFIG preset{};
      preset.version = NV_ENC_PRESET_CONFIG_VER;
      preset.presetCfg.version = NV_ENC_CONFIG_VER;
      const auto preset_status = functions_.nvEncGetEncodePresetConfigEx(
          encoder_, NV_ENC_CODEC_H264_GUID, NV_ENC_PRESET_P1_GUID,
          NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY, &preset);
      if (preset_status != NV_ENC_SUCCESS) {
        const auto failure = operation_failure(
            preset_status, NvencH264Failure::preset_unavailable);
        return cleanup_open_failure(failure);
      }

      configuration_ = configuration;
      encode_configuration_ = preset.presetCfg;
      encode_configuration_.version = NV_ENC_CONFIG_VER;
      configure_encoder(configuration_);
      initialize_parameters_ = {};
      initialize_parameters_.version = NV_ENC_INITIALIZE_PARAMS_VER;
      initialize_parameters_.encodeGUID = NV_ENC_CODEC_H264_GUID;
      initialize_parameters_.presetGUID = NV_ENC_PRESET_P1_GUID;
      initialize_parameters_.encodeWidth = configuration.width;
      initialize_parameters_.encodeHeight = configuration.height;
      initialize_parameters_.darWidth = configuration.width;
      initialize_parameters_.darHeight = configuration.height;
      initialize_parameters_.frameRateNum =
          configuration.frame_rate_numerator;
      initialize_parameters_.frameRateDen =
          configuration.frame_rate_denominator;
      initialize_parameters_.enableEncodeAsync = 0;
      initialize_parameters_.enablePTD = 1;
      initialize_parameters_.encodeConfig = &encode_configuration_;
      initialize_parameters_.maxEncodeWidth = configuration.width;
      initialize_parameters_.maxEncodeHeight = configuration.height;
      initialize_parameters_.tuningInfo =
          NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY;

      const auto initialize_status =
          functions_.nvEncInitializeEncoder(encoder_, &initialize_parameters_);
      if (initialize_status != NV_ENC_SUCCESS) {
        const auto failure = operation_failure(
            initialize_status, NvencH264Failure::initialization_failed);
        return cleanup_open_failure(failure);
      }
      frame_index_ = 0;
      return NvencH264Failure::none;
    } catch (const std::bad_alloc&) {
      return cleanup_open_failure(NvencH264Failure::resource_exhausted);
    } catch (...) {
      const auto failure =
          device_ && FAILED(device_->GetDeviceRemovedReason())
              ? NvencH264Failure::device_lost
              : NvencH264Failure::session_open_failed;
      return cleanup_open_failure(failure);
    }
  }

  [[nodiscard]] NvencH264ApiHandleResult
  create_bitstream() noexcept override {
    if (encoder_ == nullptr || functions_.nvEncCreateBitstreamBuffer == nullptr) {
      return {.failure = NvencH264Failure::session_unavailable};
    }
    NV_ENC_CREATE_BITSTREAM_BUFFER create{};
    create.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
    const auto status =
        functions_.nvEncCreateBitstreamBuffer(encoder_, &create);
    const auto failure = operation_failure(
        status, NvencH264Failure::bitstream_creation_failed);
    return {.handle = failure == NvencH264Failure::none
                          ? reinterpret_cast<std::uintptr_t>(
                                create.bitstreamBuffer)
                          : 0,
            .failure = failure};
  }

  [[nodiscard]] NvencH264ApiHandleResult register_input(
      const capture::D3d11Texture& input, std::uint32_t width,
      std::uint32_t height) noexcept override {
    if (encoder_ == nullptr || functions_.nvEncRegisterResource == nullptr) {
      return {.failure = NvencH264Failure::session_unavailable};
    }
    auto* texture = static_cast<ID3D11Texture2D*>(input.native_texture());
    if (texture == nullptr) {
      return {.failure = NvencH264Failure::invalid_frame};
    }
    winrt::com_ptr<ID3D11Device> input_device;
    texture->GetDevice(input_device.put());
    if (input_device.get() != device_.get()) {
      return {.failure = NvencH264Failure::device_unavailable};
    }
    NV_ENC_REGISTER_RESOURCE registration{};
    registration.version = NV_ENC_REGISTER_RESOURCE_VER;
    registration.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
    registration.width = width;
    registration.height = height;
    registration.pitch = 0;
    registration.subResourceIndex = 0;
    registration.resourceToRegister = texture;
    registration.bufferFormat = NV_ENC_BUFFER_FORMAT_NV12;
    registration.bufferUsage = NV_ENC_INPUT_IMAGE;
    const auto status =
        functions_.nvEncRegisterResource(encoder_, &registration);
    const auto failure = operation_failure(
        status, NvencH264Failure::input_registration_failed);
    return {.handle = failure == NvencH264Failure::none
                          ? reinterpret_cast<std::uintptr_t>(
                                registration.registeredResource)
                          : 0,
            .failure = failure};
  }

  [[nodiscard]] NvencH264ApiHandleResult map_input(
      std::uintptr_t registered) noexcept override {
    if (encoder_ == nullptr || registered == 0 ||
        functions_.nvEncMapInputResource == nullptr) {
      return {.failure = NvencH264Failure::session_unavailable};
    }
    NV_ENC_MAP_INPUT_RESOURCE mapping{};
    mapping.version = NV_ENC_MAP_INPUT_RESOURCE_VER;
    mapping.registeredResource =
        reinterpret_cast<NV_ENC_REGISTERED_PTR>(registered);
    const auto status = functions_.nvEncMapInputResource(encoder_, &mapping);
    auto failure = operation_failure(
        status, NvencH264Failure::input_mapping_failed);
    if (failure == NvencH264Failure::none &&
        mapping.mappedBufferFmt != NV_ENC_BUFFER_FORMAT_NV12) {
      if (mapping.mappedResource != nullptr) {
        const auto unmap_failure = operation_failure(
            functions_.nvEncUnmapInputResource(encoder_,
                                                mapping.mappedResource),
            NvencH264Failure::input_unmapping_failed);
        if (unmap_failure != NvencH264Failure::none) {
          failure = unmap_failure;
        } else {
          mapping.mappedResource = nullptr;
        }
      }
      if (failure == NvencH264Failure::none) {
        failure = NvencH264Failure::nv12_unsupported;
      }
    }
    return {.handle = mapping.mappedResource == nullptr
                          ? 0
                          : reinterpret_cast<std::uintptr_t>(
                                mapping.mappedResource),
            .failure = failure};
  }

  [[nodiscard]] NvencH264Failure submit(
      const NvencH264Submit& submit) noexcept override {
    if (encoder_ == nullptr || submit.mapped_input == 0 ||
        submit.output_bitstream == 0 ||
        functions_.nvEncEncodePicture == nullptr) {
      return NvencH264Failure::session_unavailable;
    }
    NV_ENC_PIC_PARAMS picture{};
    picture.version = NV_ENC_PIC_PARAMS_VER;
    picture.inputWidth = configuration_.width;
    picture.inputHeight = configuration_.height;
    picture.inputPitch = configuration_.width;
    picture.encodePicFlags =
        (submit.force_idr ? NV_ENC_PIC_FLAG_FORCEIDR : 0U) |
        (submit.output_parameter_sets ? NV_ENC_PIC_FLAG_OUTPUT_SPSPPS : 0U);
    picture.frameIdx = frame_index_;
    picture.inputTimeStamp =
        static_cast<std::uint64_t>(submit.qpc_timestamp);
    picture.inputBuffer =
        reinterpret_cast<NV_ENC_INPUT_PTR>(submit.mapped_input);
    picture.outputBitstream =
        reinterpret_cast<NV_ENC_OUTPUT_PTR>(submit.output_bitstream);
    picture.bufferFmt = NV_ENC_BUFFER_FORMAT_NV12;
    picture.pictureStruct = NV_ENC_PIC_STRUCT_FRAME;
    const auto status = functions_.nvEncEncodePicture(encoder_, &picture);
    const auto failure =
        operation_failure(status, NvencH264Failure::encode_failed);
    if (failure == NvencH264Failure::none) {
      ++frame_index_;
    }
    return failure;
  }

  [[nodiscard]] NvencH264ApiLockResult lock_bitstream(
      std::uintptr_t output_bitstream) noexcept override {
    if (encoder_ == nullptr || output_bitstream == 0 ||
        functions_.nvEncLockBitstream == nullptr) {
      return {.failure = NvencH264Failure::session_unavailable};
    }
    NV_ENC_LOCK_BITSTREAM lock{};
    lock.version = NV_ENC_LOCK_BITSTREAM_VER;
    lock.doNotWait = 0;
    lock.outputBitstream =
        reinterpret_cast<NV_ENC_OUTPUT_PTR>(output_bitstream);
    const auto status = functions_.nvEncLockBitstream(encoder_, &lock);
    auto failure = operation_failure(
        status, NvencH264Failure::bitstream_lock_failed);
    if (failure == NvencH264Failure::none &&
        (lock.bitstreamBufferPtr == nullptr || lock.bitstreamSizeInBytes == 0 ||
         lock.outputTimeStamp >
             static_cast<std::uint64_t>(
                 std::numeric_limits<std::int64_t>::max()))) {
      if (lock.bitstreamBufferPtr != nullptr) {
        (void)functions_.nvEncUnlockBitstream(
            encoder_, reinterpret_cast<NV_ENC_OUTPUT_PTR>(output_bitstream));
      }
      failure = NvencH264Failure::invalid_bitstream;
    }
    return {
        .bitstream =
            {.data = failure == NvencH264Failure::none
                         ? static_cast<const std::uint8_t*>(
                               lock.bitstreamBufferPtr)
                         : nullptr,
             .size = failure == NvencH264Failure::none
                         ? static_cast<std::size_t>(lock.bitstreamSizeInBytes)
                         : 0,
             .qpc_timestamp =
                 failure == NvencH264Failure::none
                     ? static_cast<std::int64_t>(lock.outputTimeStamp)
                     : 0},
        .failure = failure,
    };
  }

  [[nodiscard]] NvencH264Failure unlock_bitstream(
      std::uintptr_t output_bitstream) noexcept override {
    if (encoder_ == nullptr || output_bitstream == 0 ||
        functions_.nvEncUnlockBitstream == nullptr) {
      return NvencH264Failure::session_unavailable;
    }
    return operation_failure(
        functions_.nvEncUnlockBitstream(
            encoder_, reinterpret_cast<NV_ENC_OUTPUT_PTR>(output_bitstream)),
        NvencH264Failure::bitstream_unlock_failed);
  }

  [[nodiscard]] NvencH264Failure unmap_input(
      std::uintptr_t mapped) noexcept override {
    if (encoder_ == nullptr || mapped == 0 ||
        functions_.nvEncUnmapInputResource == nullptr) {
      return NvencH264Failure::session_unavailable;
    }
    return operation_failure(
        functions_.nvEncUnmapInputResource(
            encoder_, reinterpret_cast<NV_ENC_INPUT_PTR>(mapped)),
        NvencH264Failure::input_unmapping_failed);
  }

  [[nodiscard]] NvencH264Failure unregister_input(
      std::uintptr_t registered) noexcept override {
    if (encoder_ == nullptr || registered == 0 ||
        functions_.nvEncUnregisterResource == nullptr) {
      return NvencH264Failure::session_unavailable;
    }
    return operation_failure(
        functions_.nvEncUnregisterResource(
            encoder_, reinterpret_cast<NV_ENC_REGISTERED_PTR>(registered)),
        NvencH264Failure::input_unregistration_failed);
  }

  [[nodiscard]] NvencH264Failure reconfigure_bitrate(
      std::uint32_t bitrate_bps) noexcept override {
    if (encoder_ == nullptr || functions_.nvEncReconfigureEncoder == nullptr ||
        bitrate_bps == 0) {
      return NvencH264Failure::session_unavailable;
    }
    auto next_configuration = encode_configuration_;
    auto next_initialize = initialize_parameters_;
    next_initialize.encodeConfig = &next_configuration;
    next_configuration.rcParams.averageBitRate = bitrate_bps;
    next_configuration.rcParams.maxBitRate = bitrate_bps;
    NvencH264Configuration next_contract = configuration_;
    next_contract.bitrate_bps = bitrate_bps;
    const auto vbv = one_frame_vbv(next_contract);
    next_configuration.rcParams.vbvBufferSize = vbv;
    next_configuration.rcParams.vbvInitialDelay = vbv;
    NV_ENC_RECONFIGURE_PARAMS reconfigure{};
    reconfigure.version = NV_ENC_RECONFIGURE_PARAMS_VER;
    reconfigure.reInitEncodeParams = next_initialize;
    reconfigure.reInitEncodeParams.encodeConfig = &next_configuration;
    reconfigure.resetEncoder = 0;
    reconfigure.forceIDR = 0;
    const auto status =
        functions_.nvEncReconfigureEncoder(encoder_, &reconfigure);
    const auto failure = operation_failure(
        status, NvencH264Failure::reconfigure_failed);
    if (failure == NvencH264Failure::none) {
      configuration_ = next_contract;
      encode_configuration_ = next_configuration;
      initialize_parameters_ = next_initialize;
      initialize_parameters_.encodeConfig = &encode_configuration_;
    }
    return failure;
  }

  [[nodiscard]] NvencH264Failure destroy_bitstream(
      std::uintptr_t output_bitstream) noexcept override {
    if (encoder_ == nullptr || output_bitstream == 0 ||
        functions_.nvEncDestroyBitstreamBuffer == nullptr) {
      return NvencH264Failure::session_unavailable;
    }
    return operation_failure(
        functions_.nvEncDestroyBitstreamBuffer(
            encoder_, reinterpret_cast<NV_ENC_OUTPUT_PTR>(output_bitstream)),
        NvencH264Failure::bitstream_destruction_failed);
  }

  [[nodiscard]] NvencH264Failure destroy_session() noexcept override {
    if (encoder_ == nullptr) {
      return NvencH264Failure::none;
    }
    if (functions_.nvEncDestroyEncoder == nullptr) {
      return NvencH264Failure::api_unavailable;
    }
    const auto status = functions_.nvEncDestroyEncoder(encoder_);
    const auto failure = operation_failure(
        status, NvencH264Failure::session_destruction_failed);
    if (failure == NvencH264Failure::none) {
      encoder_ = nullptr;
      device_ = nullptr;
    }
    return failure;
  }

  void poison_session(
      const capture::D3d11Texture* input) noexcept override {
    // No API-safe cleanup sequence remains after an ownership call fails.
    // Retain native references for the Worker process teardown boundary.
    auto* texture = input == nullptr
                        ? nullptr
                        : static_cast<ID3D11Texture2D*>(
                              input->native_texture());
    if (texture != nullptr) {
      texture->AddRef();
    }
    (void)device_.detach();
    poisoned_ = true;
  }

  void unload() noexcept override {
    if (poisoned_) {
      return;
    }
    if (library_ != nullptr) {
      FreeLibrary(library_);
    }
    library_ = nullptr;
    max_supported_version_ = nullptr;
    create_instance_ = nullptr;
    functions_ = {};
    configuration_ = {};
    encode_configuration_ = {};
    initialize_parameters_ = {};
    frame_index_ = 0;
  }

 private:
  [[nodiscard]] bool required_functions_available() const noexcept {
    return functions_.nvEncOpenEncodeSessionEx != nullptr &&
           functions_.nvEncGetEncodeGUIDCount != nullptr &&
           functions_.nvEncGetEncodeGUIDs != nullptr &&
           functions_.nvEncGetInputFormatCount != nullptr &&
           functions_.nvEncGetInputFormats != nullptr &&
           functions_.nvEncGetEncodeCaps != nullptr &&
           functions_.nvEncGetEncodePresetConfigEx != nullptr &&
           functions_.nvEncInitializeEncoder != nullptr &&
           functions_.nvEncCreateBitstreamBuffer != nullptr &&
           functions_.nvEncDestroyBitstreamBuffer != nullptr &&
           functions_.nvEncRegisterResource != nullptr &&
           functions_.nvEncMapInputResource != nullptr &&
           functions_.nvEncEncodePicture != nullptr &&
           functions_.nvEncLockBitstream != nullptr &&
           functions_.nvEncUnlockBitstream != nullptr &&
           functions_.nvEncUnmapInputResource != nullptr &&
           functions_.nvEncUnregisterResource != nullptr &&
           functions_.nvEncReconfigureEncoder != nullptr &&
           functions_.nvEncDestroyEncoder != nullptr;
  }

  [[nodiscard]] NvencH264ApiCapabilities query_capabilities() {
    NvencH264ApiCapabilities capabilities;
    std::uint32_t codec_count{};
    if (functions_.nvEncGetEncodeGUIDCount(encoder_, &codec_count) !=
            NV_ENC_SUCCESS ||
        codec_count == 0) {
      return capabilities;
    }
    std::vector<GUID> codecs(codec_count);
    std::uint32_t written_codecs{};
    if (functions_.nvEncGetEncodeGUIDs(encoder_, codecs.data(), codec_count,
                                      &written_codecs) != NV_ENC_SUCCESS) {
      return capabilities;
    }
    capabilities.h264 = std::any_of(
        codecs.begin(), codecs.begin() + std::min(codec_count, written_codecs),
        [](const GUID& codec) {
          return same_guid(codec, NV_ENC_CODEC_H264_GUID);
        });
    if (!capabilities.h264) {
      return capabilities;
    }

    std::uint32_t format_count{};
    if (functions_.nvEncGetInputFormatCount(
            encoder_, NV_ENC_CODEC_H264_GUID, &format_count) == NV_ENC_SUCCESS &&
        format_count > 0) {
      std::vector<NV_ENC_BUFFER_FORMAT> formats(format_count);
      std::uint32_t written_formats{};
      if (functions_.nvEncGetInputFormats(
              encoder_, NV_ENC_CODEC_H264_GUID, formats.data(), format_count,
              &written_formats) == NV_ENC_SUCCESS) {
        capabilities.nv12 = std::find(
            formats.begin(),
            formats.begin() + std::min(format_count, written_formats),
            NV_ENC_BUFFER_FORMAT_NV12) !=
                            formats.begin() +
                                std::min(format_count, written_formats);
      }
    }
    capabilities.max_width = query_capability(NV_ENC_CAPS_WIDTH_MAX);
    capabilities.max_height = query_capability(NV_ENC_CAPS_HEIGHT_MAX);
    capabilities.dynamic_bitrate =
        query_capability(NV_ENC_CAPS_SUPPORT_DYN_BITRATE_CHANGE) != 0;
    return capabilities;
  }

  [[nodiscard]] std::uint32_t query_capability(NV_ENC_CAPS capability) noexcept {
    NV_ENC_CAPS_PARAM parameter{};
    parameter.version = NV_ENC_CAPS_PARAM_VER;
    parameter.capsToQuery = capability;
    int value{};
    if (functions_.nvEncGetEncodeCaps(encoder_, NV_ENC_CODEC_H264_GUID,
                                      &parameter, &value) != NV_ENC_SUCCESS ||
        value < 0) {
      return 0;
    }
    return static_cast<std::uint32_t>(value);
  }

  void configure_encoder(
      const NvencH264Configuration& configuration) noexcept {
    encode_configuration_.profileGUID = NV_ENC_H264_PROFILE_HIGH_GUID;
    encode_configuration_.gopLength = NVENC_INFINITE_GOPLENGTH;
    encode_configuration_.frameIntervalP = 1;
    encode_configuration_.frameFieldMode =
        NV_ENC_PARAMS_FRAME_FIELD_MODE_FRAME;
    encode_configuration_.mvPrecision = NV_ENC_MV_PRECISION_QUARTER_PEL;
    encode_configuration_.rcParams.rateControlMode = NV_ENC_PARAMS_RC_CBR;
    encode_configuration_.rcParams.averageBitRate = configuration.bitrate_bps;
    encode_configuration_.rcParams.maxBitRate = configuration.bitrate_bps;
    const auto vbv = one_frame_vbv(configuration);
    encode_configuration_.rcParams.vbvBufferSize = vbv;
    encode_configuration_.rcParams.vbvInitialDelay = vbv;
    encode_configuration_.rcParams.enableLookahead = 0;
    encode_configuration_.rcParams.zeroReorderDelay = 1;
    encode_configuration_.rcParams.multiPass = NV_ENC_MULTI_PASS_DISABLED;
    auto& h264 = encode_configuration_.encodeCodecConfig.h264Config;
    h264.repeatSPSPPS = 1;
    h264.idrPeriod = NVENC_INFINITE_GOPLENGTH;
    h264.chromaFormatIDC = 1;
    h264.h264VUIParameters.videoSignalTypePresentFlag = 1;
    h264.h264VUIParameters.videoFormat =
        NV_ENC_VUI_VIDEO_FORMAT_UNSPECIFIED;
    h264.h264VUIParameters.videoFullRangeFlag = 0;
    h264.h264VUIParameters.colourDescriptionPresentFlag = 1;
    h264.h264VUIParameters.colourPrimaries =
        NV_ENC_VUI_COLOR_PRIMARIES_BT709;
    h264.h264VUIParameters.transferCharacteristics =
        NV_ENC_VUI_TRANSFER_CHARACTERISTIC_BT709;
    h264.h264VUIParameters.colourMatrix = NV_ENC_VUI_MATRIX_COEFFS_BT709;
  }

  [[nodiscard]] NvencH264Failure cleanup_open_failure(
      NvencH264Failure failure) noexcept {
    if (encoder_ != nullptr) {
      if (functions_.nvEncDestroyEncoder == nullptr ||
          functions_.nvEncDestroyEncoder(encoder_) != NV_ENC_SUCCESS) {
        (void)device_.detach();
        poisoned_ = true;
        return NvencH264Failure::session_poisoned;
      }
    }
    encoder_ = nullptr;
    device_ = nullptr;
    unload();
    return failure;
  }

  HMODULE library_{};
  NvencGetMaxSupportedVersion max_supported_version_{};
  NvencCreateInstance create_instance_{};
  NV_ENCODE_API_FUNCTION_LIST functions_{};
  void* encoder_{};
  winrt::com_ptr<ID3D11Device> device_;
  NvencH264Configuration configuration_{};
  NV_ENC_CONFIG encode_configuration_{};
  NV_ENC_INITIALIZE_PARAMS initialize_parameters_{};
  std::uint32_t frame_index_{};
  bool poisoned_{};
};

}  // namespace

NvencH264Encoder::NvencH264Encoder(std::unique_ptr<INvencH264Api> api,
                                   NvencH264Plan plan)
    : api_(std::move(api)), plan_(plan) {}

NvencH264Encoder::~NvencH264Encoder() {
  std::lock_guard lock{operation_mutex_};
  shutdown_session();
}

std::optional<EncodedH264AccessUnit> NvencH264Encoder::encode(
    const ConvertedD3d11Frame& frame, bool force_idr) noexcept {
  std::lock_guard lock{operation_mutex_};
  failure_ = NvencH264Failure::none;
  if (!claim_media_thread()) {
    return std::nullopt;
  }
  if (!valid_plan()) {
    failure_ = NvencH264Failure::invalid_plan;
    return std::nullopt;
  }
  if (!valid_frame(frame)) {
    failure_ = NvencH264Failure::invalid_frame;
    return std::nullopt;
  }
  if (last_timestamp_ && frame.qpc_timestamp <= *last_timestamp_) {
    failure_ = NvencH264Failure::timestamp_not_monotonic;
    return std::nullopt;
  }
  if (!api_) {
    failure_ = NvencH264Failure::api_unavailable;
    return std::nullopt;
  }
  if (session_poisoned_) {
    failure_ = NvencH264Failure::session_poisoned;
    return std::nullopt;
  }
  void* const device = api_->device_identity(*frame.texture);
  if (device == nullptr) {
    failure_ = NvencH264Failure::device_unavailable;
    if (session_open_) {
      shutdown_session();
    }
    return std::nullopt;
  }
  if (session_open_ && device != device_) {
    shutdown_session();
    if (session_poisoned_) {
      return std::nullopt;
    }
  }
  if (!ensure_session(frame, device)) {
    return std::nullopt;
  }
  const auto registered = registered_input(frame);
  if (!registered) {
    return std::nullopt;
  }
  const auto mapped = api_->map_input(*registered);
  if (mapped.failure != NvencH264Failure::none || mapped.handle == 0) {
    if (mapped.handle != 0) {
      failure_ = mapped.failure == NvencH264Failure::none
                     ? NvencH264Failure::input_mapping_failed
                     : mapped.failure;
      poison_session(&frame);
      return std::nullopt;
    }
    const auto unregister_failure = api_->unregister_input(*registered);
    failure_ = unregister_failure != NvencH264Failure::none
                   ? unregister_failure
                   : (mapped.failure == NvencH264Failure::none
                          ? NvencH264Failure::input_mapping_failed
                          : mapped.failure);
    if (failure_ == NvencH264Failure::device_lost ||
        unregister_failure != NvencH264Failure::none) {
      if (unregister_failure != NvencH264Failure::none) {
        poison_session(&frame);
      } else {
        shutdown_session();
      }
    }
    return std::nullopt;
  }

  const bool request_idr = first_frame_ || force_idr;
  const NvencH264Submit submit{
      .mapped_input = mapped.handle,
      .output_bitstream = output_bitstream_,
      .qpc_timestamp = frame.qpc_timestamp,
      .force_idr = request_idr,
      .output_parameter_sets = request_idr,
  };
  const auto submit_failure = api_->submit(submit);
  if (submit_failure != NvencH264Failure::none) {
    const auto release_failure = release_input(mapped.handle, *registered);
    failure_ = release_failure != NvencH264Failure::none ? release_failure
                                                         : submit_failure;
    if (release_failure != NvencH264Failure::none) {
      poison_session(&frame);
    } else if (failure_ == NvencH264Failure::device_lost) {
      shutdown_session();
    }
    return std::nullopt;
  }

  const auto locked = api_->lock_bitstream(output_bitstream_);
  if (locked.failure != NvencH264Failure::none ||
      locked.bitstream.data == nullptr || locked.bitstream.size == 0) {
    failure_ = locked.failure == NvencH264Failure::none
                   ? NvencH264Failure::invalid_bitstream
                   : locked.failure;
    poison_session(&frame);
    return std::nullopt;
  }

  NvencH264Failure content_failure{NvencH264Failure::none};
  std::vector<std::uint8_t> bytes;
  if (locked.bitstream.size > bytes.max_size()) {
    content_failure = NvencH264Failure::invalid_bitstream;
  } else {
    try {
      bytes.resize(locked.bitstream.size);
      std::memcpy(bytes.data(), locked.bitstream.data, locked.bitstream.size);
    } catch (...) {
      content_failure = NvencH264Failure::resource_exhausted;
    }
  }
  const auto unlock_failure = api_->unlock_bitstream(output_bitstream_);
  if (unlock_failure != NvencH264Failure::none) {
    failure_ = unlock_failure;
    poison_session(&frame);
    return std::nullopt;
  }
  const auto release_failure = release_input(mapped.handle, *registered);
  if (release_failure != NvencH264Failure::none) {
    failure_ = release_failure;
    poison_session(&frame);
    return std::nullopt;
  }
  if (content_failure != NvencH264Failure::none) {
    failure_ = content_failure;
    first_frame_ = true;
    return std::nullopt;
  }
  if (locked.bitstream.qpc_timestamp != frame.qpc_timestamp ||
      (last_timestamp_ &&
       locked.bitstream.qpc_timestamp <= *last_timestamp_)) {
    failure_ = NvencH264Failure::timestamp_not_monotonic;
    first_frame_ = true;
    return std::nullopt;
  }

  const auto description = inspect_annex_b(bytes);
  if (!description.has_start_code ||
      (request_idr &&
       (!description.idr || !description.sps || !description.pps))) {
    failure_ = NvencH264Failure::invalid_bitstream;
    first_frame_ = true;
    return std::nullopt;
  }
  first_frame_ = false;
  last_timestamp_ = locked.bitstream.qpc_timestamp;
  return EncodedH264AccessUnit{
      .annex_b = std::move(bytes),
      .qpc_timestamp = locked.bitstream.qpc_timestamp,
      .idr = description.idr,
      .has_sps = description.sps,
      .has_pps = description.pps,
  };
}

bool NvencH264Encoder::reconfigure_bitrate(
    std::uint32_t bitrate_bps) noexcept {
  std::lock_guard lock{operation_mutex_};
  failure_ = NvencH264Failure::none;
  if (!claim_media_thread()) {
    return false;
  }
  if (session_poisoned_) {
    failure_ = NvencH264Failure::session_poisoned;
    return false;
  }
  if (!session_open_) {
    failure_ = NvencH264Failure::session_unavailable;
    return false;
  }
  if (bitrate_bps == 0) {
    failure_ = NvencH264Failure::invalid_plan;
    return false;
  }
  failure_ = api_->reconfigure_bitrate(bitrate_bps);
  if (failure_ != NvencH264Failure::none) {
    if (failure_ == NvencH264Failure::device_lost) {
      shutdown_session();
    }
    return false;
  }
  plan_.bitrate_bps = bitrate_bps;
  return true;
}

std::uint32_t NvencH264Encoder::configured_bitrate_bps() const noexcept {
  std::lock_guard lock{operation_mutex_};
  return plan_.bitrate_bps;
}

NvencH264Failure NvencH264Encoder::failure() const noexcept {
  std::lock_guard lock{operation_mutex_};
  return failure_;
}

bool NvencH264Encoder::valid_plan() const noexcept {
  return plan_.width > 0 && plan_.height > 0 &&
         (plan_.width % 2U) == 0 && (plan_.height % 2U) == 0 &&
         plan_.frame_rate_numerator > 0 &&
         plan_.frame_rate_denominator > 0 && plan_.bitrate_bps > 0;
}

bool NvencH264Encoder::claim_media_thread() noexcept {
  const auto current = std::this_thread::get_id();
  if (!media_thread_.has_value()) {
    media_thread_ = current;
    return true;
  }
  if (*media_thread_ == current) {
    return true;
  }
  failure_ = NvencH264Failure::thread_ownership_violation;
  return false;
}

bool NvencH264Encoder::valid_frame(
    const ConvertedD3d11Frame& frame) const noexcept {
  return frame.texture && frame.texture->native_texture() != nullptr &&
         frame.width == plan_.width && frame.height == plan_.height &&
         frame.qpc_timestamp > 0 && frame.format == VideoPixelFormat::nv12 &&
         frame.range == VideoRange::limited &&
         frame.matrix == VideoColorMatrix::bt709;
}

bool NvencH264Encoder::ensure_session(const ConvertedD3d11Frame& frame,
                                      void* device) noexcept {
  if (session_open_) {
    return true;
  }
  const NvencH264Configuration configuration{
      .width = plan_.width,
      .height = plan_.height,
      .frame_rate_numerator = plan_.frame_rate_numerator,
      .frame_rate_denominator = plan_.frame_rate_denominator,
      .bitrate_bps = plan_.bitrate_bps,
      .input_format = VideoPixelFormat::nv12,
      .input_range = VideoRange::limited,
      .matrix = VideoColorMatrix::bt709,
      .low_latency = true,
      .b_frame_count = 0,
      .repeat_parameter_sets = true,
  };
  failure_ = api_->open(*frame.texture, configuration);
  if (failure_ != NvencH264Failure::none) {
    session_poisoned_ = failure_ == NvencH264Failure::session_poisoned;
    return false;
  }
  session_open_ = true;
  device_ = device;
  const auto bitstream = api_->create_bitstream();
  if (bitstream.failure != NvencH264Failure::none || bitstream.handle == 0) {
    failure_ = bitstream.failure == NvencH264Failure::none
                   ? NvencH264Failure::bitstream_creation_failed
                   : bitstream.failure;
    shutdown_session();
    return false;
  }
  output_bitstream_ = bitstream.handle;
  first_frame_ = true;
  last_timestamp_.reset();
  return true;
}

std::optional<std::uintptr_t> NvencH264Encoder::registered_input(
    const ConvertedD3d11Frame& frame) noexcept {
  const auto registration =
      api_->register_input(*frame.texture, frame.width, frame.height);
  if (registration.failure != NvencH264Failure::none ||
      registration.handle == 0) {
    failure_ = registration.failure == NvencH264Failure::none
                   ? NvencH264Failure::input_registration_failed
                   : registration.failure;
    if (failure_ == NvencH264Failure::device_lost) {
      shutdown_session();
    }
    return std::nullopt;
  }
  return registration.handle;
}

NvencH264Failure NvencH264Encoder::release_input(
    std::uintptr_t mapped, std::uintptr_t registered) noexcept {
  const auto unmap_failure = api_->unmap_input(mapped);
  if (unmap_failure != NvencH264Failure::none) {
    return unmap_failure;
  }
  return api_->unregister_input(registered);
}

void NvencH264Encoder::poison_session(
    const ConvertedD3d11Frame* frame) noexcept {
  if (!api_) {
    return;
  }
  if (session_open_) {
    api_->poison_session(frame == nullptr ? nullptr : frame->texture.get());
  }
  session_open_ = false;
  session_poisoned_ = true;
}

void NvencH264Encoder::shutdown_session() noexcept {
  if (!api_ || session_poisoned_) {
    return;
  }
  if (session_open_) {
    if (output_bitstream_ != 0) {
      const auto bitstream_failure =
          api_->destroy_bitstream(output_bitstream_);
      if (bitstream_failure != NvencH264Failure::none) {
        failure_ = bitstream_failure;
        poison_session(nullptr);
        return;
      }
    }
    output_bitstream_ = 0;
    const auto session_failure = api_->destroy_session();
    if (session_failure != NvencH264Failure::none) {
      failure_ = session_failure;
      poison_session(nullptr);
      return;
    }
  }
  api_->unload();
  session_open_ = false;
  device_ = nullptr;
  first_frame_ = true;
  last_timestamp_.reset();
}

NvencH264NativeContract nvenc_h264_native_contract() noexcept {
  return {
      .api_version = NVENCAPI_VERSION,
      .device_type = NV_ENC_DEVICE_TYPE_DIRECTX,
      .buffer_format = NV_ENC_BUFFER_FORMAT_NV12,
      .tuning_info = NV_ENC_TUNING_INFO_ULTRA_LOW_LATENCY,
      .rate_control_mode = NV_ENC_PARAMS_RC_CBR,
      .frame_interval_p = 1,
      .gop_length = NVENC_INFINITE_GOPLENGTH,
      .repeat_sps_pps = true,
      .enable_encode_async = false,
      .enable_picture_type_decision = true,
      .first_frame_flags =
          NV_ENC_PIC_FLAG_FORCEIDR | NV_ENC_PIC_FLAG_OUTPUT_SPSPPS,
  };
}

NvencH264Failure classify_nvenc_runtime_preflight(
    bool runtime_loaded, bool entry_points_available,
    std::uint32_t max_supported_api_version) noexcept {
  if (!runtime_loaded) {
    return NvencH264Failure::runtime_unavailable;
  }
  if (!entry_points_available) {
    return NvencH264Failure::api_unavailable;
  }
  if (max_supported_api_version < required_driver_api_version) {
    return NvencH264Failure::api_incompatible;
  }
  return NvencH264Failure::none;
}

NvencH264Failure validate_nvenc_h264_capabilities(
    const NvencH264ApiCapabilities& capabilities,
    const NvencH264Plan& plan) noexcept {
  if (!capabilities.h264) {
    return NvencH264Failure::h264_unsupported;
  }
  if (!capabilities.nv12) {
    return NvencH264Failure::nv12_unsupported;
  }
  if (!capabilities.dynamic_bitrate) {
    return NvencH264Failure::bitrate_reconfiguration_unsupported;
  }
  if (plan.width == 0 || plan.height == 0 ||
      plan.width > capabilities.max_width ||
      plan.height > capabilities.max_height) {
    return NvencH264Failure::dimensions_unsupported;
  }
  return NvencH264Failure::none;
}

std::unique_ptr<INvencH264Api> create_windows_nvenc_h264_api() {
  return std::make_unique<WindowsNvencH264Api>();
}

}  // namespace beacon::worker::video
