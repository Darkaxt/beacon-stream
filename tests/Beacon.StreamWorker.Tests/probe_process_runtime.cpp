#include "probe_process_runtime.h"

#include <Windows.h>
#include <crtdbg.h>

#include <csignal>
#include <cstdlib>
#include <exception>

namespace beacon::worker::tests {
namespace {

constexpr UINT fatal_exit_code = 134;

void write_fatal_marker(const char *marker, DWORD marker_size) noexcept {
  const HANDLE error = GetStdHandle(STD_ERROR_HANDLE);
  if (error != nullptr && error != INVALID_HANDLE_VALUE) {
    DWORD written = 0;
    WriteFile(error, marker, marker_size, &written, nullptr);
  }
  OutputDebugStringA(marker);
}

[[noreturn]] void exit_after_fatal_marker() noexcept {
  TerminateProcess(GetCurrentProcess(), fatal_exit_code);
  std::_Exit(fatal_exit_code);
}

[[noreturn]] void terminate_handler() noexcept {
  constexpr char marker[] = "BEACON_NATIVE_PROBE_FATAL TERMINATE\n";
  write_fatal_marker(marker, sizeof(marker) - 1);
  exit_after_fatal_marker();
}

void abort_handler(int) {
  constexpr char marker[] = "BEACON_NATIVE_PROBE_FATAL SIGABRT\n";
  write_fatal_marker(marker, sizeof(marker) - 1);
  exit_after_fatal_marker();
}

} // namespace

void configure_noninteractive_probe_process() noexcept {
  constexpr UINT required_error_mode =
      SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX;
  SetErrorMode(GetErrorMode() | required_error_mode);

  constexpr unsigned int abort_flags = _WRITE_ABORT_MSG | _CALL_REPORTFAULT;
  _set_abort_behavior(0, abort_flags);

  constexpr int report_types[] = {_CRT_WARN, _CRT_ERROR, _CRT_ASSERT};
  for (const int report_type : report_types) {
    _CrtSetReportMode(report_type, _CRTDBG_MODE_FILE);
    _CrtSetReportFile(report_type, _CRTDBG_FILE_STDERR);
  }

  std::set_terminate(terminate_handler);
  std::signal(SIGABRT, abort_handler);
}

} // namespace beacon::worker::tests
