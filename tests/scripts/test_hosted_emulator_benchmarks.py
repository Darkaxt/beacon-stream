import json
import os
from pathlib import Path
import stat
import subprocess
import tempfile
import textwrap
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
RUNNER = REPOSITORY_ROOT / "scripts" / "test-hosted-emulator-benchmarks.sh"
TEST_CLASS = "dev.beacon.android.BeaconStreamCoreInstrumentationTest"


class HostedEmulatorBenchmarkRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="beacon-hosted-benchmark-script-")
        self.root = Path(self.temporary.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        self.call_log = self.root / "calls.log"
        self.credential_capture = self.root / "credential.txt"
        self.snapshot_counter = self.root / "snapshot-counter.txt"
        self.server_stopped = self.root / "server-stopped.txt"
        self.server_child_pid = self.root / "server-child.pid"
        self.worker = self._touch("BeaconHostedBenchmarkWorker")
        self.test_host = self._touch("Beacon.Server.TestHost.dll")
        self.app_apk = self._touch("app-debug.apk")
        self.test_apk = self._touch("app-debug-androidTest.apk")
        self.native_build = self._script("native-build", """
            printf 'native-build\n' >> "${BEACON_FAKE_CALL_LOG}"
        """)
        self.gradle = self._script("gradle", """
            printf 'gradle %s\n' "$*" >> "${BEACON_FAKE_CALL_LOG}"
        """)
        self.dotnet = self._script("dotnet", """
            if [[ "${1:-}" == "build" ]]; then
              printf 'dotnet %s\n' "$*" >> "${BEACON_FAKE_CALL_LOG}"
              exit 0
            fi

            sleep 100000 &
            child_pid=$!
            printf '%s\n' "${child_pid}" > "${BEACON_FAKE_SERVER_CHILD_PID}"
            cleanup_server() {
              kill "${child_pid}" 2>/dev/null || true
              wait "${child_pid}" 2>/dev/null || true
              printf 'stopped\n' > "${BEACON_FAKE_SERVER_STOPPED}"
              printf 'BEACON_HOSTED_WORKER_STOPPED\n'
              exit "${BEACON_FAKE_SERVER_EXIT:-0}"
            }
            trap cleanup_server TERM INT
            printf 'unstructured startup noise\n'
            printf '%s\n' '{"EventId":14,"LogLevel":"Information","State":{"address":"https://127.0.0.1:43127"}}'
            printf 'BEACON_HOSTED_WORKER_READY\n'
            wait "${child_pid}"
        """)
        self.adb = self._script("adb", """
            printf 'adb %s\n' "$*" >> "${BEACON_FAKE_CALL_LOG}"
            arguments="$*"
            if [[ "${arguments}" == *"get-state"* ]]; then
              printf 'device\n'
            elif [[ "${arguments}" == *"exec-in"* ]]; then
              cat > "${BEACON_FAKE_CREDENTIAL_CAPTURE}"
            elif [[ "${arguments}" == *"am instrument"* ]]; then
              if [[ "${arguments}" == *"#gate4NetworkAndHardwareBenchmark"* ]]; then
                printf 'BEACON_GATE4_NATIVE_NETWORK_COMPLETE\n'
                printf 'BEACON_GATE4_REAL_HARDWARE_CAPABILITY_REJECTED\n'
                printf 'BEACON_GATE4_REAL_HARDWARE_OBSERVED\n'
              elif [[ "${arguments}" == *"#gate4CertifiedBenchmarkEvidence"* ]]; then
                if [[ "${BEACON_FAKE_FAIL_MARKER:-}" != "manual" ]]; then
                  printf 'BEACON_GATE4_CERTIFIED_MANUAL_COMPLETE\n'
                fi
              elif [[ "${arguments}" == *"#gate4CertifiedSessionPreflight"* ]]; then
                printf 'BEACON_GATE4_CERTIFIED_PREFLIGHT_COMPLETE\n'
              fi
              printf 'OK (1 test)\n'
            fi
        """)
        self.curl = self._script("curl", """
            url="${!#}"
            if [[ "${url}" == */identity ]]; then
              printf '{"publicKeyFingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}\n'
              exit 0
            fi
            if [[ "${url}" == */hosted-benchmark-worker/snapshot ]]; then
              printf '{"isReady":true,"processGeneration":1,"diagnostics":["BEACON_HOSTED_WORKER_READY"]}\n'
              exit 0
            fi
            if [[ "${url}" == */admin/snapshot ]]; then
              count=0
              if [[ -f "${BEACON_FAKE_SNAPSHOT_COUNTER}" ]]; then
                count="$(cat "${BEACON_FAKE_SNAPSHOT_COUNTER}")"
              fi
              count=$((count + 1))
              printf '%s\n' "${count}" > "${BEACON_FAKE_SNAPSHOT_COUNTER}"
              if [[ "${count}" -eq 1 ]]; then
                benchmarks='[]'
              elif [[ "${count}" -eq 2 ]]; then
                benchmarks='[{"runId":"00000000-0000-0000-0000-000000000001","trigger":"manual","completedAt":"2026-07-14T20:00:00Z","selectedResult":{"codec":"h264"}}]'
              else
                benchmarks='[{"runId":"00000000-0000-0000-0000-000000000002","trigger":"sessionPreflight","completedAt":"2026-07-14T20:01:00Z","selectedResult":{"codec":"h264"}},{"runId":"00000000-0000-0000-0000-000000000001","trigger":"manual","completedAt":"2026-07-14T20:00:00Z","selectedResult":{"codec":"h264"}}]'
              fi
              printf '{"clients":[{"clientId":"z-fold-7","benchmarks":%s}],"diagnostics":[{"operation":"worker.transport_authenticated"},{"operation":"worker.transport_disconnected"}]}\n' "${benchmarks}"
              exit 0
            fi
            printf 'unexpected curl URL: %s\n' "${url}" >&2
            exit 22
        """)

    def tearDown(self):
        self.temporary.cleanup()

    def test_runner_dispatches_three_transactions_and_cleans_owned_processes(self):
        result = self._run()

        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        combined = result.stdout + result.stderr
        self.assertIn("BEACON_HOSTED_EMULATOR_BENCHMARKS_OK", combined)
        self.assertIn("BEACON_HOSTED_WORKER_READY", combined)
        self.assertIn("BEACON_HOSTED_WORKER_STOPPED", combined)
        self.assertIn("BEACON_HOSTED_BENCHMARK_SNAPSHOT_OK real-hardware", combined)
        self.assertIn("BEACON_HOSTED_BENCHMARK_SNAPSHOT_OK certified-manual", combined)
        self.assertIn("BEACON_HOSTED_BENCHMARK_SNAPSHOT_OK certified-preflight", combined)
        self.assertTrue(self.server_stopped.exists())

        calls = self.call_log.read_text(encoding="utf-8")
        expected_methods = [
            "gate4NetworkAndHardwareBenchmark",
            "gate4CertifiedBenchmarkEvidence",
            "gate4CertifiedSessionPreflight",
        ]
        positions = []
        for method in expected_methods:
            selector = f"{TEST_CLASS}#{method}"
            self.assertEqual(1, calls.count(selector), calls)
            positions.append(calls.index(selector))
        self.assertEqual(sorted(positions), positions)
        self.assertEqual(3, calls.count("-e serverUrl https://10.0.2.2:43127"))
        self.assertEqual(3, calls.count("-e clientId z-fold-7"))
        self.assertEqual(3, calls.count("-e serverPublicKeyFingerprint " + "A" * 64))

        credential = self.credential_capture.read_text(encoding="utf-8").strip()
        self.assertGreater(len(credential), 32)
        self.assertNotIn(credential, calls)
        self.assertNotIn(credential, combined)

        child_pid = int(self.server_child_pid.read_text(encoding="utf-8"))
        self._assert_process_exited(child_pid)

    def test_runner_propagates_missing_acceptance_marker_and_still_cleans_server(self):
        result = self._run({"BEACON_FAKE_FAIL_MARKER": "manual"})

        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("BEACON_HOSTED_EMULATOR_BENCHMARKS_OK", result.stdout + result.stderr)
        self.assertTrue(self.server_stopped.exists())

    def test_gate4_instrumentation_uses_pinned_https_and_typed_markers(self):
        source = (REPOSITORY_ROOT / "src" / "Beacon.Android" / "app" / "src" /
                  "androidTest" / "java" / "dev" / "beacon" / "android" /
                  "BeaconStreamCoreInstrumentationTest.java").read_text(encoding="utf-8")

        self.assertIn('requireArgument(arguments, "serverPublicKeyFingerprint")', source)
        self.assertIn(
            "new BeaconClientConfig(serverUrl, clientId, serverPublicKeyFingerprint)",
            source,
        )
        for marker in (
                "BEACON_GATE4_NATIVE_NETWORK_COMPLETE",
                "BEACON_GATE4_REAL_HARDWARE_CAPABILITY_REJECTED",
                "BEACON_GATE4_REAL_HARDWARE_ACCEPTED",
                "BEACON_GATE4_CERTIFIED_MANUAL_COMPLETE",
                "BEACON_GATE4_CERTIFIED_PREFLIGHT_COMPLETE"):
            self.assertIn(marker, source)
        test_host_program = (REPOSITORY_ROOT / "tests" / "Beacon.Server.TestHost" /
                             "Program.cs").read_text(encoding="utf-8")
        self.assertIn('/hosted-benchmark-worker/snapshot', test_host_program)

    def test_runner_propagates_test_host_shutdown_failure(self):
        result = self._run({"BEACON_FAKE_SERVER_EXIT": "17"})

        self.assertNotEqual(0, result.returncode)
        self.assertNotIn("BEACON_HOSTED_EMULATOR_BENCHMARKS_OK", result.stdout + result.stderr)
        self.assertTrue(self.server_stopped.exists())

    def _run(self, additions=None):
        environment = os.environ.copy()
        paths = {
            "ANDROID_SERIAL": "emulator-test",
            "BEACON_ADB": self.adb,
            "BEACON_CURL": self.curl,
            "BEACON_DOTNET": self.dotnet,
            "BEACON_GRADLE": self.gradle,
            "BEACON_NATIVE_BUILD_SCRIPT": self.native_build,
            "BEACON_HOSTED_WORKER": self.worker,
            "BEACON_TEST_HOST_ASSEMBLY": self.test_host,
            "BEACON_APP_APK": self.app_apk,
            "BEACON_TEST_APK": self.test_apk,
            "BEACON_FAKE_CALL_LOG": self.call_log,
            "BEACON_FAKE_CREDENTIAL_CAPTURE": self.credential_capture,
            "BEACON_FAKE_SNAPSHOT_COUNTER": self.snapshot_counter,
            "BEACON_FAKE_SERVER_STOPPED": self.server_stopped,
            "BEACON_FAKE_SERVER_CHILD_PID": self.server_child_pid,
            "HOME": self.root / "home",
        }
        environment.update({
            key: value if isinstance(value, str) else self._shell_path(value)
            for key, value in paths.items()
        })
        if additions:
            environment.update(additions)
        command = ["bash", self._shell_path(RUNNER)]
        process_environment = environment
        if os.name == "nt":
            exported = {
                key: environment[key]
                for key in paths
            }
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
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )

    def _touch(self, name):
        path = self.root / name
        path.write_bytes(b"fixture")
        return path

    def _script(self, name, body):
        path = self.bin / name
        with path.open("w", encoding="utf-8", newline="\n") as stream:
            stream.write(
                "#!/usr/bin/env bash\nset -euo pipefail\n" + textwrap.dedent(body).lstrip())
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

    def _assert_process_exited(self, process_id):
        if os.name == "nt":
            result = subprocess.run(
                ["wsl.exe", "bash", "-lc", f"kill -0 {process_id}"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
            )
            self.assertNotEqual(0, result.returncode)
            return
        with self.assertRaises(ProcessLookupError):
            os.kill(process_id, 0)


if __name__ == "__main__":
    unittest.main()
