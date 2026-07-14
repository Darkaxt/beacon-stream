#include "beacon/worker/video/d3d11_video_processor.h"

#include <Windows.h>
#include <d3d11.h>
#include <d3d11sdklayers.h>
#include <dxgi1_6.h>
#include <winrt/base.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <iostream>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace {

using beacon::worker::capture::CapturedD3d11Frame;
using beacon::worker::capture::D3d11Texture;
using beacon::worker::video::D3d11VideoProcessor;
using beacon::worker::video::D3d11VideoProcessorPlan;

constexpr std::uint32_t input_width = 1280;
constexpr std::uint32_t input_height = 720;
constexpr std::uint32_t output_width = 640;
constexpr std::uint32_t output_height = 400;
constexpr int sample_tolerance = 5;

struct RgbColor {
  std::uint8_t red;
  std::uint8_t green;
  std::uint8_t blue;
};

struct YuvColor {
  int y;
  int u;
  int v;
};

constexpr std::array<RgbColor, 5> colors{{
    {0, 0, 0},
    {255, 255, 255},
    {255, 0, 0},
    {0, 255, 0},
    {0, 0, 255},
}};

constexpr std::array<YuvColor, 5> expected{{
    {16, 128, 128},
    {235, 128, 128},
    {63, 102, 240},
    {173, 42, 26},
    {32, 240, 118},
}};

class ProbeTexture final : public D3d11Texture {
 public:
  explicit ProbeTexture(winrt::com_ptr<ID3D11Texture2D> texture)
      : texture_(std::move(texture)) {}

  [[nodiscard]] void* native_texture() const noexcept override {
    return texture_.get();
  }

 private:
  winrt::com_ptr<ID3D11Texture2D> texture_;
};

struct DeviceSelection {
  winrt::com_ptr<ID3D11Device> device;
  winrt::com_ptr<ID3D11DeviceContext> context;
  std::wstring adapter_name;
};

DeviceSelection create_nvidia_device() {
  winrt::com_ptr<IDXGIFactory1> factory;
  winrt::check_hresult(CreateDXGIFactory1(IID_PPV_ARGS(factory.put())));
  for (UINT index = 0;; ++index) {
    winrt::com_ptr<IDXGIAdapter1> adapter;
    const HRESULT enumerated = factory->EnumAdapters1(index, adapter.put());
    if (enumerated == DXGI_ERROR_NOT_FOUND) {
      break;
    }
    winrt::check_hresult(enumerated);
    DXGI_ADAPTER_DESC1 description{};
    winrt::check_hresult(adapter->GetDesc1(&description));
    if (description.VendorId != 0x10de ||
        (description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) {
      continue;
    }
    winrt::com_ptr<ID3D11Device> device;
    winrt::com_ptr<ID3D11DeviceContext> context;
    D3D_FEATURE_LEVEL feature_level{};
    winrt::check_hresult(D3D11CreateDevice(
        adapter.get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT |
            D3D11_CREATE_DEVICE_DEBUG,
        nullptr, 0, D3D11_SDK_VERSION, device.put(), &feature_level,
        context.put()));
    return {std::move(device), std::move(context), description.Description};
  }
  throw std::runtime_error("No NVIDIA D3D11 adapter is available.");
}

void print_debug_messages(ID3D11Device& device) {
  winrt::com_ptr<ID3D11InfoQueue> queue;
  if (FAILED(device.QueryInterface(IID_PPV_ARGS(queue.put())))) {
    return;
  }
  const UINT64 count = queue->GetNumStoredMessagesAllowedByRetrievalFilter();
  for (UINT64 index = 0; index < count; ++index) {
    SIZE_T size{};
    if (FAILED(queue->GetMessage(index, nullptr, &size)) || size == 0) {
      continue;
    }
    std::vector<std::uint8_t> storage(size);
    auto* message = reinterpret_cast<D3D11_MESSAGE*>(storage.data());
    if (SUCCEEDED(queue->GetMessage(index, message, &size))) {
      std::cerr << "D3D11: " << message->pDescription << '\n';
    }
  }
}

std::shared_ptr<D3d11Texture> create_color_bars(ID3D11Device& device,
                                                UINT bind_flags) {
  std::vector<std::uint8_t> pixels(static_cast<std::size_t>(input_width) *
                                   input_height * 4U);
  for (std::uint32_t y = 0; y < input_height; ++y) {
    for (std::uint32_t x = 0; x < input_width; ++x) {
      const std::size_t bar = std::min<std::size_t>(
          static_cast<std::size_t>(x) * colors.size() / input_width,
          colors.size() - 1U);
      const auto color = colors[bar];
      const std::size_t offset =
          (static_cast<std::size_t>(y) * input_width + x) * 4U;
      pixels[offset] = color.blue;
      pixels[offset + 1U] = color.green;
      pixels[offset + 2U] = color.red;
      pixels[offset + 3U] = 255;
    }
  }

  D3D11_TEXTURE2D_DESC description{};
  description.Width = input_width;
  description.Height = input_height;
  description.MipLevels = 1;
  description.ArraySize = 1;
  description.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
  description.SampleDesc.Count = 1;
  description.Usage = D3D11_USAGE_DEFAULT;
  description.BindFlags = bind_flags;
  D3D11_SUBRESOURCE_DATA initial{};
  initial.pSysMem = pixels.data();
  initial.SysMemPitch = input_width * 4U;
  winrt::com_ptr<ID3D11Texture2D> texture;
  winrt::check_hresult(
      device.CreateTexture2D(&description, &initial, texture.put()));
  return std::make_shared<ProbeTexture>(std::move(texture));
}

bool within_tolerance(std::uint8_t actual, int wanted) {
  return std::abs(static_cast<int>(actual) - wanted) <= sample_tolerance;
}

bool validate_nv12(ID3D11Device& device, ID3D11DeviceContext& context,
                   D3d11Texture& converted) {
  auto* output = static_cast<ID3D11Texture2D*>(converted.native_texture());
  if (output == nullptr) {
    return false;
  }
  D3D11_TEXTURE2D_DESC description{};
  output->GetDesc(&description);
  if (description.Width != output_width ||
      description.Height != output_height ||
      description.Format != DXGI_FORMAT_NV12) {
    return false;
  }
  description.Usage = D3D11_USAGE_STAGING;
  description.BindFlags = 0;
  description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
  description.MiscFlags = 0;
  winrt::com_ptr<ID3D11Texture2D> staging;
  winrt::check_hresult(
      device.CreateTexture2D(&description, nullptr, staging.put()));
  context.CopyResource(staging.get(), output);
  D3D11_MAPPED_SUBRESOURCE mapped{};
  winrt::check_hresult(
      context.Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped));

  const auto* bytes = static_cast<const std::uint8_t*>(mapped.pData);
  const auto* uv_plane =
      bytes + static_cast<std::size_t>(mapped.RowPitch) * output_height;
  bool valid = true;
  for (std::size_t index = 0; index < colors.size(); ++index) {
    std::uint32_t x = static_cast<std::uint32_t>(
        (index * output_width + output_width / 2U) / colors.size());
    x &= ~1U;
    const std::uint32_t y = output_height / 2U;
    const std::uint8_t actual_y =
        bytes[static_cast<std::size_t>(y) * mapped.RowPitch + x];
    const std::uint8_t actual_u =
        uv_plane[static_cast<std::size_t>(y / 2U) * mapped.RowPitch + x];
    const std::uint8_t actual_v =
        uv_plane[static_cast<std::size_t>(y / 2U) * mapped.RowPitch + x + 1U];
    std::cout << "bar=" << index << " yuv=" << static_cast<int>(actual_y) << ','
              << static_cast<int>(actual_u) << ',' << static_cast<int>(actual_v)
              << '\n';
    valid = valid && within_tolerance(actual_y, expected[index].y) &&
            within_tolerance(actual_u, expected[index].u) &&
            within_tolerance(actual_v, expected[index].v);
  }
  const std::uint32_t background_x = output_width / 2U;
  const std::uint32_t background_y = 10;
  const std::uint8_t background_luma =
      bytes[static_cast<std::size_t>(background_y) * mapped.RowPitch +
            background_x];
  const std::uint8_t background_u =
      uv_plane[static_cast<std::size_t>(background_y / 2U) * mapped.RowPitch +
               background_x];
  const std::uint8_t background_v =
      uv_plane[static_cast<std::size_t>(background_y / 2U) * mapped.RowPitch +
               background_x + 1U];
  std::cout << "letterbox_yuv=" << static_cast<int>(background_luma) << ','
            << static_cast<int>(background_u) << ','
            << static_cast<int>(background_v) << '\n';
  valid = valid && within_tolerance(background_luma, 16) &&
          within_tolerance(background_u, 128) &&
          within_tolerance(background_v, 128);
  context.Unmap(staging.get(), 0);
  return valid;
}

}  // namespace

int main() {
  try {
    auto selected = create_nvidia_device();
    D3d11VideoProcessor processor{
        beacon::worker::video::create_windows_d3d11_video_processor_platform()};
    auto rejected_input =
        create_color_bars(*selected.device, D3D11_BIND_SHADER_RESOURCE);
    const auto rejected = processor.convert(
        CapturedD3d11Frame{.texture = std::move(rejected_input),
                           .width = input_width,
                           .height = input_height,
                           .qpc_timestamp = 1},
        D3d11VideoProcessorPlan{.output_width = output_width,
                                .output_height = output_height,
                                .frame_rate_numerator = 120,
                                .frame_rate_denominator = 1});
    if (rejected || processor.failure() !=
                        beacon::worker::video::D3d11VideoProcessorFailure::
                            unsupported_format) {
      std::cerr << "Shader-only input was not rejected during configuration. "
                   "code="
                << static_cast<int>(processor.failure()) << '\n';
      print_debug_messages(*selected.device);
      return 2;
    }

    auto input = create_color_bars(*selected.device, 0);
    const auto converted = processor.convert(
        CapturedD3d11Frame{.texture = std::move(input),
                           .width = input_width,
                           .height = input_height,
                           .qpc_timestamp = 12345},
        D3d11VideoProcessorPlan{.output_width = output_width,
                                .output_height = output_height,
                                .frame_rate_numerator = 120,
                                .frame_rate_denominator = 1});
    if (!converted || converted->qpc_timestamp != 12345) {
      std::cerr << "D3D11 conversion failed code="
                << static_cast<int>(processor.failure()) << '\n';
      print_debug_messages(*selected.device);
      return 3;
    }
    if (!validate_nv12(*selected.device, *selected.context,
                       *converted->texture)) {
      std::cerr << "NV12 color-bar evidence exceeded tolerance="
                << sample_tolerance << '\n';
      return 4;
    }
    std::wcout << L"BEACON_D3D11_VIDEO_PROCESSOR_OK adapter=\""
               << selected.adapter_name << L"\" input=" << input_width << L'x'
               << input_height << L" output=" << output_width << L'x'
               << output_height << L" tolerance=" << sample_tolerance << L'\n';
    return 0;
  } catch (const winrt::hresult_error& failure) {
    std::wcerr << L"HRESULT failure 0x" << std::hex
               << static_cast<std::uint32_t>(failure.code().value) << L": "
               << failure.message().c_str() << L'\n';
    return 5;
  } catch (const std::exception& failure) {
    std::cerr << failure.what() << '\n';
    return 6;
  }
}
