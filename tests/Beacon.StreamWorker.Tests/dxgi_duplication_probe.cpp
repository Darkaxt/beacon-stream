#include <Windows.h>
#include <d3d11.h>
#include <d3d11_1.h>
#include <dxgi1_6.h>

#include <winrt/base.h>

#include "beacon/worker/video/d3d11_video_processor.h"
#include "beacon/worker/video/nvenc_h264_encoder.h"

#include <algorithm>
#include <cstdint>
#include <cwchar>
#include <iostream>
#include <memory>
#include <optional>
#include <string>
#include <utility>

namespace {

class ProbeTexture final : public beacon::worker::capture::D3d11Texture {
 public:
  explicit ProbeTexture(winrt::com_ptr<ID3D11Texture2D> texture)
      : texture_(std::move(texture)) {}

  [[nodiscard]] void* native_texture() const noexcept override {
    return texture_.get();
  }

 private:
  winrt::com_ptr<ID3D11Texture2D> texture_;
};

struct SharedTextureTransfer {
  winrt::com_ptr<ID3D11Texture2D> capture_texture;
  winrt::com_ptr<IDXGIKeyedMutex> capture_mutex;
  winrt::com_ptr<ID3D11DeviceContext> capture_context;
  winrt::com_ptr<ID3D11Texture2D> encoder_texture;
  winrt::com_ptr<IDXGIKeyedMutex> encoder_mutex;
  winrt::com_ptr<ID3D11DeviceContext> encoder_context;
  winrt::handle shared_handle;
};

std::optional<SharedTextureTransfer> create_shared_texture_transfer(
    ID3D11Texture2D& source, IDXGIFactory1& factory) {
  D3D11_TEXTURE2D_DESC description{};
  source.GetDesc(&description);
  description.Usage = D3D11_USAGE_DEFAULT;
  description.BindFlags =
      D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET;
  description.CPUAccessFlags = 0;
  description.MiscFlags = D3D11_RESOURCE_MISC_SHARED_NTHANDLE |
                          D3D11_RESOURCE_MISC_SHARED_KEYEDMUTEX;

  SharedTextureTransfer transfer;
  winrt::com_ptr<ID3D11Device> capture_device;
  source.GetDevice(capture_device.put());
  capture_device->GetImmediateContext(transfer.capture_context.put());
  HRESULT status = capture_device->CreateTexture2D(
      &description, nullptr, transfer.capture_texture.put());
  if (FAILED(status)) {
    std::wcerr << L"Shared texture creation failed platform=0x" << std::hex
               << static_cast<std::uint32_t>(status) << L"\n";
    return std::nullopt;
  }
  status = transfer.capture_texture->QueryInterface(
      IID_PPV_ARGS(transfer.capture_mutex.put()));
  if (FAILED(status)) {
    std::wcerr << L"Capture keyed mutex creation failed platform=0x"
               << std::hex << static_cast<std::uint32_t>(status) << L"\n";
    return std::nullopt;
  }
  winrt::com_ptr<IDXGIResource1> shared_resource;
  status = transfer.capture_texture->QueryInterface(
      IID_PPV_ARGS(shared_resource.put()));
  if (FAILED(status)) {
    std::wcerr << L"Shared resource query failed platform=0x" << std::hex
               << static_cast<std::uint32_t>(status) << L"\n";
    return std::nullopt;
  }
  status = shared_resource->CreateSharedHandle(
      nullptr, DXGI_SHARED_RESOURCE_READ | DXGI_SHARED_RESOURCE_WRITE,
      nullptr, transfer.shared_handle.put());
  if (FAILED(status)) {
    std::wcerr << L"Shared handle creation failed platform=0x" << std::hex
               << static_cast<std::uint32_t>(status) << L"\n";
    return std::nullopt;
  }

  for (UINT adapter_index = 0;; ++adapter_index) {
    winrt::com_ptr<IDXGIAdapter1> adapter;
    if (factory.EnumAdapters1(adapter_index, adapter.put()) ==
        DXGI_ERROR_NOT_FOUND) {
      break;
    }
    DXGI_ADAPTER_DESC1 adapter_description{};
    if (FAILED(adapter->GetDesc1(&adapter_description)) ||
        adapter_description.VendorId != 0x10de ||
        (adapter_description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) {
      continue;
    }
    winrt::com_ptr<ID3D11Device> encoder_device;
    D3D_FEATURE_LEVEL feature_level{};
    status = D3D11CreateDevice(
        adapter.get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION, encoder_device.put(), &feature_level,
        transfer.encoder_context.put());
    if (FAILED(status)) {
      std::wcerr << L"Encoder device creation failed platform=0x" << std::hex
                 << static_cast<std::uint32_t>(status) << L"\n";
      return std::nullopt;
    }
    winrt::com_ptr<ID3D11Device1> encoder_device1;
    status =
        encoder_device->QueryInterface(IID_PPV_ARGS(encoder_device1.put()));
    if (FAILED(status)) {
      std::wcerr << L"Encoder device1 query failed platform=0x" << std::hex
                 << static_cast<std::uint32_t>(status) << L"\n";
      return std::nullopt;
    }
    status = encoder_device1->OpenSharedResource1(
        transfer.shared_handle.get(), __uuidof(ID3D11Texture2D),
        transfer.encoder_texture.put_void());
    if (FAILED(status)) {
      std::wcerr << L"Cross-adapter shared texture open failed platform=0x"
                 << std::hex << static_cast<std::uint32_t>(status) << L"\n";
      return std::nullopt;
    }
    status = transfer.encoder_texture->QueryInterface(
        IID_PPV_ARGS(transfer.encoder_mutex.put()));
    if (FAILED(status)) {
      std::wcerr << L"Encoder keyed mutex query failed platform=0x" << std::hex
                 << static_cast<std::uint32_t>(status) << L"\n";
      return std::nullopt;
    }
    return transfer;
  }
  std::wcerr << L"NVIDIA adapter was not available for shared transfer.\n";
  return std::nullopt;
}

std::optional<std::uint64_t> texture_hash(ID3D11Texture2D& source) {
  D3D11_TEXTURE2D_DESC description{};
  source.GetDesc(&description);
  winrt::com_ptr<ID3D11Device> device;
  source.GetDevice(device.put());
  if (!device) {
    return std::nullopt;
  }
  winrt::com_ptr<ID3D11DeviceContext> context;
  device->GetImmediateContext(context.put());
  description.Usage = D3D11_USAGE_STAGING;
  description.BindFlags = 0;
  description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
  description.MiscFlags = 0;
  winrt::com_ptr<ID3D11Texture2D> staging;
  const HRESULT created =
      device->CreateTexture2D(&description, nullptr, staging.put());
  if (FAILED(created)) {
    return std::nullopt;
  }
  context->CopyResource(staging.get(), &source);
  D3D11_MAPPED_SUBRESOURCE mapped{};
  const HRESULT mapped_result =
      context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped);
  if (FAILED(mapped_result)) {
    return std::nullopt;
  }
  std::uint64_t hash = 1469598103934665603ULL;
  const auto row_bytes = std::min<std::size_t>(
      mapped.RowPitch, static_cast<std::size_t>(description.Width) * 4U);
  for (std::uint32_t row = 0; row < description.Height; ++row) {
    const auto* bytes = static_cast<const std::uint8_t*>(mapped.pData) +
                        static_cast<std::size_t>(row) * mapped.RowPitch;
    for (std::size_t index = 0; index < row_bytes; index += 16U) {
      hash ^= bytes[index];
      hash *= 1099511628211ULL;
    }
  }
  context->Unmap(staging.get(), 0);
  return hash;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
  if (argc != 2) {
    std::wcerr << L"Usage: BeaconStreamWorkerDxgiDuplicationProbe "
                  L"<\\\\.\\DISPLAYn>\n";
    return 2;
  }
  const std::wstring requested = argv[1];
  winrt::com_ptr<IDXGIFactory1> factory;
  HRESULT status = CreateDXGIFactory1(IID_PPV_ARGS(factory.put()));
  if (FAILED(status)) {
    std::wcerr << L"CreateDXGIFactory1 failed platform=0x" << std::hex
               << static_cast<std::uint32_t>(status) << L"\n";
    return 3;
  }

  for (UINT adapter_index = 0;; ++adapter_index) {
    winrt::com_ptr<IDXGIAdapter1> adapter;
    if (factory->EnumAdapters1(adapter_index, adapter.put()) ==
        DXGI_ERROR_NOT_FOUND) {
      break;
    }
    DXGI_ADAPTER_DESC1 adapter_description{};
    if (FAILED(adapter->GetDesc1(&adapter_description))) {
      continue;
    }
    for (UINT output_index = 0;; ++output_index) {
      winrt::com_ptr<IDXGIOutput> output;
      if (adapter->EnumOutputs(output_index, output.put()) ==
          DXGI_ERROR_NOT_FOUND) {
        break;
      }
      DXGI_OUTPUT_DESC output_description{};
      if (FAILED(output->GetDesc(&output_description)) ||
          _wcsicmp(output_description.DeviceName, requested.c_str()) != 0) {
        continue;
      }

      std::wcerr << L"DXGI output device=" << output_description.DeviceName
                 << L" adapter=\"" << adapter_description.Description
                 << L"\" attached="
                 << (output_description.AttachedToDesktop ? L"yes" : L"no")
                 << L" desktopCoordinates="
                 << output_description.DesktopCoordinates.left << L","
                 << output_description.DesktopCoordinates.top << L","
                 << output_description.DesktopCoordinates.right << L","
                 << output_description.DesktopCoordinates.bottom << L"\n";

      winrt::com_ptr<ID3D11Device> device;
      winrt::com_ptr<ID3D11DeviceContext> context;
      D3D_FEATURE_LEVEL feature_level{};
      status = D3D11CreateDevice(
          adapter.get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr,
          D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
          nullptr, 0, D3D11_SDK_VERSION, device.put(), &feature_level,
          context.put());
      if (FAILED(status)) {
        std::wcerr << L"D3D11CreateDevice failed adapter=\""
                   << adapter_description.Description << L"\" platform=0x"
                   << std::hex << static_cast<std::uint32_t>(status) << L"\n";
        return 4;
      }

      winrt::com_ptr<IDXGIOutputDuplication> duplication;
      winrt::com_ptr<IDXGIOutput5> output5;
      HRESULT duplicate_output1_status = E_NOINTERFACE;
      if (SUCCEEDED(output->QueryInterface(IID_PPV_ARGS(output5.put())))) {
        constexpr DXGI_FORMAT formats[]{DXGI_FORMAT_B8G8R8A8_UNORM};
        duplicate_output1_status = output5->DuplicateOutput1(
            device.get(), 0, static_cast<UINT>(std::size(formats)), formats,
            duplication.put());
      }
      if (FAILED(duplicate_output1_status)) {
        duplication = nullptr;
        winrt::com_ptr<IDXGIOutput1> output1;
        status = output->QueryInterface(IID_PPV_ARGS(output1.put()));
        if (SUCCEEDED(status)) {
          status = output1->DuplicateOutput(device.get(), duplication.put());
        }
      } else {
        status = duplicate_output1_status;
      }
      if (FAILED(status)) {
        std::wcerr << L"DuplicateOutput failed adapter=\""
                   << adapter_description.Description
                   << L"\" duplicateOutput1=0x" << std::hex
                   << static_cast<std::uint32_t>(duplicate_output1_status)
                   << L" duplicateOutput=0x"
                   << static_cast<std::uint32_t>(status) << L"\n";
        return 5;
      }

      std::optional<std::uint64_t> first_hash;
      const auto output_width = static_cast<std::uint32_t>(
          output_description.DesktopCoordinates.right -
          output_description.DesktopCoordinates.left);
      const auto output_height = static_cast<std::uint32_t>(
          output_description.DesktopCoordinates.bottom -
          output_description.DesktopCoordinates.top);
      beacon::worker::video::D3d11VideoProcessor processor{
          beacon::worker::video::create_windows_d3d11_video_processor_platform()};
      beacon::worker::video::NvencH264Encoder encoder{
          beacon::worker::video::create_windows_nvenc_h264_api(),
          {.width = output_width,
           .height = output_height,
           .frame_rate_numerator = 120,
           .frame_rate_denominator = 1,
           .bitrate_bps = 40'000'000}};
      bool encoded_frame{};
      std::optional<SharedTextureTransfer> transfer;
      for (;;) {
        DXGI_OUTDUPL_FRAME_INFO frame_information{};
        winrt::com_ptr<IDXGIResource> resource;
        status = duplication->AcquireNextFrame(
            INFINITE, &frame_information, resource.put());
        if (FAILED(status)) {
          std::wcerr << L"AcquireNextFrame failed platform=0x" << std::hex
                     << static_cast<std::uint32_t>(status)
                     << L" factoryCurrent="
                     << (factory->IsCurrent() ? L"yes" : L"no") << L"\n";
          return 6;
        }
        winrt::com_ptr<ID3D11Texture2D> texture;
        const HRESULT texture_result =
            resource->QueryInterface(IID_PPV_ARGS(texture.put()));
        const auto hash = SUCCEEDED(texture_result) && texture
                              ? texture_hash(*texture)
                              : std::optional<std::uint64_t>{};
        if (texture && !encoded_frame) {
          if (!transfer) {
            transfer = create_shared_texture_transfer(*texture, *factory);
          }
          if (!transfer) {
            duplication->ReleaseFrame();
            return 12;
          }
          status = transfer->capture_mutex->AcquireSync(0, INFINITE);
          if (FAILED(status)) {
            std::wcerr << L"Capture mutex acquisition failed platform=0x"
                       << std::hex << static_cast<std::uint32_t>(status)
                       << L"\n";
            duplication->ReleaseFrame();
            return 13;
          }
          transfer->capture_context->CopyResource(
              transfer->capture_texture.get(), texture.get());
          transfer->capture_context->Flush();
          status = transfer->capture_mutex->ReleaseSync(1);
          if (FAILED(status)) {
            std::wcerr << L"Capture mutex release failed platform=0x"
                       << std::hex << static_cast<std::uint32_t>(status)
                       << L"\n";
            duplication->ReleaseFrame();
            return 14;
          }
          status = transfer->encoder_mutex->AcquireSync(1, INFINITE);
          if (FAILED(status)) {
            std::wcerr << L"Encoder mutex acquisition failed platform=0x"
                       << std::hex << static_cast<std::uint32_t>(status)
                       << L"\n";
            duplication->ReleaseFrame();
            return 15;
          }
          LARGE_INTEGER timestamp{};
          QueryPerformanceCounter(&timestamp);
          const auto converted = processor.convert(
              {.texture =
                   std::make_shared<ProbeTexture>(transfer->encoder_texture),
               .width = output_width,
               .height = output_height,
               .qpc_timestamp = timestamp.QuadPart},
              {.output_width = output_width,
               .output_height = output_height,
               .frame_rate_numerator = 120,
               .frame_rate_denominator = 1});
          if (!converted) {
            std::wcerr << L"Video conversion failed boundary=processor code="
                       << static_cast<std::uint32_t>(processor.failure())
                       << L"\n";
            transfer->encoder_mutex->ReleaseSync(0);
            duplication->ReleaseFrame();
            return 9;
          }
          const auto access_unit = encoder.encode(*converted, true);
          if (!access_unit) {
            std::wcerr << L"Video encoding failed boundary=encoder code="
                       << static_cast<std::uint32_t>(encoder.failure())
                       << L"\n";
            transfer->encoder_mutex->ReleaseSync(0);
            duplication->ReleaseFrame();
            return 10;
          }
          transfer->encoder_context->Flush();
          status = transfer->encoder_mutex->ReleaseSync(0);
          if (FAILED(status)) {
            std::wcerr << L"Encoder mutex release failed platform=0x"
                       << std::hex << static_cast<std::uint32_t>(status)
                       << L"\n";
            duplication->ReleaseFrame();
            return 16;
          }
          encoded_frame = true;
          std::wcerr << L"Encoded virtual-display frame bytes="
                     << access_unit->annex_b.size() << L"\n";
        }
        const HRESULT released = duplication->ReleaseFrame();
        if (FAILED(released) || !hash) {
          std::wcerr << L"Duplication frame was invalid.\n";
          return 7;
        }
        if (!first_hash) {
          first_hash = hash;
        } else if (*first_hash != *hash) {
          if (!encoded_frame) {
            std::wcerr << L"No frame reached the encoder.\n";
            return 11;
          }
          std::wcout << L"BEACON_DXGI_DUPLICATION_OK device=" << requested
                     << L" adapter=\"" << adapter_description.Description
                     << L"\" frame1=" << *first_hash << L" frame2=" << *hash
                     << L"\n";
          return 0;
        }
      }
    }
  }

  std::wcerr << L"DXGI did not expose output " << requested << L".\n";
  return 8;
}
