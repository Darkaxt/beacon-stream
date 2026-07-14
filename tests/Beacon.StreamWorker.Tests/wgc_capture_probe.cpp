#include "beacon/worker/capture/wgc_display_capture.h"
#include "beacon/worker/video/d3d11_video_processor.h"

#include <Windows.h>
#include <d3d11.h>

#include <winrt/base.h>

#include <algorithm>
#include <condition_variable>
#include <cstdint>
#include <cwchar>
#include <iostream>
#include <memory>
#include <mutex>
#include <optional>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace {

using beacon::worker::capture::CapturedD3d11Frame;
using beacon::worker::capture::WgcCapturePlan;
using beacon::worker::capture::WgcDisplayCapture;
using beacon::worker::video::D3d11VideoProcessor;
using beacon::worker::video::D3d11VideoProcessorFailure;
using beacon::worker::video::D3d11VideoProcessorPlan;

constexpr UINT change_color_message = WM_APP + 1;

RECT monitor_rectangle(const std::wstring& device_name) {
  struct Search {
    const std::wstring* device_name;
    RECT rectangle{};
    bool found{};
  } search{&device_name};
  EnumDisplayMonitors(
      nullptr, nullptr,
      [](HMONITOR monitor, HDC, LPRECT, LPARAM parameter) -> BOOL {
        auto& value = *reinterpret_cast<Search*>(parameter);
        MONITORINFOEXW information{};
        information.cbSize = sizeof(information);
        if (GetMonitorInfoW(monitor, &information) &&
            _wcsicmp(information.szDevice, value.device_name->c_str()) == 0) {
          value.rectangle = information.rcMonitor;
          value.found = true;
          return FALSE;
        }
        return TRUE;
      },
      reinterpret_cast<LPARAM>(&search));
  return search.found ? search.rectangle : RECT{};
}

class PainterWindow final {
 public:
  explicit PainterWindow(RECT monitor) : monitor_(monitor) {
    thread_ = std::thread([this] { run(); });
    std::unique_lock lock{mutex_};
    ready_.wait(lock, [this] { return ready_flag_; });
    if (window_ == nullptr) {
      lock.unlock();
      thread_.join();
      throw std::runtime_error("Could not create the WGC paint probe window.");
    }
  }

  ~PainterWindow() { stop(); }

  void change() const noexcept {
    if (window_ != nullptr) {
      PostMessageW(window_, change_color_message, 0, 0);
    }
  }

  void stop() noexcept {
    if (window_ != nullptr) {
      PostMessageW(window_, WM_CLOSE, 0, 0);
    }
    if (thread_.joinable()) {
      thread_.join();
    }
    window_ = nullptr;
  }

 private:
  static LRESULT CALLBACK window_proc(HWND window, UINT message,
                                      WPARAM wparam, LPARAM lparam) {
    PainterWindow* owner = reinterpret_cast<PainterWindow*>(
        GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
      const auto* create = reinterpret_cast<CREATESTRUCTW*>(lparam);
      owner = static_cast<PainterWindow*>(create->lpCreateParams);
      SetWindowLongPtrW(window, GWLP_USERDATA,
                        reinterpret_cast<LONG_PTR>(owner));
    }
    if (owner == nullptr) {
      return DefWindowProcW(window, message, wparam, lparam);
    }
    switch (message) {
      case change_color_message:
        ++owner->phase_;
        InvalidateRect(window, nullptr, FALSE);
        UpdateWindow(window);
        return 0;
      case WM_PAINT: {
        PAINTSTRUCT paint{};
        HDC context = BeginPaint(window, &paint);
        const COLORREF color = owner->phase_ % 2 == 0
                                   ? RGB(24, 188, 72)
                                   : RGB(224, 42, 132);
        HBRUSH brush = CreateSolidBrush(color);
        FillRect(context, &paint.rcPaint, brush);
        DeleteObject(brush);
        EndPaint(window, &paint);
        return 0;
      }
      case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
      default:
        return DefWindowProcW(window, message, wparam, lparam);
    }
  }

  void run() {
    const std::wstring class_name =
        L"BeaconWgcCaptureProbe-" + std::to_wstring(GetCurrentProcessId());
    WNDCLASSW window_class{};
    window_class.lpfnWndProc = window_proc;
    window_class.hInstance = GetModuleHandleW(nullptr);
    window_class.lpszClassName = class_name.c_str();
    window_class.hCursor = LoadCursorW(nullptr, MAKEINTRESOURCEW(32512));
    const ATOM registered = RegisterClassW(&window_class);
    HWND window = nullptr;
    if (registered != 0) {
      const int monitor_width = monitor_.right - monitor_.left;
      const int monitor_height = monitor_.bottom - monitor_.top;
      window = CreateWindowExW(
          WS_EX_TOPMOST, class_name.c_str(), L"Beacon WGC Capture Probe",
          WS_POPUP | WS_VISIBLE, monitor_.left + monitor_width / 8,
          monitor_.top + monitor_height / 8,
          std::max(320, monitor_width / 2),
          std::max(240, monitor_height / 2), nullptr, nullptr,
          window_class.hInstance, this);
    }
    {
      std::lock_guard lock{mutex_};
      window_ = window;
      ready_flag_ = true;
    }
    ready_.notify_all();
    if (window != nullptr) {
      ShowWindow(window, SW_SHOW);
      UpdateWindow(window);
      MSG message{};
      while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
      }
    }
    if (registered != 0) {
      UnregisterClassW(class_name.c_str(), window_class.hInstance);
    }
  }

  RECT monitor_{};
  mutable std::mutex mutex_;
  std::condition_variable ready_;
  std::thread thread_;
  HWND window_{};
  int phase_{};
  bool ready_flag_{};
};

std::optional<std::uint64_t> texture_hash(
    const beacon::worker::capture::D3d11Texture& texture) {
  auto* source = static_cast<ID3D11Texture2D*>(texture.native_texture());
  if (source == nullptr) {
    return std::nullopt;
  }
  D3D11_TEXTURE2D_DESC description{};
  source->GetDesc(&description);
  winrt::com_ptr<ID3D11Device> device;
  source->GetDevice(device.put());
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
  if (FAILED(device->CreateTexture2D(&description, nullptr, staging.put()))) {
    return std::nullopt;
  }
  context->CopyResource(staging.get(), source);
  D3D11_MAPPED_SUBRESOURCE mapped{};
  if (FAILED(context->Map(staging.get(), 0, D3D11_MAP_READ, 0, &mapped))) {
    return std::nullopt;
  }
  std::uint64_t hash = 1469598103934665603ULL;
  const bool nv12 = description.Format == DXGI_FORMAT_NV12;
  const auto row_bytes =
      static_cast<std::size_t>(description.Width) * (nv12 ? 1U : 4U);
  const std::uint32_t rows =
      description.Height + (nv12 ? description.Height / 2U : 0U);
  for (std::uint32_t row = 0; row < rows; ++row) {
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
  if (argc != 4) {
    std::wcerr << L"Usage: BeaconStreamWorkerWgcCaptureProbe "
                  L"<\\\\.\\DISPLAYn> <width> <height>\n";
    return 2;
  }
  const std::wstring device_name = argv[1];
  const auto width = static_cast<std::uint32_t>(std::wcstoul(argv[2], nullptr, 10));
  const auto height = static_cast<std::uint32_t>(std::wcstoul(argv[3], nullptr, 10));
  const RECT rectangle = monitor_rectangle(device_name);
  if (rectangle.right <= rectangle.left || rectangle.bottom <= rectangle.top) {
    std::wcerr << L"Selected monitor is not active.\n";
    return 3;
  }

  try {
    PainterWindow painter{rectangle};
    WgcDisplayCapture capture{
        beacon::worker::capture::create_windows_wgc_capture_platform()};
    D3d11VideoProcessor processor{
        beacon::worker::video::create_windows_d3d11_video_processor_platform()};
    std::mutex mutex;
    std::condition_variable changed;
    std::vector<std::uint64_t> hashes;
    std::vector<std::int64_t> timestamps;
    bool hash_failed = false;
    D3d11VideoProcessorFailure conversion_failure{
        D3d11VideoProcessorFailure::none};
    const bool started = capture.start(
        WgcCapturePlan{.device_name = device_name,
                       .width = width,
                       .height = height},
        [&](CapturedD3d11Frame frame) {
          const auto converted = processor.convert(
              frame, D3d11VideoProcessorPlan{.output_width = width,
                                             .output_height = height,
                                             .frame_rate_numerator = 120,
                                             .frame_rate_denominator = 1});
          const auto hash = converted ? texture_hash(*converted->texture)
                                      : std::optional<std::uint64_t>{};
          std::lock_guard lock{mutex};
          if (!hash.has_value()) {
            hash_failed = true;
            conversion_failure = processor.failure();
            changed.notify_all();
            return;
          }
          if (hashes.empty() || hashes.back() != *hash) {
            hashes.push_back(*hash);
            timestamps.push_back(converted->qpc_timestamp);
            if (hashes.size() == 1) {
              painter.change();
            }
            changed.notify_all();
          }
        });
    if (!started) {
      std::wcerr << L"WGC start failed code="
                 << static_cast<int>(capture.failure()) << L"\n";
      return 4;
    }
    {
      std::unique_lock lock{mutex};
      changed.wait(lock, [&] { return hashes.size() >= 2 || hash_failed; });
    }
    capture.stop();
    painter.stop();
    if (hash_failed || hashes.size() < 2 || timestamps[1] <= timestamps[0]) {
      std::wcerr << L"WGC NV12 frame evidence was invalid. conversion="
                 << static_cast<int>(conversion_failure) << L"\n";
      return 5;
    }
    std::wcout << L"BEACON_WGC_CAPTURE_OK device=" << device_name
               << L" adapter=" << capture.selected_adapter_description()
               << L" size=" << width << L"x" << height << L" format=NV12"
               << L" frame1=" << hashes[0] << L" frame2=" << hashes[1]
               << L" qpc1=" << timestamps[0] << L" qpc2=" << timestamps[1]
               << L"\n";
    return 0;
  } catch (const std::exception& failure) {
    std::cerr << failure.what() << '\n';
    return 6;
  }
}
