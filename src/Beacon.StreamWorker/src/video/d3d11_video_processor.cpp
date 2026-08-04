#include "beacon/worker/video/d3d11_video_processor.h"

#include <Windows.h>
#include <d3d11.h>
#include <d3d11_1.h>
#include <d3dcompiler.h>
#include <dxgi1_2.h>
#include <winrt/base.h>

#include <algorithm>
#include <cstdint>
#include <memory>
#include <string_view>
#include <utility>

namespace beacon::worker::video {
namespace {

constexpr DXGI_FORMAT sdr_input_format = DXGI_FORMAT_B8G8R8A8_UNORM;
constexpr DXGI_FORMAT sdr_output_format = DXGI_FORMAT_NV12;
constexpr DXGI_COLOR_SPACE_TYPE sdr_input_color_space =
    DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709;
constexpr DXGI_COLOR_SPACE_TYPE sdr_output_color_space =
    DXGI_COLOR_SPACE_YCBCR_STUDIO_G22_LEFT_P709;
constexpr DXGI_FORMAT hdr_input_format = DXGI_FORMAT_R16G16B16A16_FLOAT;
constexpr DXGI_FORMAT hdr_output_format = DXGI_FORMAT_P010;
constexpr DXGI_COLOR_SPACE_TYPE hdr_input_color_space =
    DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709;
constexpr DXGI_COLOR_SPACE_TYPE hdr_output_color_space =
    DXGI_COLOR_SPACE_YCBCR_STUDIO_G2084_LEFT_P2020;

constexpr std::string_view hdr_shader_source{R"(
Texture2D<float4> source_texture : register(t0);
SamplerState source_sampler : register(s0);

struct VertexOutput {
  float4 position : SV_Position;
  float2 uv : TEXCOORD0;
};

VertexOutput vertex_main(uint id : SV_VertexID) {
  VertexOutput output;
  output.position = id == 0 ? float4(-1, -1, 0, 1)
                  : id == 1 ? float4(-1, 3, 0, 1)
                            : float4(3, -1, 0, 1);
  output.uv = id == 0 ? float2(0, 1)
            : id == 1 ? float2(0, -1)
                      : float2(2, 1);
  return output;
}

float3 nits_to_pq(float3 value) {
  const float m1 = 2610.0 / 4096.0 / 4.0;
  const float m2 = 2523.0 / 4096.0 * 128.0;
  const float c1 = 3424.0 / 4096.0;
  const float c2 = 2413.0 / 4096.0 * 32.0;
  const float c3 = 2392.0 / 4096.0 * 32.0;
  float3 powered = pow(saturate(value / 10000.0), m1);
  return pow((c1 + c2 * powered) / (1.0 + c3 * powered), m2);
}

float3 scrgb_to_pq2020(float3 value) {
  const float3x3 rec709_to_rec2020 = {
    0.627402, 0.329292, 0.043306,
    0.069095, 0.919544, 0.011360,
    0.016394, 0.088028, 0.895578
  };
  return nits_to_pq(mul(rec709_to_rec2020, value) * 80.0);
}

float3 rgb_to_ycbcr(float3 rgb) {
  float y = dot(rgb, float3(0.2627, 0.6780, 0.0593));
  float cb = (rgb.b - y) / 1.8814;
  float cr = (rgb.r - y) / 1.4746;
  return float3(y, cb, cr);
}

float y_main(VertexOutput input) : SV_Target {
  float y = rgb_to_ycbcr(scrgb_to_pq2020(
      source_texture.SampleLevel(source_sampler, input.uv, 0).rgb)).x;
  return (64.0 + 876.0 * y) / 1023.0;
}

float2 uv_main(VertexOutput input) : SV_Target {
  float3 yuv = rgb_to_ycbcr(scrgb_to_pq2020(
      source_texture.SampleLevel(source_sampler, input.uv, 0).rgb));
  return (float2(512.0, 512.0) + 896.0 * yuv.yz) / 1023.0;
}
)"};

winrt::com_ptr<ID3DBlob> compile_hdr_shader(const char* entry,
                                             const char* target) {
  winrt::com_ptr<ID3DBlob> shader;
  winrt::com_ptr<ID3DBlob> errors;
  const HRESULT result = D3DCompile(
      hdr_shader_source.data(), hdr_shader_source.size(), "beacon-hdr10", nullptr,
      nullptr, entry, target, D3DCOMPILE_ENABLE_STRICTNESS, 0, shader.put(),
      errors.put());
  if (FAILED(result)) {
    winrt::throw_hresult(result);
  }
  return shader;
}

D3d11VideoProcessorNativeConversionQuery native_query(
    const D3d11VideoProcessorConfiguration& configuration) noexcept {
  return configuration.output_format == VideoPixelFormat::p010
             ? d3d11_hdr10_video_conversion_query()
             : d3d11_sdr_video_conversion_query();
}

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
      const auto conversion = native_query(configuration);
      const auto input_format = static_cast<DXGI_FORMAT>(conversion.input_format);
      const auto output_format = static_cast<DXGI_FORMAT>(conversion.output_format);
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
      device_->GetImmediateContext(context_.put());
      if (!context_) {
        reset();
        return D3d11VideoProcessorFailure::device_unavailable;
      }
      if (configuration.output_format == VideoPixelFormat::p010) {
        return configure_hdr_shader(configuration);
      }
      if (FAILED(device_->QueryInterface(IID_PPV_ARGS(video_device_.put())))) {
        reset();
        return D3d11VideoProcessorFailure::device_unavailable;
      }
      if (FAILED(
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
      const HRESULT conversion_result =
          enumerator1->CheckVideoProcessorFormatConversion(
              static_cast<DXGI_FORMAT>(conversion.input_format),
              static_cast<DXGI_COLOR_SPACE_TYPE>(conversion.input_color_space),
              static_cast<DXGI_FORMAT>(conversion.output_format),
              static_cast<DXGI_COLOR_SPACE_TYPE>(conversion.output_color_space),
              &conversion_supported);
      const auto conversion_failure = classify_d3d11_video_conversion_query(
          static_cast<std::int32_t>(conversion_result),
          conversion_supported != FALSE, device_removed());
      if (conversion_failure != D3d11VideoProcessorFailure::none) {
        reset();
        return conversion_failure;
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

  [[nodiscard]] D3d11VideoProcessorTextureResult
  create_output_texture() noexcept override {
    try {
      if (!device_ ||
          (!processor_ && configuration_.output_format != VideoPixelFormat::p010)) {
        return {.failure =
                    D3d11VideoProcessorFailure::processor_creation_failed};
      }
      D3D11_TEXTURE2D_DESC description{};
      description.Width = configuration_.output_width;
      description.Height = configuration_.output_height;
      description.MipLevels = 1;
      description.ArraySize = 1;
      description.Format = static_cast<DXGI_FORMAT>(
          native_query(configuration_).output_format);
      description.SampleDesc.Count = 1;
      description.Usage = D3D11_USAGE_DEFAULT;
      description.BindFlags = D3D11_BIND_RENDER_TARGET;
      winrt::com_ptr<ID3D11Texture2D> texture;
      const HRESULT result =
          device_->CreateTexture2D(&description, nullptr, texture.put());
      if (FAILED(result)) {
        return {.failure =
                    device_removed()
                        ? D3d11VideoProcessorFailure::device_lost
                        : D3d11VideoProcessorFailure::output_creation_failed};
      }
      return {.texture =
                  std::make_shared<WindowsD3d11Texture>(std::move(texture))};
    } catch (...) {
      return {.failure =
                  device_removed()
                      ? D3d11VideoProcessorFailure::device_lost
                      : D3d11VideoProcessorFailure::output_creation_failed};
    }
  }

  [[nodiscard]] D3d11VideoProcessorFailure blit(
      const capture::D3d11Texture& input, capture::D3d11Texture& output,
      const D3d11VideoProcessorLayout& layout) noexcept override {
    try {
      if (configuration_.output_format != VideoPixelFormat::p010 &&
          (!video_device_ || !video_context_ || !video_context1_ ||
           !enumerator_ || !processor_)) {
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
      if (configuration_.output_format == VideoPixelFormat::p010) {
        return blit_hdr_shader(*input_texture, *output_texture, layout);
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
      const bool hdr = configuration_.output_format == VideoPixelFormat::p010;
      background.YCbCr.Y = hdr ? 64.0F / 1023.0F : 16.0F / 255.0F;
      background.YCbCr.Cb = hdr ? 512.0F / 1023.0F : 128.0F / 255.0F;
      background.YCbCr.Cr = hdr ? 512.0F / 1023.0F : 128.0F / 255.0F;
      background.YCbCr.A = 1.0F;
      video_context_->VideoProcessorSetOutputTargetRect(processor_.get(), TRUE,
                                                        &target);
      video_context_->VideoProcessorSetOutputBackgroundColor(processor_.get(),
                                                             TRUE, &background);
      video_context_->VideoProcessorSetStreamFrameFormat(
          processor_.get(), 0, D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
      video_context_->VideoProcessorSetStreamAutoProcessingMode(
          processor_.get(), 0, FALSE);
      video_context_->VideoProcessorSetStreamSourceRect(processor_.get(), 0,
                                                        TRUE, &source);
      video_context_->VideoProcessorSetStreamDestRect(processor_.get(), 0, TRUE,
                                                      &destination);
      const auto conversion = native_query(configuration_);
      video_context1_->VideoProcessorSetStreamColorSpace1(
          processor_.get(), 0,
          static_cast<DXGI_COLOR_SPACE_TYPE>(conversion.input_color_space));
      video_context1_->VideoProcessorSetOutputColorSpace1(
          processor_.get(),
          static_cast<DXGI_COLOR_SPACE_TYPE>(conversion.output_color_space));

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
    hdr_sampler_ = nullptr;
    hdr_uv_shader_ = nullptr;
    hdr_y_shader_ = nullptr;
    hdr_vertex_shader_ = nullptr;
    hdr_input_srv_ = nullptr;
    hdr_input_texture_ = nullptr;
    device_ = nullptr;
    configuration_ = {};
  }

 private:
  [[nodiscard]] D3d11VideoProcessorFailure configure_hdr_shader(
      const D3d11VideoProcessorConfiguration& configuration) noexcept {
    try {
      D3D11_TEXTURE2D_DESC input_description{};
      input_description.Width = configuration.input_width;
      input_description.Height = configuration.input_height;
      input_description.MipLevels = 1;
      input_description.ArraySize = 1;
      input_description.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
      input_description.SampleDesc.Count = 1;
      input_description.Usage = D3D11_USAGE_DEFAULT;
      input_description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
      winrt::check_hresult(device_->CreateTexture2D(
          &input_description, nullptr, hdr_input_texture_.put()));
      winrt::check_hresult(device_->CreateShaderResourceView(
          hdr_input_texture_.get(), nullptr, hdr_input_srv_.put()));

      const auto vertex = compile_hdr_shader("vertex_main", "vs_5_0");
      const auto y = compile_hdr_shader("y_main", "ps_5_0");
      const auto uv = compile_hdr_shader("uv_main", "ps_5_0");
      winrt::check_hresult(device_->CreateVertexShader(
          vertex->GetBufferPointer(), vertex->GetBufferSize(), nullptr,
          hdr_vertex_shader_.put()));
      winrt::check_hresult(device_->CreatePixelShader(
          y->GetBufferPointer(), y->GetBufferSize(), nullptr,
          hdr_y_shader_.put()));
      winrt::check_hresult(device_->CreatePixelShader(
          uv->GetBufferPointer(), uv->GetBufferSize(), nullptr,
          hdr_uv_shader_.put()));

      D3D11_SAMPLER_DESC sampler{};
      sampler.Filter = D3D11_FILTER_MIN_MAG_LINEAR_MIP_POINT;
      sampler.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
      sampler.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
      sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
      sampler.MaxLOD = D3D11_FLOAT32_MAX;
      winrt::check_hresult(
          device_->CreateSamplerState(&sampler, hdr_sampler_.put()));

      D3D11_TEXTURE2D_DESC output_description{};
      output_description.Width = configuration.output_width;
      output_description.Height = configuration.output_height;
      output_description.MipLevels = 1;
      output_description.ArraySize = 1;
      output_description.Format = DXGI_FORMAT_P010;
      output_description.SampleDesc.Count = 1;
      output_description.Usage = D3D11_USAGE_DEFAULT;
      output_description.BindFlags = D3D11_BIND_RENDER_TARGET;
      winrt::com_ptr<ID3D11Texture2D> output;
      winrt::check_hresult(device_->CreateTexture2D(
          &output_description, nullptr, output.put()));
      winrt::com_ptr<ID3D11RenderTargetView> y_view;
      winrt::com_ptr<ID3D11RenderTargetView> uv_view;
      if (!create_p010_views(*output, y_view, uv_view)) {
        reset();
        return D3d11VideoProcessorFailure::unsupported_format;
      }
      configuration_ = configuration;
      return D3d11VideoProcessorFailure::none;
    } catch (...) {
      const auto failure = device_removed()
                               ? D3d11VideoProcessorFailure::device_lost
                               : D3d11VideoProcessorFailure::processor_creation_failed;
      reset();
      return failure;
    }
  }

  [[nodiscard]] bool create_p010_views(
      ID3D11Texture2D& output,
      winrt::com_ptr<ID3D11RenderTargetView>& y_view,
      winrt::com_ptr<ID3D11RenderTargetView>& uv_view) noexcept {
    D3D11_RENDER_TARGET_VIEW_DESC description{};
    description.ViewDimension = D3D11_RTV_DIMENSION_TEXTURE2D;
    description.Format = DXGI_FORMAT_R16_UNORM;
    if (FAILED(device_->CreateRenderTargetView(&output, &description,
                                                y_view.put()))) {
      return false;
    }
    description.Format = DXGI_FORMAT_R16G16_UNORM;
    return SUCCEEDED(device_->CreateRenderTargetView(&output, &description,
                                                      uv_view.put()));
  }

  [[nodiscard]] D3d11VideoProcessorFailure blit_hdr_shader(
      ID3D11Texture2D& input, ID3D11Texture2D& output,
      const D3d11VideoProcessorLayout& layout) noexcept {
    winrt::com_ptr<ID3D11RenderTargetView> y_view;
    winrt::com_ptr<ID3D11RenderTargetView> uv_view;
    if (!create_p010_views(output, y_view, uv_view)) {
      return D3d11VideoProcessorFailure::blit_failed;
    }
    context_->CopyResource(hdr_input_texture_.get(), &input);
    constexpr float y_black[4]{64.0F / 1023.0F, 0, 0, 0};
    constexpr float uv_black[4]{512.0F / 1023.0F,
                                512.0F / 1023.0F, 0, 0};
    context_->ClearRenderTargetView(y_view.get(), y_black);
    context_->ClearRenderTargetView(uv_view.get(), uv_black);
    context_->IASetInputLayout(nullptr);
    context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    context_->VSSetShader(hdr_vertex_shader_.get(), nullptr, 0);
    ID3D11ShaderResourceView* source = hdr_input_srv_.get();
    context_->PSSetShaderResources(0, 1, &source);
    ID3D11SamplerState* sampler = hdr_sampler_.get();
    context_->PSSetSamplers(0, 1, &sampler);

    const D3D11_VIEWPORT y_viewport{
        .TopLeftX = static_cast<float>(layout.destination.left),
        .TopLeftY = static_cast<float>(layout.destination.top),
        .Width = static_cast<float>(layout.destination.right -
                                   layout.destination.left),
        .Height = static_cast<float>(layout.destination.bottom -
                                    layout.destination.top),
        .MinDepth = 0.0F,
        .MaxDepth = 1.0F};
    ID3D11RenderTargetView* y_target = y_view.get();
    context_->OMSetRenderTargets(1, &y_target, nullptr);
    context_->RSSetViewports(1, &y_viewport);
    context_->PSSetShader(hdr_y_shader_.get(), nullptr, 0);
    context_->Draw(3, 0);

    D3D11_VIEWPORT uv_viewport = y_viewport;
    uv_viewport.TopLeftX *= 0.5F;
    uv_viewport.TopLeftY *= 0.5F;
    uv_viewport.Width *= 0.5F;
    uv_viewport.Height *= 0.5F;
    ID3D11RenderTargetView* uv_target = uv_view.get();
    context_->OMSetRenderTargets(1, &uv_target, nullptr);
    context_->RSSetViewports(1, &uv_viewport);
    context_->PSSetShader(hdr_uv_shader_.get(), nullptr, 0);
    context_->Draw(3, 0);

    ID3D11RenderTargetView* no_target{};
    ID3D11ShaderResourceView* no_source{};
    context_->OMSetRenderTargets(1, &no_target, nullptr);
    context_->PSSetShaderResources(0, 1, &no_source);
    return device_removed() ? D3d11VideoProcessorFailure::device_lost
                            : D3d11VideoProcessorFailure::none;
  }

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
  winrt::com_ptr<ID3D11Texture2D> hdr_input_texture_;
  winrt::com_ptr<ID3D11ShaderResourceView> hdr_input_srv_;
  winrt::com_ptr<ID3D11VertexShader> hdr_vertex_shader_;
  winrt::com_ptr<ID3D11PixelShader> hdr_y_shader_;
  winrt::com_ptr<ID3D11PixelShader> hdr_uv_shader_;
  winrt::com_ptr<ID3D11SamplerState> hdr_sampler_;
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
  const bool hdr = plan.dynamic_range == stream::v1::DYNAMIC_RANGE_HDR10;
  if ((hdr && input.pixel_format != capture::WgcCapturePixelFormat::rgba16_float) ||
      (!hdr && input.pixel_format != capture::WgcCapturePixelFormat::bgra8)) {
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
      .input_format = hdr ? VideoPixelFormat::rgba16_float
                          : VideoPixelFormat::bgra8,
      .output_format = hdr ? VideoPixelFormat::p010 : VideoPixelFormat::nv12,
      .input_range = VideoRange::full,
      .output_range = VideoRange::limited,
      .matrix = hdr ? VideoColorMatrix::bt2020_non_constant_luminance
                    : VideoColorMatrix::bt709,
      .transfer_function = hdr ? VideoTransferFunction::pq
                               : VideoTransferFunction::bt709,
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
    auto created = platform_->create_output_texture();
    failure_ = created.failure;
    output = std::move(created.texture);
    if (failure_ != D3d11VideoProcessorFailure::none || !output ||
        output->native_texture() == nullptr) {
      if (failure_ == D3d11VideoProcessorFailure::none) {
        failure_ = D3d11VideoProcessorFailure::output_creation_failed;
      }
      if (failure_ == D3d11VideoProcessorFailure::device_lost) {
        discard_gpu_state();
      }
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
      .format = configuration.output_format,
      .range = configuration.output_range,
      .matrix = configuration.matrix,
      .transfer_function = configuration.transfer_function,
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

D3d11VideoProcessorNativeConversionQuery
d3d11_sdr_video_conversion_query() noexcept {
  return {
      .input_format = static_cast<std::uint32_t>(sdr_input_format),
      .input_color_space = static_cast<std::uint32_t>(sdr_input_color_space),
      .output_format = static_cast<std::uint32_t>(sdr_output_format),
      .output_color_space = static_cast<std::uint32_t>(sdr_output_color_space),
  };
}

D3d11VideoProcessorNativeConversionQuery
d3d11_hdr10_video_conversion_query() noexcept {
  return {
      .input_format = static_cast<std::uint32_t>(hdr_input_format),
      .input_color_space = static_cast<std::uint32_t>(hdr_input_color_space),
      .output_format = static_cast<std::uint32_t>(hdr_output_format),
      .output_color_space = static_cast<std::uint32_t>(hdr_output_color_space),
  };
}

D3d11VideoProcessorFailure classify_d3d11_video_conversion_query(
    std::int32_t status, bool supported, bool removed) noexcept {
  if (removed) {
    return D3d11VideoProcessorFailure::device_lost;
  }
  if (status < 0 || !supported) {
    return D3d11VideoProcessorFailure::unsupported_color_conversion;
  }
  return D3d11VideoProcessorFailure::none;
}

std::unique_ptr<ID3d11VideoProcessorPlatform>
create_windows_d3d11_video_processor_platform() {
  return std::make_unique<WindowsD3d11VideoProcessorPlatform>();
}

D3d11VideoProcessorFailure
probe_windows_d3d11_hdr10_video_conversion() noexcept {
  try {
    winrt::com_ptr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(factory.put())))) {
      return D3d11VideoProcessorFailure::device_unavailable;
    }
    for (UINT index = 0;; ++index) {
      winrt::com_ptr<IDXGIAdapter1> adapter;
      if (factory->EnumAdapters1(index, adapter.put()) == DXGI_ERROR_NOT_FOUND) {
        break;
      }
      DXGI_ADAPTER_DESC1 adapter_description{};
      if (FAILED(adapter->GetDesc1(&adapter_description)) ||
          adapter_description.VendorId != 0x10de ||
          (adapter_description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) {
        continue;
      }
      winrt::com_ptr<ID3D11Device> device;
      winrt::com_ptr<ID3D11DeviceContext> context;
      D3D_FEATURE_LEVEL level{};
      if (FAILED(D3D11CreateDevice(
              adapter.get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
              D3D11_CREATE_DEVICE_VIDEO_SUPPORT, nullptr, 0, D3D11_SDK_VERSION,
              device.put(), &level, context.put()))) {
        continue;
      }
      D3D11_TEXTURE2D_DESC description{};
      description.Width = 2;
      description.Height = 2;
      description.MipLevels = 1;
      description.ArraySize = 1;
      description.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
      description.SampleDesc.Count = 1;
      description.Usage = D3D11_USAGE_DEFAULT;
      description.BindFlags = D3D11_BIND_RENDER_TARGET;
      winrt::com_ptr<ID3D11Texture2D> texture;
      if (FAILED(device->CreateTexture2D(&description, nullptr,
                                         texture.put()))) {
        continue;
      }
      WindowsD3d11Texture input{std::move(texture)};
      auto platform = create_windows_d3d11_video_processor_platform();
      return platform->configure(
          input,
          {.input_width = 2,
           .input_height = 2,
           .output_width = 2,
           .output_height = 2,
           .frame_rate_numerator = 60,
           .frame_rate_denominator = 1,
           .input_format = VideoPixelFormat::rgba16_float,
           .output_format = VideoPixelFormat::p010,
           .input_range = VideoRange::full,
           .output_range = VideoRange::limited,
           .matrix = VideoColorMatrix::bt2020_non_constant_luminance,
           .transfer_function = VideoTransferFunction::pq});
    }
    return D3d11VideoProcessorFailure::device_unavailable;
  } catch (...) {
    return D3d11VideoProcessorFailure::device_unavailable;
  }
}

}  // namespace beacon::worker::video
