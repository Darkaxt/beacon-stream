#include "beacon/worker/video/d3d11_video_processor.h"
#include "beacon/worker/video/nvenc_h264_encoder.h"

#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <winrt/base.h>

#include <algorithm>
#include <array>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace {

using beacon::worker::capture::CapturedD3d11Frame;
using beacon::worker::capture::D3d11Texture;
using beacon::worker::video::create_windows_d3d11_video_processor_platform;
using beacon::worker::video::create_windows_nvenc_h264_api;
using beacon::worker::video::D3d11VideoProcessor;
using beacon::worker::video::D3d11VideoProcessorPlan;
using beacon::worker::video::EncodedH264AccessUnit;
using beacon::worker::video::NvencH264Encoder;
using beacon::worker::video::NvencH264Plan;

constexpr std::uint32_t input_width = 1280;
constexpr std::uint32_t input_height = 720;
constexpr std::uint32_t output_width = 640;
constexpr std::uint32_t output_height = 400;
constexpr std::uint32_t initial_bitrate = 8'000'000;
constexpr std::uint32_t reconfigured_bitrate = 6'000'000;

struct RgbColor {
  std::uint8_t red;
  std::uint8_t green;
  std::uint8_t blue;
};

constexpr std::array<RgbColor, 5> colors{{
    {0, 0, 0},
    {255, 255, 255},
    {255, 0, 0},
    {0, 255, 0},
    {0, 0, 255},
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
        D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION, device.put(), &feature_level,
        context.put()));
    return {std::move(device), std::move(context), description.Description};
  }
  throw std::runtime_error("No NVIDIA D3D11 adapter is available.");
}

std::shared_ptr<D3d11Texture> create_color_bars(ID3D11Device& device) {
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
  description.BindFlags = D3D11_BIND_RENDER_TARGET;
  D3D11_SUBRESOURCE_DATA initial{};
  initial.pSysMem = pixels.data();
  initial.SysMemPitch = input_width * 4U;
  winrt::com_ptr<ID3D11Texture2D> texture;
  winrt::check_hresult(
      device.CreateTexture2D(&description, &initial, texture.put()));
  return std::make_shared<ProbeTexture>(std::move(texture));
}

void write_access_units(const std::filesystem::path& output,
                        const std::vector<EncodedH264AccessUnit>& units) {
  std::ofstream stream(output, std::ios::binary | std::ios::trunc);
  if (!stream) {
    throw std::runtime_error("Unable to create the H.264 probe output.");
  }
  for (const auto& unit : units) {
    stream.write(reinterpret_cast<const char*>(unit.annex_b.data()),
                 static_cast<std::streamsize>(unit.annex_b.size()));
  }
  if (!stream) {
    throw std::runtime_error("Unable to write the H.264 probe output.");
  }
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
  try {
    if (argc != 2) {
      std::wcerr << L"Usage: BeaconStreamWorkerNvencH264EncoderProbe <output.h264>\n";
      return 2;
    }
    auto selected = create_nvidia_device();
    auto source = create_color_bars(*selected.device);
    D3d11VideoProcessor processor{
        create_windows_d3d11_video_processor_platform()};
    NvencH264Encoder encoder{create_windows_nvenc_h264_api(),
                             NvencH264Plan{
                                 .width = output_width,
                                 .height = output_height,
                                 .frame_rate_numerator = 120,
                                 .frame_rate_denominator = 1,
                                 .bitrate_bps = initial_bitrate,
                             }};
    const D3d11VideoProcessorPlan conversion_plan{
        .output_width = output_width,
        .output_height = output_height,
        .frame_rate_numerator = 120,
        .frame_rate_denominator = 1,
    };

    std::vector<EncodedH264AccessUnit> units;
    for (std::int64_t frame_index = 0; frame_index < 4; ++frame_index) {
      const CapturedD3d11Frame captured{
          .texture = source,
          .width = input_width,
          .height = input_height,
          .qpc_timestamp = 1000 + frame_index,
      };
      auto converted = processor.convert(captured, conversion_plan);
      if (!converted) {
        std::cerr << "D3D11 conversion failed: "
                  << static_cast<int>(processor.failure()) << '\n';
        return 3;
      }
      if (frame_index == 2 &&
          !encoder.reconfigure_bitrate(reconfigured_bitrate)) {
        std::cerr << "NVENC bitrate reconfiguration failed: "
                  << static_cast<int>(encoder.failure()) << '\n';
        return 4;
      }
      auto encoded = encoder.encode(*converted, frame_index == 2);
      if (!encoded) {
        std::cerr << "NVENC encode failed: "
                  << static_cast<int>(encoder.failure()) << '\n';
        return 5;
      }
      units.push_back(std::move(*encoded));
    }

    if (units.size() != 4 || !units[0].idr || !units[0].has_sps ||
        !units[0].has_pps || units[1].idr || !units[2].idr ||
        !units[2].has_sps || !units[2].has_pps || units[3].idr) {
      std::cerr << "NVENC access-unit sequence was invalid.\n";
      return 6;
    }
    write_access_units(std::filesystem::path{argv[1]}, units);
    std::size_t bytes{};
    for (const auto& unit : units) {
      bytes += unit.annex_b.size();
    }
    std::wcout << L"BEACON_NVENC_H264_OK adapter=\"" << selected.adapter_name
               << L"\" input=" << input_width << L"x" << input_height
               << L" output=" << output_width << L"x" << output_height
               << L" frames=" << units.size() << L" bytes=" << bytes
               << L" first_idr=1 forced_idr=1 bitrate_reconfigure=1\n";
    return 0;
  } catch (const std::exception& error) {
    std::cerr << "NVENC probe failed: " << error.what() << '\n';
    return 1;
  }
}
