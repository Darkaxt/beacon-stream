#include <Windows.h>

#include <MddBootstrap.h>
#include <WindowsAppSDK-VersionInfo.h>

#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.Display.h>
#include <winrt/Windows.Security.Authorization.AppCapabilityAccess.h>
#include <winrt/Microsoft.UI.Interop.h>
#include <winrt/base.h>

#include <cstdint>
#include <cwchar>
#include <iostream>
#include <string>

namespace {

using BootstrapInitialize = HRESULT(WINAPI*)(
    UINT32,
    PCWSTR,
    PACKAGE_VERSION,
    MddBootstrapInitializeOptions) noexcept;
using BootstrapShutdown = void(WINAPI*)() noexcept;

struct MonitorSearch {
  std::wstring device_name;
  HMONITOR monitor{};
};

class LoadedModule final {
 public:
  explicit LoadedModule(HMODULE module) : module_(module) {}
  ~LoadedModule() {
    if (module_ != nullptr) {
      FreeLibrary(module_);
    }
  }

  LoadedModule(const LoadedModule&) = delete;
  LoadedModule& operator=(const LoadedModule&) = delete;

  [[nodiscard]] HMODULE get() const noexcept { return module_; }
  [[nodiscard]] explicit operator bool() const noexcept {
    return module_ != nullptr;
  }

 private:
  HMODULE module_{};
};

BOOL CALLBACK find_monitor(HMONITOR monitor, HDC, LPRECT, LPARAM context) {
  auto& search = *reinterpret_cast<MonitorSearch*>(context);
  MONITORINFOEXW information{};
  information.cbSize = sizeof(information);
  if (GetMonitorInfoW(monitor, &information) != FALSE &&
      _wcsicmp(information.szDevice, search.device_name.c_str()) == 0) {
    search.monitor = monitor;
    return FALSE;
  }
  return TRUE;
}

}  // namespace

int wmain(int argc, wchar_t** argv) {
  if (argc != 2) {
    std::wcerr << L"Usage: BeaconStreamWorkerDisplayIdCaptureProbe "
                  L"<\\\\.\\DISPLAYn>\n";
    return 2;
  }

  try {
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    MonitorSearch search{.device_name = argv[1]};
    EnumDisplayMonitors(nullptr, nullptr, find_monitor,
                        reinterpret_cast<LPARAM>(&search));
    if (search.monitor == nullptr) {
      std::wcerr << L"Display monitor was not found.\n";
      return 3;
    }

    const auto access = winrt::Windows::Graphics::Capture::
        GraphicsCaptureAccess::RequestAccessAsync(
            winrt::Windows::Graphics::Capture::
                GraphicsCaptureAccessKind::Programmatic)
            .get();
    std::wcerr << L"Programmatic capture access="
               << static_cast<std::int32_t>(access) << L"\n";

    LoadedModule bootstrap{LoadLibraryExW(
        L"Microsoft.WindowsAppRuntime.Bootstrap.dll", nullptr,
        LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32)};
    if (!bootstrap) {
      std::wcerr << L"Windows App Runtime bootstrap failed to load code="
                 << GetLastError() << L"\n";
      return 4;
    }
    const auto initialize = reinterpret_cast<BootstrapInitialize>(
        GetProcAddress(bootstrap.get(), "MddBootstrapInitialize2"));
    const auto shutdown = reinterpret_cast<BootstrapShutdown>(
        GetProcAddress(bootstrap.get(), "MddBootstrapShutdown"));
    if (initialize == nullptr || shutdown == nullptr) {
      std::wcerr << L"Windows App Runtime bootstrap exports were unavailable.\n";
      return 5;
    }

    PACKAGE_VERSION minimum{};
    minimum.Version = WINDOWSAPPSDK_RUNTIME_VERSION_UINT64;
    const HRESULT initialized = initialize(
        WINDOWSAPPSDK_RELEASE_MAJORMINOR,
        WINDOWSAPPSDK_RELEASE_VERSION_TAG_W,
        minimum,
        MddBootstrapInitializeOptions_OnPackageIdentity_NOOP);
    std::wcerr << L"Windows App Runtime bootstrap=0x" << std::hex
               << static_cast<std::uint32_t>(initialized) << L"\n";
    if (FAILED(initialized)) {
      return 6;
    }
    const auto mapped =
        winrt::Microsoft::UI::GetDisplayIdFromMonitor(search.monitor);
    const winrt::Windows::Graphics::DisplayId display_id{mapped.Value};
    std::wcerr << L"DisplayId value=0x" << std::hex << display_id.Value
               << L"\n";

    {
      const auto item = winrt::Windows::Graphics::Capture::GraphicsCaptureItem::
          TryCreateFromDisplayId(display_id);
      if (!item) {
        std::wcerr << L"TryCreateFromDisplayId returned no capture item.\n";
        return 7;
      }
      const auto size = item.Size();
      std::wcout << L"BEACON_DISPLAY_ID_CAPTURE_ITEM_OK device=" << argv[1]
                 << L" width=" << size.Width << L" height=" << size.Height
                 << L" access=" << static_cast<std::int32_t>(access) << L"\n";
    }
    shutdown();
    return 0;
  } catch (const winrt::hresult_error& error) {
    std::wcerr << L"DisplayId capture probe failed platform=0x" << std::hex
               << static_cast<std::uint32_t>(error.code().value)
               << L" message=\"" << error.message().c_str() << L"\"\n";
    return 8;
  } catch (...) {
    std::wcerr << L"DisplayId capture probe failed unexpectedly.\n";
    return 9;
  }
}
