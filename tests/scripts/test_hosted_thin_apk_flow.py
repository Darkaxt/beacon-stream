import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import textwrap
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
RUNNER = REPOSITORY_ROOT / "scripts" / "test-hosted-thin-apk-flow.sh"


class HostedThinApkFlowScriptTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="beacon-hosted-thin-apk-")
        self.root = Path(self.temporary.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.host_pid = self.root / "host.pid"
        self.terminated_pid = self.root / "terminated.pid"
        self.calls = self.root / "calls.txt"
        self.worker = self._touch("BeaconHostedBenchmarkWorker")
        self.test_host = self._touch("Beacon.Server.TestHost.dll")
        self.app_apk = self._touch("app-debug.apk")
        self.test_apk = self._touch("app-debug-androidTest.apk")
        self.native_build = self._script("native-build", "#!/usr/bin/env bash\nexit 0\n")
        self._write_commands()

    def tearDown(self):
        self.temporary.cleanup()

    def test_success_requires_junit_markers_snapshots_and_stops_only_owned_host(self):
        unrelated = subprocess.Popen(
            ["wsl.exe", "python3", "-c", "import signal; signal.pause()"]
            if os.name == "nt"
            else [sys.executable, "-c", "import signal; signal.pause()"])
        try:
            result = self._run()
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            combined = result.stdout + result.stderr
            self.assertIn("OK (1 test)", combined)
            self.assertIn("BEACON_HOSTED_THIN_FINAL_SNAPSHOT_OK hosted-fake-no-topology", combined)
            self.assertIn("BEACON_HOSTED_THIN_APK_FLOW_OK", combined)
            self.assertLess(
                combined.index("BEACON_HOSTED_WORKER_STOPPED"),
                combined.index("BEACON_HOSTED_THIN_APK_FLOW_OK"))
            self.assertEqual(self.host_pid.read_text(), self.terminated_pid.read_text())
            self.assertIsNone(unrelated.poll(), "Runner terminated an unrelated process.")
            calls = self.calls.read_text()
            self.assertEqual(1, calls.count("shell am instrument"))
            self.assertIn("/admin/snapshot", calls)
            self.assertIn("/hosted-thin-apk-flow/snapshot", calls)
            self._assert_app_data_cleared_after_instrumentation()
        finally:
            unrelated.terminate()
            unrelated.wait()

    def test_zero_exit_without_junit_ok_fails_closed(self):
        result = self._run({"BEACON_FIXTURE_NO_JUNIT": "1"})
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("BEACON_HOSTED_THIN_APK_FLOW_OK", result.stdout + result.stderr)
        self.assertEqual(self.host_pid.read_text(), self.terminated_pid.read_text())

    def test_missing_instrumentation_marker_prevents_final_ok(self):
        result = self._run({"BEACON_FIXTURE_MISSING_MARKER": "1"})
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("BEACON_HOSTED_THIN_APK_FLOW_OK", result.stdout + result.stderr)
        self._assert_app_data_cleared_after_instrumentation()

    def test_active_worker_runtime_prevents_final_ok(self):
        result = self._run({"BEACON_FIXTURE_ACTIVE_RUNTIME": "1"})
        self.assertNotEqual(0, result.returncode)
        self.assertIn("retained activeStreams", result.stdout + result.stderr)
        self.assertNotIn("BEACON_HOSTED_THIN_APK_FLOW_OK", result.stdout + result.stderr)

    def test_partial_credential_stage_failure_clears_app_data(self):
        result = self._run({"BEACON_FIXTURE_CREDENTIAL_STAGE_FAIL": "1"})
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("BEACON_HOSTED_THIN_APK_FLOW_OK", result.stdout + result.stderr)
        self._assert_app_data_cleared_after("partial-credential-write:4")

    def _run(self, additions=None):
        environment = os.environ.copy()
        paths = {
            "BEACON_ADB": str(self.bin / "adb"),
            "BEACON_CURL": str(self.bin / "curl"),
            "BEACON_DOTNET": str(self.bin / "dotnet"),
            "BEACON_GRADLE": str(self.bin / "gradle"),
            "BEACON_PYTHON": "python3",
            "BEACON_NATIVE_BUILD_SCRIPT": str(self.native_build),
            "BEACON_HOSTED_WORKER": str(self.worker),
            "BEACON_TEST_HOST_ASSEMBLY": str(self.test_host),
            "BEACON_APP_APK": str(self.app_apk),
            "BEACON_TEST_APK": str(self.test_apk),
            "BEACON_FIXTURE_CALLS": str(self.calls),
            "BEACON_FIXTURE_HOST_PID": str(self.host_pid),
            "BEACON_FIXTURE_TERMINATED_PID": str(self.terminated_pid),
        }
        environment.update({
            key: self._shell_path(value) if key != "BEACON_PYTHON" else value
            for key, value in paths.items()
        })
        if additions:
            environment.update(additions)
        command = ["bash", self._shell_path(RUNNER)]
        process_environment = environment
        if os.name == "nt":
            exported = {key: environment[key] for key in paths}
            if additions:
                exported.update(additions)
            command = [
                "wsl.exe",
                "env",
                *(f"{key}={value}" for key, value in exported.items()),
                "bash",
                self._shell_path(RUNNER),
            ]
            process_environment = os.environ.copy()
        return subprocess.run(
            command,
            cwd=REPOSITORY_ROOT,
            env=process_environment,
            text=True,
            capture_output=True,
            check=False,
        )

    def _assert_app_data_cleared_after_instrumentation(self):
        self._assert_app_data_cleared_after("shell am instrument")

    def _assert_app_data_cleared_after(self, operation):
        calls = self.calls.read_text().splitlines()
        operations = [
            index for index, call in enumerate(calls)
            if operation in call
        ]
        clears = [
            index for index, call in enumerate(calls)
            if "shell pm clear dev.beacon.android" in call
        ]
        self.assertEqual(1, len(operations), calls)
        self.assertEqual(2, len(clears), calls)
        self.assertLess(clears[0], operations[0])
        self.assertGreater(clears[1], operations[0])

    def _write_commands(self):
        self._script("gradle", "#!/usr/bin/env bash\nexit 0\n")
        self._script(
            "dotnet",
            textwrap.dedent(r"""
                #!/usr/bin/env bash
                set -euo pipefail
                if [[ "${1:-}" == build ]]; then
                  exit 0
                fi
                printf '%s' "$$" > "${BEACON_FIXTURE_HOST_PID}"
                python3 -c 'import signal; signal.pause()' &
                child_pid=$!
                stop_host() {
                  kill "${child_pid}" 2>/dev/null || true
                  wait "${child_pid}" 2>/dev/null || true
                  printf '%s' "$$" > "${BEACON_FIXTURE_TERMINATED_PID}"
                  echo BEACON_HOSTED_WORKER_STOPPED
                  exit 0
                }
                trap stop_host TERM INT
                echo '{"address":"https://127.0.0.1:43127"}'
                echo BEACON_HOSTED_WORKER_READY
                wait "${child_pid}"
                """),
        )
        self._script(
            "adb",
            textwrap.dedent(r"""
                #!/usr/bin/env bash
                set -euo pipefail
                printf '%s\n' "$*" >> "${BEACON_FIXTURE_CALLS}"
                arguments="$*"
                if [[ "${arguments}" == *"get-state"* ]]; then
                  echo device
                elif [[ "${arguments}" == *"shell am instrument"* ]]; then
                  echo 'INSTRUMENTATION_STATUS: class=dev.beacon.android.HostedThinApkFlowInstrumentationTest'
                  if [[ -z "${BEACON_FIXTURE_NO_JUNIT:-}" ]]; then
                    echo 'OK (1 test)'
                  fi
                elif [[ "${arguments}" == *"logcat -d"* ]]; then
                  markers=(
                    BEACON_HOSTED_THIN_ENROLLMENT_SEEDED
                    BEACON_HOSTED_THIN_AUTOMATIC_PRESENCE_OK
                    BEACON_HOSTED_THIN_AUTOMATIC_BENCHMARK_OK
                    'BEACON_HOSTED_THIN_CATALOG_OK steam-shortcut:3767414131'
                    'BEACON_HOSTED_THIN_LAUNCH_RENDER_OK 12'
                    BEACON_HOSTED_THIN_DISCONNECT_RETAINED_OK
                    'BEACON_HOSTED_THIN_RECONNECT_RENDER_OK 12'
                    BEACON_HOSTED_THIN_QUIT_OK
                    BEACON_HOSTED_THIN_EMERGENCY_RESTORE_OK
                    BEACON_HOSTED_THIN_ACTIVITY_CLEANUP_OK
                  )
                  for marker in "${markers[@]}"; do
                    if [[ "${BEACON_FIXTURE_MISSING_MARKER:-}" == 1 &&
                          "${marker}" == BEACON_HOSTED_THIN_QUIT_OK ]]; then
                      continue
                    fi
                    echo "${marker}"
                  done
                elif [[ "${arguments}" == *"exec-in"* ]]; then
                  if [[ "${BEACON_FIXTURE_CREDENTIAL_STAGE_FAIL:-}" == 1 ]]; then
                    IFS= read -r -n 4 partial_credential
                    printf 'partial-credential-write:%s\n' "${#partial_credential}" >> "${BEACON_FIXTURE_CALLS}"
                    exit 17
                  fi
                  cat >/dev/null
                fi
                """),
        )
        self._script(
            "curl",
            textwrap.dedent(r"""
                #!/usr/bin/env bash
                set -euo pipefail
                url="${!#}"
                printf '%s\n' "${url}" >> "${BEACON_FIXTURE_CALLS}"
                if [[ "${url}" == */identity ]]; then
                  printf '{"publicKeyFingerprint":"%064d"}\n' 0 | tr '0' 'A'
                elif [[ "${url}" == */admin/snapshot ]]; then
                  printf '%s\n' '{"clients":[{"clientId":"z-fold-7","benchmarks":[{"trigger":"Automatic","completedAt":"now","selectedResult":{"codec":"h264"}},{"trigger":"SessionPreflight","completedAt":"now","selectedResult":{"codec":"h264"}}]}],"streams":[{"state":"stopped","activeListenerPort":null}],"ownership":[],"security":{"pendingRegistrations":[]},"diagnostics":[{"operation":"lease.prepare"},{"operation":"lease.cleanup.removed"},{"operation":"lease.recover"},{"operation":"worker.transport_authenticated"},{"operation":"worker.transport_disconnected"}]}'
                elif [[ "${url}" == */hosted-thin-apk-flow/snapshot ]]; then
                  active=0
                  [[ -n "${BEACON_FIXTURE_ACTIVE_RUNTIME:-}" ]] && active=1
                  printf '%s\n' "{\"boundary\":\"hosted-fake-no-topology\",\"host\":{\"mode\":\"fake-hosted-worker\",\"displayBackend\":\"FakeDisplayBackend\",\"gameLauncher\":\"FakeGameLauncher\",\"streamingBackend\":\"StreamWorkerStreamingBackend\"},\"launches\":[{\"appId\":\"steam-shortcut:3767414131\"}],\"runtime\":{\"activeStreams\":${active},\"activeBenchmarks\":0,\"boundRuntimes\":0},\"worker\":{\"isReady\":true,\"processHasExited\":false,\"clientTerminalError\":null,\"diagnostics\":[\"BEACON_HOSTED_WORKER_READY\"]},\"requests\":[{\"sequence\":1,\"method\":\"POST\",\"path\":\"/clients/hello\",\"statusCode\":200},{\"sequence\":2,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/capabilities\",\"statusCode\":200},{\"sequence\":3,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/beacon\",\"statusCode\":200},{\"sequence\":4,\"method\":\"POST\",\"path\":\"/clients/hello\",\"statusCode\":200},{\"sequence\":5,\"method\":\"GET\",\"path\":\"/games\",\"statusCode\":200},{\"sequence\":6,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/launch\",\"statusCode\":200},{\"sequence\":7,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/disconnect\",\"statusCode\":200},{\"sequence\":8,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/reconnect\",\"statusCode\":200},{\"sequence\":9,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/beacon\",\"statusCode\":200},{\"sequence\":10,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/quit\",\"statusCode\":200},{\"sequence\":11,\"method\":\"POST\",\"path\":\"/clients/z-fold-7/emergency-restore\",\"statusCode\":200}],\"display\":{\"prepareCalls\":[\"client-z-fold-7\"],\"restoreCalls\":[\"physical-primary\"],\"removeCalls\":[\"client-z-fold-7\"]}}"
                else
                  echo "Unexpected fixture URL: ${url}" >&2
                  exit 1
                fi
                """),
        )

    def _touch(self, name):
        path = self.root / name
        path.write_bytes(b"fixture")
        return path

    def _script(self, name, content):
        path = self.bin / name
        path.write_text(textwrap.dedent(content).lstrip(), encoding="utf-8", newline="\n")
        path.chmod(path.stat().st_mode | stat.S_IXUSR)
        return path

    @staticmethod
    def _shell_path(path):
        resolved = Path(path).resolve()
        if os.name != "nt":
            return str(resolved)
        drive = resolved.drive[0].lower()
        suffix = resolved.as_posix().split(":", 1)[1].lstrip("/")
        return f"/mnt/{drive}/{suffix}"


if __name__ == "__main__":
    unittest.main()
