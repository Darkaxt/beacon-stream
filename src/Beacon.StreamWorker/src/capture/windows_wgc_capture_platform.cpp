#include "beacon/worker/capture/wgc_display_capture.h"

#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <roapi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/base.h>

#include <algorithm>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <utility>
#include <vector>

namespace beacon::worker::capture {
namespace {

using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;

std::uint64_t pack_luid(const LUID& luid) noexcept {
  return static_cast<std::uint64_t>(static_cast<std::uint32_t>(luid.LowPart)) |
         (static_cast<std::uint64_t>(static_cast<std::uint32_t>(luid.HighPart))
          << 32U);
}

class WindowsD3d11Texture final : public D3d11Texture {
 public:
  explicit WindowsD3d11Texture(winrt::com_ptr<ID3D11Texture2D> texture)
      : texture_(std::move(texture)) {}

  [[nodiscard]] void* native_texture() const noexcept override {
    return texture_.get();
  }

 private:
  winrt::com_ptr<ID3D11Texture2D> texture_;
};

class WindowsWgcCapturePlatform final : public IWgcCapturePlatform {
 public:
  WindowsWgcCapturePlatform() {
    const auto result = RoInitialize(RO_INIT_MULTITHREADED);
    if (FAILED(result) && result != RPC_E_CHANGED_MODE) {
      winrt::throw_hresult(result);
    }
    uninitialize_ro_ = SUCCEEDED(result);
  }

  ~WindowsWgcCapturePlatform() override {
    stop_capture();
    if (uninitialize_ro_) {
      RoUninitialize();
    }
  }

  [[nodiscard]] std::vector<WgcDisplayTargetSnapshot>
  display_targets() override {
    std::vector<WgcDisplayTargetSnapshot> result;
    DISPLAY_DEVICEW device{};
    device.cb = sizeof(device);
    for (DWORD index = 0; EnumDisplayDevicesW(nullptr, index, &device, 0);
         ++index) {
      const bool active =
          (device.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0 &&
          (device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
      DEVMODEW mode{};
      mode.dmSize = sizeof(mode);
      const bool has_mode =
          EnumDisplaySettingsExW(device.DeviceName, ENUM_CURRENT_SETTINGS,
                                 &mode, 0) != FALSE;
      result.push_back({
          .device_name = device.DeviceName,
          .monitor = active ? find_monitor(device.DeviceName) : 0,
          .active = active,
          .width = has_mode ? mode.dmPelsWidth : 0,
          .height = has_mode ? mode.dmPelsHeight : 0,
      });
      device = {};
      device.cb = sizeof(device);
    }
    return result;
  }

  [[nodiscard]] std::vector<WgcAdapterSnapshot>
  graphics_adapters() override {
    std::vector<WgcAdapterSnapshot> result;
    winrt::com_ptr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(factory.put())))) {
      return result;
    }
    for (UINT index = 0;; ++index) {
      winrt::com_ptr<IDXGIAdapter1> adapter;
      if (factory->EnumAdapters1(index, adapter.put()) == DXGI_ERROR_NOT_FOUND) {
        break;
      }
      DXGI_ADAPTER_DESC1 description{};
      if (FAILED(adapter->GetDesc1(&description))) {
        continue;
      }
      result.push_back({
          .luid = pack_luid(description.AdapterLuid),
          .vendor_id = description.VendorId,
          .software = (description.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0,
          .dedicated_video_memory = description.DedicatedVideoMemory,
          .description = description.Description,
      });
    }
    return result;
  }

  [[nodiscard]] bool start_capture(
      const WgcDisplayTargetSnapshot& target,
      const WgcAdapterSnapshot& adapter,
      FrameCallback callback) override {
    if (target.monitor == 0 || !callback) {
      return false;
    }
    try {
      winrt::com_ptr<IDXGIAdapter1> selected = find_adapter(adapter.luid);
      if (!selected) {
        return false;
      }
      UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT |
                   D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
      D3D_FEATURE_LEVEL level{};
      winrt::com_ptr<ID3D11Device> d3d_device;
      winrt::com_ptr<ID3D11DeviceContext> d3d_context;
      if (FAILED(D3D11CreateDevice(
              selected.get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, flags, nullptr,
              0, D3D11_SDK_VERSION, d3d_device.put(), &level,
              d3d_context.put()))) {
        return false;
      }
      winrt::com_ptr<IDXGIDevice> dxgi_device = d3d_device.as<IDXGIDevice>();
      winrt::com_ptr<IInspectable> inspectable;
      winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(
          dxgi_device.get(), inspectable.put()));
      IDirect3DDevice winrt_device = inspectable.as<IDirect3DDevice>();

      auto factory = winrt::get_activation_factory<
          GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
      GraphicsCaptureItem item{nullptr};
      winrt::check_hresult(factory->CreateForMonitor(
          reinterpret_cast<HMONITOR>(target.monitor),
          winrt::guid_of<GraphicsCaptureItem>(), winrt::put_abi(item)));
      auto size = item.Size();
      if (size.Width <= 0 || size.Height <= 0) {
        return false;
      }
      auto pool = Direct3D11CaptureFramePool::CreateFreeThreaded(
          winrt_device, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
      auto session = pool.CreateCaptureSession(item);

      {
        std::lock_guard lock{mutex_};
        if (running_) {
          return false;
        }
        callback_ = std::move(callback);
        d3d_device_ = std::move(d3d_device);
        d3d_context_ = std::move(d3d_context);
        winrt_device_ = std::move(winrt_device);
        item_ = std::move(item);
        frame_pool_ = std::move(pool);
        session_ = std::move(session);
        frame_token_ = frame_pool_.FrameArrived(
            {this, &WindowsWgcCapturePlatform::on_frame_arrived});
        running_ = true;
      }
      session_.StartCapture();
      return true;
    } catch (...) {
      stop_capture();
      return false;
    }
  }

  [[nodiscard]] bool recreate_frame_pool(std::uint32_t width,
                                         std::uint32_t height) override {
    if (width == 0 || height == 0) {
      return false;
    }
    try {
      std::lock_guard lock{mutex_};
      if (!running_ || !frame_pool_ || !winrt_device_) {
        return false;
      }
      frame_pool_.Recreate(
          winrt_device_, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2,
          {static_cast<std::int32_t>(width),
           static_cast<std::int32_t>(height)});
      return true;
    } catch (...) {
      return false;
    }
  }

  void stop_capture() noexcept override {
    try {
      Direct3D11CaptureFramePool pool{nullptr};
      GraphicsCaptureSession session{nullptr};
      {
        std::unique_lock lock{mutex_};
        if (!running_ && !frame_pool_ && !session_) {
          return;
        }
        running_ = false;
        callback_ = {};
        callbacks_drained_.wait(lock,
                                [this] { return active_callbacks_ == 0; });
        if (frame_pool_) {
          frame_pool_.FrameArrived(frame_token_);
        }
        pool = std::exchange(frame_pool_, nullptr);
        session = std::exchange(session_, nullptr);
        item_ = nullptr;
        winrt_device_ = nullptr;
        d3d_context_ = nullptr;
        d3d_device_ = nullptr;
      }
      if (session) {
        session.Close();
      }
      if (pool) {
        pool.Close();
      }
    } catch (...) {
    }
  }

 private:
  static std::uint64_t find_monitor(const wchar_t* device_name) {
    struct Search {
      const wchar_t* device_name;
      HMONITOR monitor{};
    } search{device_name};
    EnumDisplayMonitors(
        nullptr, nullptr,
        [](HMONITOR monitor, HDC, LPRECT, LPARAM parameter) -> BOOL {
          auto& value = *reinterpret_cast<Search*>(parameter);
          MONITORINFOEXW information{};
          information.cbSize = sizeof(information);
          if (GetMonitorInfoW(monitor, &information) &&
              _wcsicmp(information.szDevice, value.device_name) == 0) {
            value.monitor = monitor;
            return FALSE;
          }
          return TRUE;
        },
        reinterpret_cast<LPARAM>(&search));
    return reinterpret_cast<std::uint64_t>(search.monitor);
  }

  static winrt::com_ptr<IDXGIAdapter1> find_adapter(std::uint64_t luid) {
    winrt::com_ptr<IDXGIFactory1> factory;
    if (FAILED(CreateDXGIFactory1(IID_PPV_ARGS(factory.put())))) {
      return nullptr;
    }
    for (UINT index = 0;; ++index) {
      winrt::com_ptr<IDXGIAdapter1> adapter;
      if (factory->EnumAdapters1(index, adapter.put()) == DXGI_ERROR_NOT_FOUND) {
        return nullptr;
      }
      DXGI_ADAPTER_DESC1 description{};
      if (SUCCEEDED(adapter->GetDesc1(&description)) &&
          pack_luid(description.AdapterLuid) == luid) {
        return adapter;
      }
    }
  }

  void on_frame_arrived(const Direct3D11CaptureFramePool& sender,
                        const winrt::Windows::Foundation::IInspectable&) noexcept {
    FrameCallback callback;
    {
      std::lock_guard lock{mutex_};
      if (!running_) {
        return;
      }
      ++active_callbacks_;
      callback = callback_;
    }
    try {
      auto frame = sender.TryGetNextFrame();
      if (frame && callback) {
        auto access = frame.Surface().as<
            ::Windows::Graphics::DirectX::Direct3D11::
                IDirect3DDxgiInterfaceAccess>();
        winrt::com_ptr<ID3D11Texture2D> texture;
        winrt::check_hresult(
            access->GetInterface(IID_PPV_ARGS(texture.put())));
        const auto size = frame.ContentSize();
        const auto timestamp = frame.SystemRelativeTime().count();
        frame.Close();
        callback({
            .texture =
                std::make_shared<WindowsD3d11Texture>(std::move(texture)),
            .width = static_cast<std::uint32_t>(std::max(size.Width, 0)),
            .height = static_cast<std::uint32_t>(std::max(size.Height, 0)),
            .qpc_timestamp = timestamp,
        });
      }
    } catch (...) {
    }
    {
      std::lock_guard lock{mutex_};
      --active_callbacks_;
    }
    callbacks_drained_.notify_all();
  }

  std::mutex mutex_;
  std::condition_variable callbacks_drained_;
  FrameCallback callback_;
  winrt::com_ptr<ID3D11Device> d3d_device_;
  winrt::com_ptr<ID3D11DeviceContext> d3d_context_;
  IDirect3DDevice winrt_device_{nullptr};
  GraphicsCaptureItem item_{nullptr};
  Direct3D11CaptureFramePool frame_pool_{nullptr};
  GraphicsCaptureSession session_{nullptr};
  winrt::event_token frame_token_{};
  std::size_t active_callbacks_{};
  bool running_{};
  bool uninitialize_ro_{};
};

}  // namespace

std::unique_ptr<IWgcCapturePlatform> create_windows_wgc_capture_platform() {
  return std::make_unique<WindowsWgcCapturePlatform>();
}

}  // namespace beacon::worker::capture
