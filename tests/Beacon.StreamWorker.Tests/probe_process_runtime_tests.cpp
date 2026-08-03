#include "probe_process_runtime.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <Windows.h>
#include <crtdbg.h>

#include <csignal>
#include <cstdlib>
#include <exception>

int main() {
  const auto initial_terminate_handler = std::get_terminate();

  beacon::worker::tests::configure_noninteractive_probe_process();

  constexpr UINT required_error_mode =
      SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX;
  BEACON_TEST_REQUIRE((GetErrorMode() & required_error_mode) ==
                      required_error_mode);

  constexpr int report_types[] = {_CRT_WARN, _CRT_ERROR, _CRT_ASSERT};
  for (const int report_type : report_types) {
    BEACON_TEST_REQUIRE(_CrtSetReportMode(report_type, _CRTDBG_REPORT_MODE) ==
                        _CRTDBG_MODE_FILE);
  }

  constexpr unsigned int abort_flags = _WRITE_ABORT_MSG | _CALL_REPORTFAULT;
  const auto configured_abort_behavior = _set_abort_behavior(0, abort_flags);
  BEACON_TEST_REQUIRE((configured_abort_behavior & abort_flags) == 0);

  BEACON_TEST_REQUIRE(std::get_terminate() != initial_terminate_handler);
  const auto configured_abort_handler = std::signal(SIGABRT, SIG_DFL);
  BEACON_TEST_REQUIRE(configured_abort_handler != SIG_ERR);
  BEACON_TEST_REQUIRE(configured_abort_handler != SIG_DFL);

  using beacon::worker::tests::ProbePrepareDisposition;
  using beacon::worker::tests::ProbePrepareFacts;
  using beacon::worker::tests::classify_probe_prepare;
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = true,
                              .advertised_video_available = true,
                              .advertised_audio_available = true,
                              .exchange_succeeded = true,
                              .has_completion = true,
                              .completion_succeeded = true}) ==
      ProbePrepareDisposition::succeeded);
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = true,
                              .advertised_video_available = false,
                              .advertised_audio_available = true,
                              .has_completion = true,
                              .capability_unavailable = true}) ==
      ProbePrepareDisposition::unsupported_video_hardware);
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = true,
                              .advertised_video_available = true,
                              .advertised_audio_available = false,
                              .has_completion = true,
                              .capability_unavailable = true}) ==
      ProbePrepareDisposition::worker_rejected);
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = true,
                              .advertised_video_available = false,
                              .advertised_audio_available = false,
                              .has_completion = true,
                              .capability_unavailable = true}) ==
      ProbePrepareDisposition::worker_rejected);
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = false,
                              .advertised_video_available = false,
                              .advertised_audio_available = true,
                              .has_completion = true,
                              .capability_unavailable = true}) ==
      ProbePrepareDisposition::worker_rejected);
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = true,
                              .advertised_video_available = false,
                              .advertised_audio_available = true,
                              .has_completion = true}) ==
      ProbePrepareDisposition::worker_rejected);
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = true,
                              .advertised_video_available = false,
                              .advertised_audio_available = true}) ==
      ProbePrepareDisposition::exchange_failed);
  BEACON_TEST_REQUIRE(
      classify_probe_prepare({.video_mode = true,
                              .advertised_video_available = false,
                              .advertised_audio_available = true,
                              .has_completion = true,
                              .completion_succeeded = true}) ==
      ProbePrepareDisposition::exchange_failed);
  return 0;
}
