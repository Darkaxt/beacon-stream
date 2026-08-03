#!/usr/bin/env bash
set -euo pipefail

readonly script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_directory}/.." && pwd)"
readonly serial="${ANDROID_SERIAL:-emulator-5554}"
readonly adb_command="${BEACON_ADB:-adb}"
readonly curl_command="${BEACON_CURL:-curl}"
readonly dotnet_command="${BEACON_DOTNET:-dotnet}"
readonly gradle_command="${BEACON_GRADLE:-gradle}"
readonly python_command="${BEACON_PYTHON:-python3}"
readonly native_build_script="${BEACON_NATIVE_BUILD_SCRIPT:-${script_directory}/build-native-linux.sh}"
readonly linux_build_root="${BEACON_LINUX_BUILD_ROOT:-${HOME}/.cache/beacon/build/linux-x64-debug}"
readonly hosted_worker="${BEACON_HOSTED_WORKER:-${linux_build_root}/Beacon.StreamProtocol.Tests/BeaconHostedBenchmarkWorker}"
readonly test_host_assembly="${BEACON_TEST_HOST_ASSEMBLY:-${repository_root}/tests/Beacon.Server.TestHost/bin/Release/net10.0-windows/Beacon.Server.TestHost.dll}"
readonly app_apk="${BEACON_APP_APK:-${repository_root}/src/Beacon.Android/app/build/outputs/apk/debug/app-debug.apk}"
readonly test_apk="${BEACON_TEST_APK:-${repository_root}/src/Beacon.Android/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk}"
readonly video_720p="${repository_root}/src/Beacon.Android/app/src/main/assets/benchmark-vectors/beacon-h264-high-8-1280x720-60-v1.bau"
readonly video_360p="${repository_root}/src/Beacon.Android/app/src/main/assets/benchmark-vectors/beacon-h264-high-8-640x360-30-v1.bau"
readonly client_id="z-fold-7"
readonly package_name="dev.beacon.android"
readonly test_runner="dev.beacon.android.test/androidx.test.runner.AndroidJUnitRunner"
readonly test_class="dev.beacon.android.HostedThinApkFlowInstrumentationTest#coldLaunchDrivesEntireProductionActivityTransaction"

require_command() {
  local command_name="$1"
  if [[ "${command_name}" == */* ]]; then
    [[ -x "${command_name}" ]] || { echo "Required command '${command_name}' is unavailable." >&2; exit 1; }
  elif ! command -v "${command_name}" >/dev/null 2>&1; then
    echo "Required command '${command_name}' is unavailable." >&2
    exit 1
  fi
}

require_file() {
  local description="$1"
  local path="$2"
  [[ -f "${path}" ]] || { echo "${description} is unavailable at '${path}'." >&2; exit 1; }
}

for command_name in "${adb_command}" "${curl_command}" "${dotnet_command}" \
    "${gradle_command}" "${python_command}" bash; do
  require_command "${command_name}"
done

bash "${native_build_script}"
"${dotnet_command}" build \
  "${repository_root}/tests/Beacon.Server.TestHost/Beacon.Server.TestHost.csproj" \
  --configuration Release --nologo
"${gradle_command}" -p "${repository_root}/src/Beacon.Android" \
  assembleDebug assembleDebugAndroidTest

require_file "Hosted Worker" "${hosted_worker}"
require_file "Beacon TestHost assembly" "${test_host_assembly}"
require_file "Beacon debug APK" "${app_apk}"
require_file "Beacon instrumentation APK" "${test_apk}"
require_file "Hosted 720p H.264 vector" "${video_720p}"
require_file "Hosted 360p H.264 vector" "${video_360p}"

if ! "${adb_command}" -s "${serial}" get-state >/dev/null 2>&1; then
  echo "Android emulator '${serial}' is unavailable." >&2
  exit 1
fi

temporary_directory="$(mktemp -d)"
identity_path="${temporary_directory}/server-identity.pfx"
credentials_path="${temporary_directory}/client-credentials.json"
profiles_path="${temporary_directory}/client-profiles.json"
benchmarks_path="${temporary_directory}/benchmark-evidence.json"
credential_evidence="${temporary_directory}/client-credential"
host_output="${temporary_directory}/test-host.log"
instrumentation_output="${temporary_directory}/instrumentation.txt"
test_host_pid=""
test_host_output_fd=""
host_drain_pid=""
test_host_exit_status=""
app_prepared=false
flow_validated=false

stop_test_host() {
  if [[ -n "${test_host_pid}" ]]; then
    if kill -0 "${test_host_pid}" 2>/dev/null; then
      kill -TERM "${test_host_pid}" 2>/dev/null || true
    fi
    if wait "${test_host_pid}"; then
      test_host_exit_status=0
    else
      test_host_exit_status=$?
    fi
  fi
  test_host_pid=""
  if [[ -n "${test_host_output_fd}" ]]; then
    exec {test_host_output_fd}<&- || true
    test_host_output_fd=""
  fi
  if [[ -n "${host_drain_pid}" ]]; then
    wait "${host_drain_pid}" 2>/dev/null || true
    host_drain_pid=""
  fi
}

cleanup() {
  local status=$?
  trap - EXIT INT TERM
  stop_test_host
  if [[ "${app_prepared}" == true ]]; then
    if ! "${adb_command}" -s "${serial}" shell pm clear "${package_name}" >/dev/null 2>&1; then
      echo "Could not clear hosted Beacon app data during cleanup." >&2
      if [[ "${status}" -eq 0 ]]; then status=1; fi
    fi
  fi
  if [[ "${status}" -ne 0 ]]; then
    [[ -f "${instrumentation_output}" ]] && cat "${instrumentation_output}" >&2
    [[ -f "${host_output}" ]] && cat "${host_output}" >&2
  fi
  rm -rf "${temporary_directory}"
  if [[ "${status}" -eq 0 && "${flow_validated}" == true ]]; then
    echo BEACON_HOSTED_THIN_APK_FLOW_OK
  fi
  exit "${status}"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

"${python_command}" - "${client_id}" "${credentials_path}" "${credential_evidence}" <<'PY'
import base64
import hashlib
import json
import os
from pathlib import Path
import sys

client_id, store_path, evidence_path = sys.argv[1:]
credential = os.urandom(32)
salt = os.urandom(32)
try:
    Path(store_path).write_text(json.dumps([{
        "clientId": client_id,
        "salt": base64.b64encode(salt).decode("ascii"),
        "hash": base64.b64encode(hashlib.sha256(salt + credential).digest()).decode("ascii"),
        "revoked": False,
    }]), encoding="utf-8")
    Path(evidence_path).write_text(
        base64.b64encode(credential).decode("ascii"), encoding="utf-8")
finally:
    credential = b""
    salt = b""
PY
chmod 600 "${credentials_path}" "${credential_evidence}"

coproc TEST_HOST_PROCESS {
  exec env \
    ASPNETCORE_URLS="https://127.0.0.1:0" \
    DOTNET_ENVIRONMENT="Production" \
    Logging__Console__FormatterName="json" \
    Beacon__HostedBenchmarkWorker__Path="${hosted_worker}" \
    Beacon__HostedBenchmarkWorker__Video720pPath="${video_720p}" \
    Beacon__HostedBenchmarkWorker__Video360pPath="${video_360p}" \
    Beacon__TestHost__SeedBenchmarkEvidence="false" \
    Beacon__Profiles__Path="${profiles_path}" \
    Beacon__Benchmarks__Path="${benchmarks_path}" \
    Beacon__Security__IdentityPath="${identity_path}" \
    Beacon__Security__CredentialsPath="${credentials_path}" \
    Beacon__Security__TestHost="false" \
    LD_LIBRARY_PATH="${linux_build_root}/msquic/bin/Debug:${LD_LIBRARY_PATH:-}" \
    "${dotnet_command}" "${test_host_assembly}" 2>&1
}
test_host_pid="${TEST_HOST_PROCESS_PID:-}"
test_host_source_fd="${TEST_HOST_PROCESS[0]:-}"
if [[ -z "${test_host_pid}" || -z "${test_host_source_fd}" ]]; then
  echo "Beacon TestHost exited before its output stream was attached." >&2
  exit 1
fi
exec {test_host_output_fd}<&"${test_host_source_fd}"

host_port=""
while IFS= read -r line <&"${test_host_output_fd}"; do
  printf '%s\n' "${line}" >> "${host_output}"
  if candidate_port="$("${python_command}" - "${line}" <<'PY'
import json
import re
import sys

try:
    document = json.loads(sys.argv[1])
except json.JSONDecodeError:
    raise SystemExit(1)

def strings(value):
    if isinstance(value, dict):
        for item in value.values():
            yield from strings(item)
    elif isinstance(value, list):
        for item in value:
            yield from strings(item)
    elif isinstance(value, str):
        yield value

for value in strings(document):
    match = re.fullmatch(r"https://127\.0\.0\.1:([1-9][0-9]*)", value)
    if match and int(match.group(1)) <= 65535:
        print(match.group(1))
        raise SystemExit(0)
raise SystemExit(1)
PY
)"; then
    host_port="${candidate_port}"
    break
  fi
done
[[ -n "${host_port}" ]] || { echo "Beacon TestHost exited before structured Kestrel readiness." >&2; exit 1; }

cat <&"${test_host_output_fd}" >> "${host_output}" &
host_drain_pid=$!
local_server_url="https://127.0.0.1:${host_port}"
emulator_server_url="https://10.0.2.2:${host_port}"
identity_json="$("${curl_command}" --fail --silent --show-error --insecure "${local_server_url}/identity")"
server_fingerprint="$("${python_command}" - "${identity_json}" <<'PY'
import json
import re
import sys

value = json.loads(sys.argv[1]).get("publicKeyFingerprint", "")
if not re.fullmatch(r"[0-9A-F]{64}", value):
    raise SystemExit("Beacon TestHost returned an invalid public-key fingerprint.")
print(value)
PY
)"

"${adb_command}" -s "${serial}" install -r "${app_apk}" >/dev/null
"${adb_command}" -s "${serial}" install -r "${test_apk}" >/dev/null
"${adb_command}" -s "${serial}" shell pm clear "${package_name}" >/dev/null
app_prepared=true
"${adb_command}" -s "${serial}" shell run-as "${package_name}" mkdir -p files >/dev/null
"${adb_command}" -s "${serial}" exec-in run-as "${package_name}" sh -c \
  'cat > files/beacon-gate3-client-credential' < "${credential_evidence}"
"${adb_command}" -s "${serial}" logcat -c

if ! "${adb_command}" -s "${serial}" shell am instrument -w -r \
    -e serverUrl "${emulator_server_url}" \
    -e clientId "${client_id}" \
    -e serverPublicKeyFingerprint "${server_fingerprint}" \
    -e class "${test_class}" \
    "${test_runner}" > "${instrumentation_output}" 2>&1; then
  echo "Hosted thin-APK instrumentation failed." >&2
  exit 1
fi
cat "${instrumentation_output}"
grep -Eq '^OK \(1 test\)\r?$' "${instrumentation_output}" || {
  echo "Hosted thin-APK instrumentation did not report OK (1 test)." >&2
  exit 1
}

marker_output="$("${adb_command}" -s "${serial}" logcat -d -v raw -s BeaconHostedThinApk:I '*:S')"
printf '%s\n' "${marker_output}" | tee -a "${instrumentation_output}"
required_markers=(
  BEACON_HOSTED_THIN_ENROLLMENT_SEEDED
  BEACON_HOSTED_THIN_AUTOMATIC_PRESENCE_OK
  BEACON_HOSTED_THIN_AUTOMATIC_BENCHMARK_OK
  BEACON_HOSTED_THIN_CATALOG_OK
  BEACON_HOSTED_THIN_LAUNCH_RENDER_OK
  BEACON_HOSTED_THIN_DISCONNECT_RETAINED_OK
  BEACON_HOSTED_THIN_RECONNECT_RENDER_OK
  BEACON_HOSTED_THIN_QUIT_OK
  BEACON_HOSTED_THIN_EMERGENCY_RESTORE_OK
  BEACON_HOSTED_THIN_ACTIVITY_CLEANUP_OK
)
for marker in "${required_markers[@]}"; do
  grep -Fq "${marker}" "${instrumentation_output}" || {
    echo "Hosted thin-APK instrumentation omitted '${marker}'." >&2
    exit 1
  }
done

admin_snapshot="${temporary_directory}/admin-final.json"
flow_snapshot="${temporary_directory}/flow-final.json"
"${curl_command}" --fail --silent --show-error --insecure \
  "${local_server_url}/admin/snapshot" > "${admin_snapshot}"
"${curl_command}" --fail --silent --show-error --insecure \
  "${local_server_url}/hosted-thin-apk-flow/snapshot" > "${flow_snapshot}"

"${python_command}" - "${client_id}" "${admin_snapshot}" "${flow_snapshot}" <<'PY'
import json
from pathlib import Path
import sys

client_id, admin_path, flow_path = sys.argv[1:]
admin = json.loads(Path(admin_path).read_text(encoding="utf-8"))
flow = json.loads(Path(flow_path).read_text(encoding="utf-8"))

clients = [item for item in admin.get("clients", []) if item.get("clientId") == client_id]
if len(clients) != 1:
    raise SystemExit("Final snapshot does not contain exactly one hosted client.")
benchmarks = clients[0].get("benchmarks", [])
if not benchmarks or any(item.get("completedAt") is None for item in benchmarks):
    raise SystemExit("Final snapshot retained a pending benchmark.")
triggers = {
    str(item.get("trigger", "")).casefold()
    for item in benchmarks
    if item.get("selectedResult")
}
if not {"automatic", "sessionpreflight"}.issubset(triggers):
    raise SystemExit("Automatic and session-preflight terminal benchmark evidence is incomplete.")
if any(item.get("state") == "running" or item.get("activeListenerPort") is not None
       for item in admin.get("streams", [])):
    raise SystemExit("Final snapshot retained an active stream session.")
if any(item.get("hasOwnedWork") for item in admin.get("ownership", [])):
    raise SystemExit("Final snapshot retained server-owned session work.")
if admin.get("security", {}).get("pendingRegistrations"):
    raise SystemExit("Final snapshot retained a pending client registration.")

expected_host = {
    "mode": "fake-hosted-worker",
    "displayBackend": "FakeDisplayBackend",
    "gameLauncher": "FakeGameLauncher",
    "streamingBackend": "StreamWorkerStreamingBackend",
}
if flow.get("boundary") != "hosted-fake-no-topology" or flow.get("host") != expected_host:
    raise SystemExit("Hosted fake/no-topology boundary evidence is invalid.")
launches = flow.get("launches", [])
if len(launches) != 1 or launches[0].get("appId") != "steam-shortcut:3767414131":
    raise SystemExit("Hosted deterministic application launch evidence is invalid.")
runtime = flow.get("runtime") or {}
for name in ("activeStreams", "activeBenchmarks", "boundRuntimes"):
    if int(runtime.get(name, -1)) != 0:
        raise SystemExit(f"Hosted Worker runtime retained {name}.")
worker = flow.get("worker") or {}
if (not worker.get("isReady") or worker.get("processHasExited") or
        worker.get("clientTerminalError") is not None or
        "BEACON_HOSTED_WORKER_READY" not in worker.get("diagnostics", [])):
    raise SystemExit("Hosted Worker final process evidence is invalid.")

requests = [item for item in flow.get("requests", [])
            if item.get("path", "").startswith("/clients") or item.get("path") == "/games"]
expected_prefix = [
    ("POST", "/clients/hello"),
    ("POST", f"/clients/{client_id}/capabilities"),
    ("POST", f"/clients/{client_id}/beacon"),
]
actual_prefix = [(item.get("method"), item.get("path")) for item in requests[:3]]
if actual_prefix != expected_prefix or any(not 200 <= int(item.get("statusCode", 0)) < 300 for item in requests):
    raise SystemExit("Automatic hello/capability/presence request order is invalid.")
required_paths = {
    "/games",
    f"/clients/{client_id}/launch",
    f"/clients/{client_id}/disconnect",
    f"/clients/{client_id}/reconnect",
    f"/clients/{client_id}/quit",
    f"/clients/{client_id}/emergency-restore",
}
paths = [item.get("path") for item in requests]
if not required_paths.issubset(paths):
    raise SystemExit("Hosted production Activity request transaction is incomplete.")
if paths.count(f"/clients/{client_id}/beacon") != 2:
    raise SystemExit("Hosted presence did not contain exactly one arrival and one departure.")
for path in required_paths:
    if paths.count(path) != 1:
        raise SystemExit(f"Hosted action '{path}' did not execute exactly once.")
ordered_actions = [
    "/games",
    f"/clients/{client_id}/launch",
    f"/clients/{client_id}/disconnect",
    f"/clients/{client_id}/reconnect",
    f"/clients/{client_id}/quit",
    f"/clients/{client_id}/emergency-restore",
]
action_positions = [paths.index(path) for path in ordered_actions]
if action_positions != sorted(action_positions):
    raise SystemExit("Hosted Activity actions did not execute in transaction order.")
presence_positions = [
    index for index, path in enumerate(paths)
    if path == f"/clients/{client_id}/beacon"
]
if not action_positions[3] < presence_positions[1] < action_positions[4]:
    raise SystemExit("Presence departure did not occur between reconnect and explicit quit.")

display = flow.get("display") or {}
if not display.get("prepareCalls") or not display.get("restoreCalls") or not display.get("removeCalls"):
    raise SystemExit("Hosted fake display lifecycle evidence is incomplete.")
operations = {item.get("operation") for item in admin.get("diagnostics", [])}
required_operations = {
    "lease.prepare",
    "lease.cleanup.removed",
    "lease.recover",
    "worker.transport_authenticated",
    "worker.transport_disconnected",
}
if not required_operations.issubset(operations):
    raise SystemExit("Final lifecycle diagnostics are incomplete.")
PY
echo "BEACON_HOSTED_THIN_FINAL_SNAPSHOT_OK hosted-fake-no-topology"

stop_test_host
if [[ "${test_host_exit_status}" != "0" ]]; then
  echo "Beacon TestHost failed during shutdown with exit code ${test_host_exit_status:-unknown}." >&2
  exit "${test_host_exit_status:-1}"
fi
cat "${host_output}"
grep -Fq "BEACON_HOSTED_WORKER_READY" "${host_output}"
grep -Fq "BEACON_HOSTED_WORKER_STOPPED" "${host_output}"
flow_validated=true
