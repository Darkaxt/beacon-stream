#include "beacon/worker/capture/wgc_display_capture.h"

#include <Windows.h>
#include <d3d11.h>
#include <dwmapi.h>
#include <dxgi1_6.h>
#include <roapi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>

#include <MddBootstrap.h>
#include <WindowsAppSDK-VersionInfo.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.Display.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <winrt/Windows.Security.Authorization.AppCapabilityAccess.h>
#include <winrt/Microsoft.UI.Interop.h>
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

class ThreadWinrtApartment final {
 public:
  ThreadWinrtApartment() {
    const auto result = RoInitialize(RO_INIT_MULTITHREADED);
    if (FAILED(result) && result != RPC_E_CHANGED_MODE) {
      winrt::throw_hresult(result);
    }
    uninitialize_ = SUCCEEDED(result);
  }

  ~ThreadWinrtApartment() {
    if (uninitialize_) {
      RoUninitialize();
    }
  }

 private:
  bool uninitialize_{};
};

void ensure_winrt_apartment() {
  thread_local ThreadWinrtApartment apartment;
  static_cast<void>(apartment);
}

class WindowsAppRuntimeBootstrap final {
 public:
  WindowsAppRuntimeBootstrap() noexcept {
    module_ = LoadLibraryExW(L"Microsoft.WindowsAppRuntime.Bootstrap.dll",
                             nullptr,
                             LOAD_LIBRARY_SEARCH_APPLICATION_DIR |
                                 LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (module_ == nullptr) {
      result_ = HRESULT_FROM_WIN32(GetLastError());
      return;
    }

    const auto initialize = reinterpret_cast<MddBootstrapInitialize2Fn>(
        GetProcAddress(module_, "MddBootstrapInitialize2"));
    shutdown_ = reinterpret_cast<MddBootstrapShutdownFn>(
        GetProcAddress(module_, "MddBootstrapShutdown"));
    if (initialize == nullptr || shutdown_ == nullptr) {
      result_ = HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
      return;
    }

    PACKAGE_VERSION minimum{};
    minimum.Version = WINDOWSAPPSDK_RUNTIME_VERSION_UINT64;
    result_ = initialize(
        WINDOWSAPPSDK_RELEASE_MAJORMINOR,
        WINDOWSAPPSDK_RELEASE_VERSION_TAG_W,
        minimum,
        MddBootstrapInitializeOptions_OnPackageIdentity_NOOP);
    initialized_ = SUCCEEDED(result_);
  }

  ~WindowsAppRuntimeBootstrap() {
    if (initialized_) {
      shutdown_();
    }
    if (module_ != nullptr) {
      FreeLibrary(module_);
    }
  }

  WindowsAppRuntimeBootstrap(const WindowsAppRuntimeBootstrap&) = delete;
  WindowsAppRuntimeBootstrap& operator=(
      const WindowsAppRuntimeBootstrap&) = delete;

  [[nodiscard]] HRESULT result() const noexcept { return result_; }

 private:
  using MddBootstrapInitialize2Fn = HRESULT(WINAPI*)(
      UINT32,
      PCWSTR,
      PACKAGE_VERSION,
      MddBootstrapInitializeOptions) noexcept;
  using MddBootstrapShutdownFn = void(WINAPI*)() noexcept;

  HMODULE module_{};
  MddBootstrapShutdownFn shutdown_{};
  HRESULT result_{E_UNEXPECTED};
  bool initialized_{};
};

struct CaptureItemAttempt {
  GraphicsCaptureItem item{nullptr};
  WgcCapturePlatformStage stage{WgcCapturePlatformStage::none};
  HRESULT result{S_OK};
};

CaptureItemAttempt create_capture_item_from_display_id(HMONITOR monitor) {
  static WindowsAppRuntimeBootstrap runtime;
  if (FAILED(runtime.result())) {
    return {
        .stage = WgcCapturePlatformStage::capture_item_display_id_runtime,
        .result = runtime.result(),
    };
  }

  auto stage = WgcCapturePlatformStage::capture_item_display_id_access;
  try {
    static const auto access = GraphicsCaptureAccess::RequestAccessAsync(
                                   GraphicsCaptureAccessKind::Programmatic)
                                   .get();
    if (access != winrt::Windows::Security::Authorization::
                      AppCapabilityAccess::AppCapabilityAccessStatus::Allowed) {
      return {.stage = stage, .result = E_ACCESSDENIED};
    }

    stage = WgcCapturePlatformStage::capture_item_display_id_mapping;
    const auto mapped =
        winrt::Microsoft::UI::GetDisplayIdFromMonitor(monitor);

    stage = WgcCapturePlatformStage::capture_item_display_id_creation;
    const winrt::Windows::Graphics::DisplayId display_id{mapped.Value};
    auto item = GraphicsCaptureItem::TryCreateFromDisplayId(display_id);
    if (!item) {
      return {
          .stage = stage,
          .result = HRESULT_FROM_WIN32(ERROR_NOT_FOUND),
      };
    }
    return {.item = std::move(item), .stage = stage, .result = S_OK};
  } catch (const winrt::hresult_error& failure) {
    return {.stage = stage, .result = failure.code()};
  } catch (...) {
    return {.stage = stage, .result = E_FAIL};
  }
}

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
  ~WindowsWgcCapturePlatform() override { stop_capture(); }

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
      FrameCallback callback,
      FailureCallback failure_callback) override {
    clear_failure();
    if (target.monitor == 0 || !callback || !failure_callback) {
      set_failure(WgcCapturePlatformStage::request_validation,
                  static_cast<std::uint32_t>(E_INVALIDARG));
      return false;
    }
    auto stage = WgcCapturePlatformStage::winrt_apartment;
    auto capture_monitor = reinterpret_cast<HMONITOR>(target.monitor);
    CaptureItemAttempt display_id_attempt;
    try {
      ensure_winrt_apartment();
      stage = WgcCapturePlatformStage::adapter_lookup;
      winrt::com_ptr<IDXGIAdapter1> selected = find_adapter(adapter.luid);
      if (!selected) {
        set_failure(stage, static_cast<std::uint32_t>(DXGI_ERROR_NOT_FOUND));
        return false;
      }
      stage = WgcCapturePlatformStage::d3d_device_creation;
      UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT |
                   D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
      D3D_FEATURE_LEVEL level{};
      winrt::com_ptr<ID3D11Device> d3d_device;
      winrt::com_ptr<ID3D11DeviceContext> d3d_context;
      const HRESULT device_result = D3D11CreateDevice(
          selected.get(), D3D_DRIVER_TYPE_UNKNOWN, nullptr, flags, nullptr, 0,
          D3D11_SDK_VERSION, d3d_device.put(), &level, d3d_context.put());
      if (FAILED(device_result)) {
        set_failure(stage, static_cast<std::uint32_t>(device_result));
        return false;
      }
      stage = WgcCapturePlatformStage::winrt_device_creation;
      winrt::com_ptr<IDXGIDevice> dxgi_device = d3d_device.as<IDXGIDevice>();
      winrt::com_ptr<IInspectable> inspectable;
      winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(
          dxgi_device.get(), inspectable.put()));
      IDirect3DDevice winrt_device = inspectable.as<IDirect3DDevice>();

      auto current_monitor = reinterpret_cast<HMONITOR>(
          find_monitor(target.device_name.c_str()));
      if (current_monitor == nullptr) {
        set_failure(WgcCapturePlatformStage::capture_item_display_id_mapping,
                    static_cast<std::uint32_t>(
                        HRESULT_FROM_WIN32(ERROR_NOT_FOUND)));
        return false;
      }
      capture_monitor = current_monitor;

      display_id_attempt = create_capture_item_from_display_id(current_monitor);
      if (!display_id_attempt.item && SUCCEEDED(DwmFlush())) {
        current_monitor = reinterpret_cast<HMONITOR>(
            find_monitor(target.device_name.c_str()));
        if (current_monitor != nullptr) {
          capture_monitor = current_monitor;
          display_id_attempt =
              create_capture_item_from_display_id(current_monitor);
        }
      }
      GraphicsCaptureItem item = std::move(display_id_attempt.item);
      if (!item) {
        stage = WgcCapturePlatformStage::capture_item_creation;
        auto factory = winrt::get_activation_factory<
            GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
        winrt::check_hresult(factory->CreateForMonitor(
            current_monitor,
            winrt::guid_of<GraphicsCaptureItem>(), winrt::put_abi(item)));
      }
      stage = WgcCapturePlatformStage::content_size_read;
      auto size = item.Size();
      if (size.Width <= 0 || size.Height <= 0) {
        set_failure(stage, static_cast<std::uint32_t>(E_UNEXPECTED));
        return false;
      }
      stage = WgcCapturePlatformStage::frame_pool_creation;
      auto pool = Direct3D11CaptureFramePool::CreateFreeThreaded(
          winrt_device, DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, size);
      stage = WgcCapturePlatformStage::capture_session_creation;
      auto session = pool.CreateCaptureSession(item);

      {
        std::lock_guard lock{mutex_};
        if (running_) {
          set_failure(WgcCapturePlatformStage::capture_session_creation,
                      static_cast<std::uint32_t>(
                          HRESULT_FROM_WIN32(ERROR_ALREADY_EXISTS)));
          return false;
        }
        callback_ = std::move(callback);
        failure_callback_ = std::move(failure_callback);
        d3d_device_ = std::move(d3d_device);
        d3d_context_ = std::move(d3d_context);
        winrt_device_ = std::move(winrt_device);
        item_ = std::move(item);
        frame_pool_ = std::move(pool);
        session_ = std::move(session);
        stage = WgcCapturePlatformStage::frame_event_registration;
        frame_token_ = frame_pool_.FrameArrived(
            {this, &WindowsWgcCapturePlatform::on_frame_arrived});
        running_ = true;
      }
      stage = WgcCapturePlatformStage::capture_start;
      session_.StartCapture();
      return true;
    } catch (const winrt::hresult_error& failure) {
      const auto native_code =
          static_cast<std::uint32_t>(failure.code().value);
      if (stage == WgcCapturePlatformStage::capture_item_creation) {
        const auto current_monitor_at_failure =
            find_monitor(target.device_name.c_str());
        const auto reported = detail::resolve_capture_item_failure(
            {.stage = display_id_attempt.stage,
             .native_code =
                 static_cast<std::uint32_t>(display_id_attempt.result)},
            native_code,
            current_monitor_at_failure ==
                reinterpret_cast<std::uint64_t>(capture_monitor));
        set_failure(reported.stage, reported.native_code);
      } else {
        set_failure(stage, native_code);
      }
      stop_capture();
      return false;
    } catch (...) {
      set_failure(stage, static_cast<std::uint32_t>(E_FAIL));
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
      ensure_winrt_apartment();
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

  [[nodiscard]] WgcCapturePlatformFailure
  capture_failure() const noexcept override {
    std::lock_guard lock{failure_mutex_};
    return failure_;
  }

  void stop_capture() noexcept override {
    try {
      ensure_winrt_apartment();
      Direct3D11CaptureFramePool pool{nullptr};
      GraphicsCaptureSession session{nullptr};
      {
        std::unique_lock lock{mutex_};
        if (!running_ && !frame_pool_ && !session_) {
          return;
        }
        running_ = false;
        callback_ = {};
        failure_callback_ = {};
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
  void clear_failure() noexcept {
    std::lock_guard lock{failure_mutex_};
    failure_ = {};
  }

  void set_failure(WgcCapturePlatformStage stage,
                   std::uint32_t native_code) noexcept {
    std::lock_guard lock{failure_mutex_};
    failure_ = {.stage = stage, .native_code = native_code};
  }

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
    FailureCallback failure_callback;
    {
      std::lock_guard lock{mutex_};
      if (!running_) {
        return;
      }
      ++active_callbacks_;
      callback = callback_;
      failure_callback = failure_callback_;
    }
    auto stage = WgcCapturePlatformStage::frame_acquisition;
    WgcCapturePlatformFailure runtime_failure;
    try {
      ensure_winrt_apartment();
      auto frame = sender.TryGetNextFrame();
      if (frame && callback) {
        stage = WgcCapturePlatformStage::frame_surface_access;
        auto access = frame.Surface().as<
            ::Windows::Graphics::DirectX::Direct3D11::
                IDirect3DDxgiInterfaceAccess>();
        stage = WgcCapturePlatformStage::frame_texture_access;
        winrt::com_ptr<ID3D11Texture2D> texture;
        winrt::check_hresult(
            access->GetInterface(IID_PPV_ARGS(texture.put())));
        stage = WgcCapturePlatformStage::frame_metadata;
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
    } catch (const winrt::hresult_error& failure) {
      runtime_failure = {
          .stage = stage,
          .native_code = static_cast<std::uint32_t>(failure.code().value),
      };
      set_failure(runtime_failure.stage, runtime_failure.native_code);
    } catch (...) {
      runtime_failure = {
          .stage = stage,
          .native_code = static_cast<std::uint32_t>(E_FAIL),
      };
      set_failure(runtime_failure.stage, runtime_failure.native_code);
    }
    if (runtime_failure.stage != WgcCapturePlatformStage::none &&
        failure_callback) {
      try {
        failure_callback(runtime_failure);
      } catch (...) {
      }
    }
    {
      std::lock_guard lock{mutex_};
      --active_callbacks_;
    }
    callbacks_drained_.notify_all();
  }

  std::mutex mutex_;
  mutable std::mutex failure_mutex_;
  std::condition_variable callbacks_drained_;
  FrameCallback callback_;
  FailureCallback failure_callback_;
  winrt::com_ptr<ID3D11Device> d3d_device_;
  winrt::com_ptr<ID3D11DeviceContext> d3d_context_;
  IDirect3DDevice winrt_device_{nullptr};
  GraphicsCaptureItem item_{nullptr};
  Direct3D11CaptureFramePool frame_pool_{nullptr};
  GraphicsCaptureSession session_{nullptr};
  winrt::event_token frame_token_{};
  std::size_t active_callbacks_{};
  bool running_{};
  WgcCapturePlatformFailure failure_{};
};

}  // namespace

std::unique_ptr<IWgcCapturePlatform> create_windows_wgc_capture_platform() {
  return std::make_unique<WindowsWgcCapturePlatform>();
}

}  // namespace beacon::worker::capture
