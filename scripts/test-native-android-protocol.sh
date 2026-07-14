#!/usr/bin/env bash
set -euo pipefail

readonly serial="${ANDROID_SERIAL:-emulator-5554}"
readonly script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_directory}/.." && pwd)"
readonly build_root="${HOME}/.cache/beacon/build/android-x86_64-debug"
readonly remote_directory="/data/local/tmp/beacon-native"
readonly vector_name="beacon-h264-high-8-640x360-30-v1.bau"
readonly vector_path="${repository_root}/src/Beacon.Android/app/src/main/assets/benchmark-vectors/${vector_name}"
readonly tests=(
  BeaconStreamProtocolVersionTests
  BeaconMediaDatagramTests
  BeaconFakeTransportTests
  BeaconSessionTests
  BeaconFrameAssemblerTests
  BeaconServerSessionProtocolTests
  BeaconVideoMediaPacketizerTests
  BeaconAccessUnitVectorTests
  BeaconHostedEmulatorEndpointServerTests
  BeaconMsQuicTransportTests
  BeaconAndroidStreamCoreTests
  BeaconAndroidCertificatePinTests
  BeaconAndroidLifecycleTests
)

adb -s "${serial}" get-state >/dev/null
adb -s "${serial}" shell mkdir -p "${remote_directory}"
adb -s "${serial}" push \
  "${build_root}/msquic/bin/Debug/libmsquic.so" \
  "${remote_directory}/libmsquic.so" >/dev/null
adb -s "${serial}" push \
  "${vector_path}" "${remote_directory}/${vector_name}" >/dev/null

for test_name in "${tests[@]}"; do
  local_executable="${build_root}/Beacon.StreamProtocol.Tests/${test_name}"
  if [[ ! -f "${local_executable}" ]]; then
    echo "Android native test '${test_name}' is not built." >&2
    exit 1
  fi
  adb -s "${serial}" push \
    "${local_executable}" "${remote_directory}/${test_name}" >/dev/null
  adb -s "${serial}" shell chmod 755 "${remote_directory}/${test_name}"
  test_argument=""
  if [[ "${test_name}" == BeaconAccessUnitVectorTests ]]; then
    test_argument="${remote_directory}/${vector_name}"
  fi
  adb -s "${serial}" shell \
    "cd ${remote_directory} && export LD_LIBRARY_PATH=. && ./${test_name} ${test_argument}"
done

echo BEACON_ANDROID_PROTOCOL_TESTS_OK
