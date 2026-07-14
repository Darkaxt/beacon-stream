#include "beacon/worker/video/d3d11_video_processor.h"

#include <Windows.h>
#include <d3d11.h>
#include <d3d11_1.h>
#include <winrt/base.h>

#include <algorithm>
#include <cstdint>
#include <memory>
#include <utility>

namespace beacon::worker::video {
namespace {

constexpr DXGI_FORMAT input_format = DXGI_FORMAT_B8G8R8A8_UNORM;
constexpr DXGI_FORMAT output_format = DXGI_FORMAT_NV12;
constexpr DXGI_COLOR_SPACE_TYPE input_color_space =
    DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709;
constexpr DXGI_COLOR_SPACE_TYPE output_color_space =
    DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709;

RECT to_rect(const VideoRectangle& value) noexcept {
  return {
      .left = static_cast<LONG>(value.left),
      .top = static_cast<LONG>(value.top),
      .right = static_cast<LONG>(value.right),
      .bottom = static_cast<LONG>(value.bottom),
  };
}

class WindowsD3d11Texture final : public capture::D3d11Texture {
 public:
  explicit WindowsD3d11Texture(winrt::com_ptr<ID3D11Texture2D> texture)
      : texture_(std::move(texture)) {}

  [[nodiscard]] void* native_texture() const noexcept override {
    return texture_.get();
  }

 private:
  winrt::com_ptr<ID3D11Texture2D> texture_;
};

class WindowsD3d11VideoProcessorPlatform final
    : public ID3d11VideoProcessorPlatform {
 public:
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

  [[nodiscard]] D3d11VideoProcessorFailure configure(
      const capture::D3d11Texture& input,
      const D3d11VideoProcessorConfiguration& configuration) noexcept override {
    reset();
    try {
      auto* texture = static_cast<ID3D11Texture2D*>(input.native_texture());
      if (texture == nullptr) {
        return D3d11VideoProcessorFailure::device_unavailable;
      }
      D3D11_TEXTURE2D_DESC texture_description{};
      texture->GetDesc(&texture_description);
      constexpr UINT video_input_bind_flags =
          D3D11_BIND_DECODER | D3D11_BIND_VIDEO_ENCODER |
          D3D11_BIND_RENDER_TARGET | D3D11_BIND_UNORDERED_ACCESS;
      if (texture_description.Format != input_format ||
          texture_description.Width < configuration.input_width ||
          texture_description.Height < configuration.input_height ||
          (texture_description.BindFlags != 0 &&
           (texture_description.BindFlags & video_input_bind_flags) == 0)) {
        return D3d11VideoProcessorFailure::unsupported_format;
      }

      texture->GetDevice(device_.put());
      if (!device_) {
        return D3d11VideoProcessorFailure::device_unavailable;
      }
      if (FAILED(device_->GetDeviceRemovedReason())) {
        reset();
        return D3d11VideoProcessorFailure::device_lost;
      }
      if (FAILED(device_->QueryInterface(IID_PPV_ARGS(video_device_.put())))) {
        reset();
        return D3d11VideoProcessorFailure::device_unavailable;
      }
      device_->GetImmediateContext(context_.put());
      if (!context_ ||
          FAILED(
              context_->QueryInterface(IID_PPV_ARGS(video_context_.put()))) ||
          FAILED(
              context_->QueryInterface(IID_PPV_ARGS(video_context1_.put())))) {
        reset();
        return D3d11VideoProcessorFailure::unsupported_color_conversion;
      }

      D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
      content.InputFrameFormat = D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
      content.InputFrameRate = {configuration.frame_rate_numerator,
                                configuration.frame_rate_denominator};
      content.InputWidth = configuration.input_width;
      content.InputHeight = configuration.input_height;
      content.OutputFrameRate = content.InputFrameRate;
      content.OutputWidth = configuration.output_width;
      content.OutputHeight = configuration.output_height;
      content.Usage = D3D11_VIDEO_USAGE_OPTIMAL_SPEED;
      if (FAILED(video_device_->CreateVideoProcessorEnumerator(
              &content, enumerator_.put()))) {
        reset();
        return D3d11VideoProcessorFailure::processor_creation_failed;
      }

      UINT input_support{};
      UINT output_support{};
      if (FAILED(enumerator_->CheckVideoProcessorFormat(input_format,
                                                        &input_support)) ||
          (input_support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_INPUT) == 0 ||
          FAILED(enumerator_->CheckVideoProcessorFormat(output_format,
                                                        &output_support)) ||
          (output_support & D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT) == 0) {
        reset();
        return D3d11VideoProcessorFailure::unsupported_format;
      }

      winrt::com_ptr<ID3D11VideoProcessorEnumerator1> enumerator1;
      if (FAILED(
              enumerator_->QueryInterface(IID_PPV_ARGS(enumerator1.put())))) {
        reset();
        return D3d11VideoProcessorFailure::unsupported_color_conversion;
      }
      BOOL conversion_supported{};
      if (FAILED(enumerator1->CheckVideoProcessorFormatConversion(
              input_format, input_color_space, output_format,
              output_color_space, &conversion_supported)) ||
          conversion_supported == FALSE) {
        reset();
        return D3d11VideoProcessorFailure::unsupported_color_conversion;
      }

      D3D11_VIDEO_PROCESSOR_CAPS capabilities{};
      if (FAILED(enumerator_->GetVideoProcessorCaps(&capabilities)) ||
          capabilities.MaxInputStreams < 1 ||
          capabilities.RateConversionCapsCount < 1 ||
          FAILED(video_device_->CreateVideoProcessor(enumerator_.get(), 0,
                                                     processor_.put()))) {
        reset();
        return D3d11VideoProcessorFailure::processor_creation_failed;
      }
      configuration_ = configuration;
      return D3d11VideoProcessorFailure::none;
    } catch (...) {
      const auto failure =
          device_removed()
              ? D3d11VideoProcessorFailure::device_lost
              : D3d11VideoProcessorFailure::processor_creation_failed;
      reset();
      return failure;
    }
  }

  [[nodiscard]] std::shared_ptr<capture::D3d11Texture>
  create_output_texture() noexcept override {
    try {
      if (!device_ || !processor_) {
        return {};
      }
      D3D11_TEXTURE2D_DESC description{};
      description.Width = configuration_.output_width;
      description.Height = configuration_.output_height;
      description.MipLevels = 1;
      description.ArraySize = 1;
      description.Format = output_format;
      description.SampleDesc.Count = 1;
      description.Usage = D3D11_USAGE_DEFAULT;
      description.BindFlags = D3D11_BIND_RENDER_TARGET;
      winrt::com_ptr<ID3D11Texture2D> texture;
      if (FAILED(
              device_->CreateTexture2D(&description, nullptr, texture.put()))) {
        return {};
      }
      return std::make_shared<WindowsD3d11Texture>(std::move(texture));
    } catch (...) {
      return {};
    }
  }

  [[nodiscard]] D3d11VideoProcessorFailure blit(
      const capture::D3d11Texture& input, capture::D3d11Texture& output,
      const D3d11VideoProcessorLayout& layout) noexcept override {
    try {
      if (!video_device_ || !video_context_ || !video_context1_ ||
          !enumerator_ || !processor_) {
        return D3d11VideoProcessorFailure::processor_creation_failed;
      }
      auto* input_texture =
          static_cast<ID3D11Texture2D*>(input.native_texture());
      auto* output_texture =
          static_cast<ID3D11Texture2D*>(output.native_texture());
      if (input_texture == nullptr || output_texture == nullptr) {
        return D3d11VideoProcessorFailure::blit_failed;
      }

      winrt::com_ptr<ID3D11Device> input_device;
      winrt::com_ptr<ID3D11Device> output_device;
      input_texture->GetDevice(input_device.put());
      output_texture->GetDevice(output_device.put());
      if (input_device.get() != device_.get() ||
          output_device.get() != device_.get()) {
        return D3d11VideoProcessorFailure::blit_failed;
      }

      D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC input_view_description{};
      input_view_description.ViewDimension = D3D11_VPIV_DIMENSION_TEXTURE2D;
      input_view_description.Texture2D.MipSlice = 0;
      input_view_description.Texture2D.ArraySlice = 0;
      winrt::com_ptr<ID3D11VideoProcessorInputView> input_view;
      if (FAILED(video_device_->CreateVideoProcessorInputView(
              input_texture, enumerator_.get(), &input_view_description,
              input_view.put()))) {
        return device_removed() ? D3d11VideoProcessorFailure::device_lost
                                : D3d11VideoProcessorFailure::blit_failed;
      }

      D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC output_view_description{};
      output_view_description.ViewDimension = D3D11_VPOV_DIMENSION_TEXTURE2D;
      output_view_description.Texture2D.MipSlice = 0;
      winrt::com_ptr<ID3D11VideoProcessorOutputView> output_view;
      if (FAILED(video_device_->CreateVideoProcessorOutputView(
              output_texture, enumerator_.get(), &output_view_description,
              output_view.put()))) {
        return device_removed() ? D3d11VideoProcessorFailure::device_lost
                                : D3d11VideoProcessorFailure::blit_failed;
      }

      const RECT source = to_rect(layout.source);
      const RECT destination = to_rect(layout.destination);
      const RECT target{0, 0, static_cast<LONG>(configuration_.output_width),
                        static_cast<LONG>(configuration_.output_height)};
      D3D11_VIDEO_COLOR background{};
      background.RGBA.A = 1.0F;
      video_context_->VideoProcessorSetOutputTargetRect(processor_.get(), TRUE,
                                                        &target);
      video_context_->VideoProcessorSetOutputBackgroundColor(
          processor_.get(), FALSE, &background);
      video_context_->VideoProcessorSetStreamFrameFormat(
          processor_.get(), 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
      video_context_->VideoProcessorSetStreamAutoProcessingMode(
          processor_.get(), 0, FALSE);
      video_context_->VideoProcessorSetStreamSourceRect(processor_.get(), 0,
                                                        TRUE, &source);
      video_context_->VideoProcessorSetStreamDestRect(processor_.get(), 0, TRUE,
                                                      &destination);
      video_context1_->VideoProcessorSetStreamColorSpace1(processor_.get(), 0,
                                                          input_color_space);
      video_context1_->VideoProcessorSetOutputColorSpace1(processor_.get(),
                                                          output_color_space);

      D3D11_VIDEO_PROCESSOR_STREAM stream{};
      stream.Enable = TRUE;
      stream.pInputSurface = input_view.get();
      const HRESULT result = video_context_->VideoProcessorBlt(
          processor_.get(), output_view.get(), 0, 1, &stream);
      if (FAILED(result)) {
        return device_removed() ? D3d11VideoProcessorFailure::device_lost
                                : D3d11VideoProcessorFailure::blit_failed;
      }
      return D3d11VideoProcessorFailure::none;
    } catch (...) {
      return device_removed() ? D3d11VideoProcessorFailure::device_lost
                              : D3d11VideoProcessorFailure::blit_failed;
    }
  }

  void reset() noexcept override {
    processor_ = nullptr;
    enumerator_ = nullptr;
    video_context1_ = nullptr;
    video_context_ = nullptr;
    context_ = nullptr;
    video_device_ = nullptr;
    device_ = nullptr;
    configuration_ = {};
  }

 private:
  [[nodiscard]] bool device_removed() const noexcept {
    return device_ && FAILED(device_->GetDeviceRemovedReason());
  }

  winrt::com_ptr<ID3D11Device> device_;
  winrt::com_ptr<ID3D11DeviceContext> context_;
  winrt::com_ptr<ID3D11VideoDevice> video_device_;
  winrt::com_ptr<ID3D11VideoContext> video_context_;
  winrt::com_ptr<ID3D11VideoContext1> video_context1_;
  winrt::com_ptr<ID3D11VideoProcessorEnumerator> enumerator_;
  winrt::com_ptr<ID3D11VideoProcessor> processor_;
  D3d11VideoProcessorConfiguration configuration_{};
};

}  // namespace

D3d11VideoProcessor::D3d11VideoProcessor(
    std::unique_ptr<ID3d11VideoProcessorPlatform> platform)
    : platform_(std::move(platform)) {}

D3d11VideoProcessor::~D3d11VideoProcessor() { discard_gpu_state(); }

std::optional<ConvertedD3d11Frame> D3d11VideoProcessor::convert(
    const capture::CapturedD3d11Frame& input,
    const D3d11VideoProcessorPlan& plan) noexcept {
  failure_ = D3d11VideoProcessorFailure::none;
  if (!input.texture || input.texture->native_texture() == nullptr ||
      input.width == 0 || input.height == 0) {
    failure_ = D3d11VideoProcessorFailure::invalid_frame;
    return std::nullopt;
  }
  if (plan.output_width == 0 || plan.output_height == 0 ||
      (plan.output_width % 2U) != 0 || (plan.output_height % 2U) != 0 ||
      plan.frame_rate_numerator == 0 || plan.frame_rate_denominator == 0) {
    failure_ = D3d11VideoProcessorFailure::invalid_plan;
    return std::nullopt;
  }
  const auto layout = calculate_video_processor_layout(
      input.width, input.height, plan.output_width, plan.output_height);
  if (!layout || !platform_) {
    failure_ = layout ? D3d11VideoProcessorFailure::device_unavailable
                      : D3d11VideoProcessorFailure::invalid_plan;
    return std::nullopt;
  }
  void* const device = platform_->device_identity(*input.texture);
  if (device == nullptr) {
    failure_ = D3d11VideoProcessorFailure::device_unavailable;
    return std::nullopt;
  }

  const D3d11VideoProcessorConfiguration configuration{
      .input_width = input.width,
      .input_height = input.height,
      .output_width = plan.output_width,
      .output_height = plan.output_height,
      .frame_rate_numerator = plan.frame_rate_numerator,
      .frame_rate_denominator = plan.frame_rate_denominator,
  };
  const CachedConfiguration requested{.device = device,
                                      .configuration = configuration};
  if (!cached_ || *cached_ != requested) {
    if (cached_) {
      discard_gpu_state();
    }
    failure_ = platform_->configure(*input.texture, configuration);
    if (failure_ != D3d11VideoProcessorFailure::none) {
      platform_->reset();
      return std::nullopt;
    }
    cached_ = requested;
  }

  std::shared_ptr<capture::D3d11Texture> output;
  const auto available = std::find_if(
      output_pool_.begin(), output_pool_.end(),
      [](const auto& candidate) { return candidate.use_count() == 1; });
  if (available != output_pool_.end()) {
    output = *available;
  } else if (output_pool_.size() < output_pool_capacity) {
    output = platform_->create_output_texture();
    if (!output || output->native_texture() == nullptr) {
      failure_ = D3d11VideoProcessorFailure::output_creation_failed;
      return std::nullopt;
    }
    output_pool_.push_back(output);
  } else {
    failure_ = D3d11VideoProcessorFailure::output_pool_exhausted;
    return std::nullopt;
  }

  failure_ = platform_->blit(*input.texture, *output, *layout);
  if (failure_ != D3d11VideoProcessorFailure::none) {
    discard_gpu_state();
    return std::nullopt;
  }
  return ConvertedD3d11Frame{
      .texture = std::move(output),
      .width = plan.output_width,
      .height = plan.output_height,
      .qpc_timestamp = input.qpc_timestamp,
  };
}

D3d11VideoProcessorFailure D3d11VideoProcessor::failure() const noexcept {
  return failure_;
}

void D3d11VideoProcessor::discard_gpu_state() noexcept {
  output_pool_.clear();
  cached_.reset();
  if (platform_) {
    platform_->reset();
  }
}

std::optional<D3d11VideoProcessorLayout> calculate_video_processor_layout(
    std::uint32_t input_width, std::uint32_t input_height,
    std::uint32_t output_width, std::uint32_t output_height) noexcept {
  if (input_width == 0 || input_height == 0 || output_width == 0 ||
      output_height == 0) {
    return std::nullopt;
  }

  std::uint32_t scaled_width = output_width;
  std::uint32_t scaled_height = output_height;
  if (static_cast<std::uint64_t>(input_width) * output_height >
      static_cast<std::uint64_t>(output_width) * input_height) {
    scaled_height = static_cast<std::uint32_t>(
        static_cast<std::uint64_t>(output_width) * input_height / input_width);
    scaled_height &= ~1U;
  } else {
    scaled_width = static_cast<std::uint32_t>(
        static_cast<std::uint64_t>(output_height) * input_width / input_height);
    scaled_width &= ~1U;
  }
  if (scaled_width == 0 || scaled_height == 0) {
    return std::nullopt;
  }
  const std::uint32_t left = ((output_width - scaled_width) / 2U) & ~1U;
  const std::uint32_t top = ((output_height - scaled_height) / 2U) & ~1U;
  return D3d11VideoProcessorLayout{
      .source = {.left = 0,
                 .top = 0,
                 .right = input_width,
                 .bottom = input_height},
      .destination = {.left = left,
                      .top = top,
                      .right = left + scaled_width,
                      .bottom = top + scaled_height},
  };
}

std::unique_ptr<ID3d11VideoProcessorPlatform>
create_windows_d3d11_video_processor_platform() {
  return std::make_unique<WindowsD3d11VideoProcessorPlatform>();
}

}  // namespace beacon::worker::video
