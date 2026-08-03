#!/usr/bin/env bash
set -euo pipefail

readonly script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_directory}/.." && pwd)"
readonly serial="${ANDROID_SERIAL:-emulator-5554}"
readonly adb_command="${BEACON_ADB:-adb}"
readonly build_root="${BEACON_ANDROID_BUILD_ROOT:-${HOME}/.cache/beacon/build/android-x86_64-debug}"
readonly endpoint="${build_root}/Beacon.StreamProtocol.Tests/BeaconHostedEmulatorEndpoint"
readonly msquic="${build_root}/msquic/bin/Debug/libmsquic.so"
readonly vector="${repository_root}/src/Beacon.Android/app/src/main/assets/benchmark-vectors/beacon-h264-high-8-640x360-30-v1.bau"
readonly app_apk="${repository_root}/src/Beacon.Android/app/build/outputs/apk/debug/app-debug.apk"
readonly test_apk="${repository_root}/src/Beacon.Android/app/build/outputs/apk/androidTest/debug/app-debug-androidTest.apk"
readonly remote_directory="/data/local/tmp/beacon-hosted-stream"
readonly test_method="dev.beacon.android.HostedEmulatorStreamInstrumentationTest#rendersChangingH264FramesThroughProductionStreamCore"
readonly test_runner="dev.beacon.android.test/androidx.test.runner.AndroidJUnitRunner"

require_command() {
  local command_name="$1"
  if ! command -v "${command_name}" >/dev/null 2>&1; then
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
require_command openssl
require_file "Hosted emulator endpoint" "${endpoint}"
require_file "Android MsQuic library" "${msquic}"
require_file "Hosted H.264 access-unit vector" "${vector}"
require_file "Beacon debug APK" "${app_apk}"
require_file "Beacon instrumentation APK" "${test_apk}"

if ! "${adb_command}" -s "${serial}" get-state >/dev/null 2>&1; then
  echo "Android emulator '${serial}' is unavailable." >&2
  exit 1
fi

temporary_directory="$(mktemp -d)"
certificate="${temporary_directory}/certificate.pem"
private_key="${temporary_directory}/private-key.pem"
endpoint_pid=""
endpoint_output_fd=""
remote_prepared=false

stop_endpoint() {
  "${adb_command}" -s "${serial}" shell \
    "if [ -f ${remote_directory}/endpoint.pid ]; then kill \$(cat ${remote_directory}/endpoint.pid) 2>/dev/null || true; fi" \
    >/dev/null 2>&1 || true
}

drain_endpoint_output() {
  endpoint_output="$(cat <&"${endpoint_output_fd}")"
  if wait "${endpoint_pid}"; then
    endpoint_status=0
  else
    endpoint_status=$?
  fi
  endpoint_pid=""
}

cleanup() {
  local status=$?
  trap - EXIT
  if [[ "${remote_prepared}" == true ]]; then
    if [[ -n "${endpoint_pid}" ]]; then
      stop_endpoint
    fi
    "${adb_command}" -s "${serial}" shell rm -rf "${remote_directory}" \
      >/dev/null 2>&1 || true
  fi
  if [[ -n "${endpoint_pid}" ]] && kill -0 "${endpoint_pid}" 2>/dev/null; then
    kill "${endpoint_pid}" 2>/dev/null || true
  fi
  if [[ -n "${endpoint_output_fd}" ]]; then
    exec {endpoint_output_fd}<&- || true
  fi
  rm -rf "${temporary_directory}"
  exit "${status}"
}
trap cleanup EXIT

openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
  -subj "/CN=localhost" \
  -addext "subjectAltName=DNS:localhost,IP:127.0.0.1" \
  -keyout "${private_key}" \
  -out "${certificate}" \
  >/dev/null 2>&1

fingerprint="$(
  openssl x509 -in "${certificate}" -pubkey -noout |
    openssl pkey -pubin -outform DER 2>/dev/null |
    openssl dgst -sha256 -hex |
    awk '{ print toupper($NF) }'
)"
if [[ ! "${fingerprint}" =~ ^[0-9A-F]{64}$ ]]; then
  echo "Could not calculate the hosted endpoint SPKI SHA-256 fingerprint." >&2
  exit 1
fi

"${adb_command}" -s "${serial}" shell rm -rf "${remote_directory}"
"${adb_command}" -s "${serial}" shell mkdir -p "${remote_directory}"
remote_prepared=true
"${adb_command}" -s "${serial}" push \
  "${endpoint}" "${remote_directory}/BeaconHostedEmulatorEndpoint" >/dev/null
"${adb_command}" -s "${serial}" push \
  "${msquic}" "${remote_directory}/libmsquic.so" >/dev/null
"${adb_command}" -s "${serial}" push \
  "${certificate}" "${remote_directory}/certificate.pem" >/dev/null
"${adb_command}" -s "${serial}" push \
  "${private_key}" "${remote_directory}/private-key.pem" >/dev/null
"${adb_command}" -s "${serial}" push \
  "${vector}" "${remote_directory}/frames.bau" >/dev/null
"${adb_command}" -s "${serial}" shell chmod 700 \
  "${remote_directory}/BeaconHostedEmulatorEndpoint"

"${adb_command}" -s "${serial}" install -r "${app_apk}" >/dev/null
"${adb_command}" -s "${serial}" install -r "${test_apk}" >/dev/null
"${adb_command}" -s "${serial}" logcat -c

coproc HOSTED_ENDPOINT {
  "${adb_command}" -s "${serial}" shell \
    "cd ${remote_directory} && echo \$\$ > endpoint.pid && export LD_LIBRARY_PATH=. && exec ./BeaconHostedEmulatorEndpoint --certificate certificate.pem --private-key private-key.pem --vector frames.bau 2>&1"
}
endpoint_pid="${HOSTED_ENDPOINT_PID:-}"
endpoint_source_fd="${HOSTED_ENDPOINT[0]:-}"
if [[ -z "${endpoint_pid}" || -z "${endpoint_source_fd}" ]]; then
  echo "Hosted endpoint exited before its output stream was attached." >&2
  exit 1
fi
exec {endpoint_output_fd}<&"${endpoint_source_fd}"

if ! IFS= read -r readiness <&"${endpoint_output_fd}"; then
  wait "${endpoint_pid}" || true
  endpoint_pid=""
  echo "Hosted endpoint exited before reporting readiness." >&2
  exit 1
fi
if [[ ! "${readiness}" =~ ^BEACON_HOSTED_ENDPOINT_READY[[:space:]]+([0-9]+)$ ]]; then
  echo "Unexpected hosted endpoint readiness output: ${readiness}" >&2
  exit 1
fi
readonly endpoint_port="${BASH_REMATCH[1]}"

instrumentation_output=""
instrumentation_status=0
if instrumentation_output="$(
  "${adb_command}" -s "${serial}" shell am instrument -w -r \
    -e hostedStreamEndpointPort "${endpoint_port}" \
    -e hostedStreamPublicKeyFingerprint "${fingerprint}" \
    -e class "${test_method}" \
    "${test_runner}" 2>&1
)"; then
  :
else
  instrumentation_status=$?
fi
if (( instrumentation_status != 0 )); then
  printf '%s\n' "${instrumentation_output}" >&2
  stop_endpoint
  drain_endpoint_output
  printf '%s\n' "${readiness}" "${endpoint_output}" >&2
  echo "Hosted stream instrumentation failed." >&2
  exit 1
fi
printf '%s\n' "${instrumentation_output}"
if ! grep -Fq "OK (1 test)" <<<"${instrumentation_output}"; then
  stop_endpoint
  drain_endpoint_output
  printf '%s\n' "${readiness}" "${endpoint_output}" >&2
  echo "Hosted stream instrumentation did not pass." >&2
  exit 1
fi

drain_endpoint_output
if (( endpoint_status != 0 )); then
  printf '%s\n' "${readiness}" "${endpoint_output}" >&2
  echo "Hosted endpoint failed with exit code ${endpoint_status}." >&2
  exit 1
fi
printf '%s\n' "${readiness}" "${endpoint_output}"

client_evidence="$(
  "${adb_command}" -s "${serial}" logcat -d -s BeaconHostedStream:I '*:S'
)"
printf '%s\n' "${client_evidence}"

grep -Fq "BEACON_HOSTED_ENDPOINT_AUTHENTICATED 1" <<<"${endpoint_output}"
grep -Fq "BEACON_HOSTED_ENDPOINT_FRAMES 30" <<<"${endpoint_output}"
grep -Fq "BEACON_HOSTED_ENDPOINT_RENDERED_FEEDBACK 30" <<<"${endpoint_output}"
grep -Fq "BEACON_HOSTED_ENDPOINT_STOPPED 1" <<<"${endpoint_output}"
grep -Fq "BEACON_HOSTED_STREAM_FRAMES 30" <<<"${client_evidence}"
grep -Eq "BEACON_HOSTED_STREAM_PIXEL_VARIANTS ([2-9]|[1-9][0-9]+)" \
  <<<"${client_evidence}"

echo BEACON_HOSTED_EMULATOR_STREAM_OK
