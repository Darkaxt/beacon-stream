import os
from pathlib import Path
import shutil
import stat
import subprocess
import tempfile
import textwrap
import unittest


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
RUNNER = REPOSITORY_ROOT / "scripts" / "test-hosted-emulator-stream.sh"


class HostedEmulatorStreamRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="beacon-hosted-stream-script-")
        self.root = Path(self.temporary.name)
        self.repository = self.root / "repository"
        self.bin = self.root / "bin"
        self.build_root = self.root / "android-build"
        self.instrumentation_called = self.root / "instrumentation-called.txt"
        self.endpoint_output_wait_entered = self.root / "endpoint-output-wait-entered.txt"
        self.bin.mkdir()

        runner = self.repository / "scripts" / RUNNER.name
        runner.parent.mkdir(parents=True)
        shutil.copy2(RUNNER, runner)
        self.runner = runner

        self._touch(
            self.repository / "src" / "Beacon.Android" / "app" / "src" / "main" /
            "assets" / "benchmark-vectors" / "beacon-h264-high-8-640x360-30-v1.bau"
        )
        self._touch(
            self.repository / "src" / "Beacon.Android" / "app" / "build" / "outputs" /
            "apk" / "debug" / "app-debug.apk"
        )
        self._touch(
            self.repository / "src" / "Beacon.Android" / "app" / "build" / "outputs" /
            "apk" / "androidTest" / "debug" / "app-debug-androidTest.apk"
        )
        self._touch(
            self.build_root / "Beacon.StreamProtocol.Tests" / "BeaconHostedEmulatorEndpoint"
        )
        self._touch(self.build_root / "msquic" / "bin" / "Debug" / "libmsquic.so")

        self.adb = self._script("adb", """
            arguments="$*"
            if [[ "${arguments}" == *"get-state"* ]]; then
              printf 'device\n'
            elif [[ "${arguments}" == *"BeaconHostedEmulatorEndpoint --certificate"* ]]; then
              printf 'BEACON_HOSTED_ENDPOINT_READY 43127\n'
              while [[ ! -f "${BEACON_FAKE_INSTRUMENTATION_CALLED}" ]]; do :; done
            elif [[ "${arguments}" == *"shell am instrument"* ]]; then
              printf 'called\n' > "${BEACON_FAKE_INSTRUMENTATION_CALLED}"
              printf 'java.lang.IllegalArgumentException: selectedAudio is required for a stream grant.\n'
              printf 'FAILURES!!!\n'
              exit 0
            fi
        """)
        self._script("openssl", """
            operation="${1:-}"
            if [[ "${operation}" == "req" ]]; then
              while [[ "$#" -gt 0 ]]; do
                if [[ "$1" == "-keyout" || "$1" == "-out" ]]; then
                  printf 'fixture\n' > "$2"
                  shift 2
                else
                  shift
                fi
              done
            elif [[ "${operation}" == "x509" ]]; then
              printf 'public-key\n'
            elif [[ "${operation}" == "pkey" ]]; then
              while IFS= read -r _; do :; done
              printf 'public-key-der\n'
            elif [[ "${operation}" == "dgst" ]]; then
              while IFS= read -r _; do :; done
              printf 'SHA2-256(stdin)= %064d\n' 0 | tr '0' 'A'
            fi
        """)
        self._script("cat", """
            printf 'entered\n' > "${BEACON_FAKE_ENDPOINT_OUTPUT_WAIT_ENTERED}"
            /bin/cat "$@"
        """)

    def tearDown(self):
        self.temporary.cleanup()

    def test_zero_exit_junit_failure_is_rejected_before_endpoint_output_wait(self):
        result = self._run()

        self.assertNotEqual(0, result.returncode)
        self.assertIn(
            "selectedAudio is required for a stream grant.",
            result.stdout + result.stderr,
        )
        self.assertFalse(
            self.endpoint_output_wait_entered.exists(),
            "runner entered endpoint-output wait after failed JUnit output",
        )

    def _run(self):
        environment = os.environ.copy()
        shell_path = ":".join((
            self._shell_path(self.bin),
            "/usr/local/sbin",
            "/usr/local/bin",
            "/usr/sbin",
            "/usr/bin",
            "/sbin",
            "/bin",
        ))
        values = {
            "ANDROID_SERIAL": "emulator-test",
            "BEACON_ADB": self.adb,
            "BEACON_ANDROID_BUILD_ROOT": self.build_root,
            "BEACON_FAKE_INSTRUMENTATION_CALLED": self.instrumentation_called,
            "BEACON_FAKE_ENDPOINT_OUTPUT_WAIT_ENTERED": self.endpoint_output_wait_entered,
            "HOME": self.root / "home",
            "PATH": shell_path,
        }
        environment.update({
            key: value if isinstance(value, str) else self._shell_path(value)
            for key, value in values.items()
        })
        command = ["bash", self._shell_path(self.runner)]
        process_environment = environment
        if os.name == "nt":
            command = [
                "wsl.exe",
                "env",
                *(f"{key}={environment[key]}" for key in values),
                "bash",
                self._shell_path(self.runner),
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

    @staticmethod
    def _touch(path):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"fixture")
        return path

    def _script(self, name, body):
        path = self.bin / name
        path.write_text(
            "#!/usr/bin/env bash\nset -euo pipefail\n" + textwrap.dedent(body).lstrip(),
            encoding="utf-8",
            newline="\n",
        )
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
