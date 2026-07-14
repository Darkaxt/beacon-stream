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
readonly client_id="z-fold-7"
readonly package_name="dev.beacon.android"
readonly test_runner="dev.beacon.android.test/androidx.test.runner.AndroidJUnitRunner"
readonly test_class="dev.beacon.android.BeaconStreamCoreInstrumentationTest"

require_command() {
  local command_name="$1"
  if [[ "${command_name}" == */* ]]; then
    if [[ ! -x "${command_name}" ]]; then
      echo "Required command '${command_name}' is unavailable." >&2
      exit 1
    fi
  elif ! command -v "${command_name}" >/dev/null 2>&1; then
    echo "Required command '${command_name}' is unavailable." >&2
    exit 1
  fi
}

require_file() {
  local description="$1"
  local path="$2"
  if [[ ! -f "${path}" ]]; then
    echo "${description} is unavailable at '${path}'." >&2
    exit 1
  fi
}

require_command "${adb_command}"
require_command "${curl_command}"
require_command "${dotnet_command}"
require_command "${gradle_command}"
require_command "${python_command}"
require_command bash

bash "${native_build_script}"
"${dotnet_command}" build \
  "${repository_root}/tests/Beacon.Server.TestHost/Beacon.Server.TestHost.csproj" \
  --configuration Release --nologo
"${gradle_command}" -p "${repository_root}/src/Beacon.Android" \
  assembleDebug assembleDebugAndroidTest

require_file "Hosted benchmark Worker" "${hosted_worker}"
require_file "Beacon TestHost assembly" "${test_host_assembly}"
require_file "Beacon debug APK" "${app_apk}"
require_file "Beacon instrumentation APK" "${test_apk}"

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
test_host_pid=""
test_host_output_fd=""
host_drain_pid=""
test_host_exit_status=""
app_prepared=false

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
    "${adb_command}" -s "${serial}" shell run-as "${package_name}" \
      rm -f files/beacon-gate3-client-credential >/dev/null 2>&1 || true
  fi
  if [[ "${status}" -ne 0 && -f "${host_output}" ]]; then
    cat "${host_output}" >&2
  fi
  rm -rf "${temporary_directory}"
  exit "${status}"
}
trap cleanup EXIT INT TERM

"${python_command}" - \
  "${client_id}" "${credentials_path}" "${credential_evidence}" <<'PY'
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
    persisted = [{
        "clientId": client_id,
        "salt": base64.b64encode(salt).decode("ascii"),
        "hash": base64.b64encode(hashlib.sha256(salt + credential).digest()).decode("ascii"),
        "revoked": False,
    }]
    Path(store_path).write_text(json.dumps(persisted), encoding="utf-8")
    Path(evidence_path).write_text(
        base64.b64encode(credential).decode("ascii"),
        encoding="utf-8")
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
if [[ -z "${host_port}" ]]; then
  echo "Beacon TestHost exited before structured Kestrel readiness." >&2
  exit 1
fi

cat <&"${test_host_output_fd}" >> "${host_output}" &
host_drain_pid=$!

local_server_url="https://127.0.0.1:${host_port}"
emulator_server_url="https://10.0.2.2:${host_port}"
identity_json="$("${curl_command}" --fail --silent --show-error --insecure \
  "${local_server_url}/identity")"
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
"${adb_command}" -s "${serial}" shell run-as "${package_name}" mkdir -p files >/dev/null
"${adb_command}" -s "${serial}" exec-in run-as "${package_name}" sh -c \
  'cat > files/beacon-gate3-client-credential' < "${credential_evidence}"
app_prepared=true
"${adb_command}" -s "${serial}" logcat -c

validate_snapshot() {
  local phase="$1"
  local admin_snapshot="${temporary_directory}/snapshot-${phase}.json"
  local worker_snapshot="${temporary_directory}/worker-${phase}.json"
  "${curl_command}" --fail --silent --show-error --insecure \
    "${local_server_url}/admin/snapshot" > "${admin_snapshot}"
  "${curl_command}" --fail --silent --show-error --insecure \
    "${local_server_url}/hosted-benchmark-worker/snapshot" > "${worker_snapshot}"
  "${python_command}" - "${phase}" "${client_id}" \
    "${admin_snapshot}" "${worker_snapshot}" <<'PY'
import json
from pathlib import Path
import sys

phase, client_id, admin_path, worker_path = sys.argv[1:]
admin = json.loads(Path(admin_path).read_text(encoding="utf-8"))
worker = json.loads(Path(worker_path).read_text(encoding="utf-8"))
clients = [value for value in admin.get("clients", []) if value.get("clientId") == client_id]
if len(clients) != 1:
    raise SystemExit("Hosted benchmark snapshot does not contain exactly one target client.")
benchmarks = clients[0].get("benchmarks", [])
if any(value.get("completedAt") is None for value in benchmarks):
    raise SystemExit("Hosted benchmark snapshot retained a pending run.")
if phase == "certified-manual" and not any(
        value.get("trigger") == "manual" and value.get("selectedResult")
        for value in benchmarks):
    raise SystemExit("Certified manual benchmark evidence is unavailable.")
if phase == "certified-preflight" and not any(
        value.get("trigger") == "sessionPreflight" and value.get("selectedResult")
        for value in benchmarks):
    raise SystemExit("Certified session-preflight evidence is unavailable.")
if phase == "certified-preflight":
    operations = {value.get("operation") for value in admin.get("diagnostics", [])}
    required = {"worker.transport_authenticated", "worker.transport_disconnected"}
    if not required.issubset(operations):
        raise SystemExit("Hosted benchmark transport diagnostics are incomplete.")
if not worker.get("isReady") or int(worker.get("processGeneration", 0)) <= 0:
    raise SystemExit("Hosted benchmark Worker is not ready.")
if "BEACON_HOSTED_WORKER_READY" not in worker.get("diagnostics", []):
    raise SystemExit("Hosted benchmark Worker readiness marker is unavailable.")
PY
  printf 'BEACON_HOSTED_BENCHMARK_SNAPSHOT_OK %s\n' "${phase}"
}

emit_transport_diagnostics() {
  printf '%s\n' '--- Beacon StreamCore transport diagnostics ---' >&2
  "${adb_command}" -s "${serial}" logcat -d -s BeaconStreamCore:I '*:S' >&2 || true
  printf '%s\n' '--- Beacon hosted Worker snapshot ---' >&2
  "${curl_command}" --fail --silent --show-error --insecure \
    "${local_server_url}/hosted-benchmark-worker/snapshot" >&2 || true
  printf '\n' >&2
  printf '%s\n' '--- Beacon Server admin snapshot ---' >&2
  "${curl_command}" --fail --silent --show-error --insecure \
    "${local_server_url}/admin/snapshot" >&2 || true
  printf '\n' >&2
}

run_instrumentation() {
  local method="$1"
  shift
  local output_file="${temporary_directory}/${method}.txt"
  if ! "${adb_command}" -s "${serial}" shell am instrument -w -r \
      -e serverUrl "${emulator_server_url}" \
      -e clientId "${client_id}" \
      -e serverPublicKeyFingerprint "${server_fingerprint}" \
      -e class "${test_class}#${method}" \
      "${test_runner}" > "${output_file}" 2>&1; then
    cat "${output_file}" >&2
    emit_transport_diagnostics
    echo "Hosted benchmark instrumentation '${method}' failed." >&2
    return 1
  fi
  cat "${output_file}"
  if ! grep -Fq "OK (1 test)" "${output_file}"; then
    emit_transport_diagnostics
    echo "Hosted benchmark instrumentation '${method}' did not pass." >&2
    return 1
  fi
  local marker
  for marker in "$@"; do
    if ! grep -Fq "${marker}" "${output_file}"; then
      emit_transport_diagnostics
      echo "Hosted benchmark instrumentation '${method}' omitted marker '${marker}'." >&2
      return 1
    fi
  done
}

run_instrumentation \
  gate4NetworkAndHardwareBenchmark \
  BEACON_GATE4_NATIVE_NETWORK_COMPLETE \
  BEACON_GATE4_REAL_HARDWARE_OBSERVED
if ! grep -Eq \
    'BEACON_GATE4_REAL_HARDWARE_(CAPABILITY_REJECTED|ACCEPTED)' \
    "${temporary_directory}/gate4NetworkAndHardwareBenchmark.txt"; then
  echo "Real hardware benchmark emitted no typed capability outcome." >&2
  exit 1
fi
validate_snapshot real-hardware

run_instrumentation \
  gate4CertifiedBenchmarkEvidence \
  BEACON_GATE4_CERTIFIED_MANUAL_COMPLETE
validate_snapshot certified-manual

run_instrumentation \
  gate4CertifiedSessionPreflight \
  BEACON_GATE4_CERTIFIED_PREFLIGHT_COMPLETE
validate_snapshot certified-preflight

stop_test_host
if [[ "${test_host_exit_status}" != "0" ]]; then
  echo "Beacon TestHost failed during shutdown with exit code ${test_host_exit_status:-unknown}." >&2
  exit "${test_host_exit_status:-1}"
fi
cat "${host_output}"
grep -Fq "BEACON_HOSTED_WORKER_READY" "${host_output}"
grep -Fq "BEACON_HOSTED_WORKER_STOPPED" "${host_output}"

echo BEACON_HOSTED_EMULATOR_BENCHMARKS_OK
