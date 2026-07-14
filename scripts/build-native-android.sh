#!/usr/bin/env bash
set -euo pipefail

readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_dir}/.." && pwd)"
readonly configuration="${1:-Debug}"

if [[ "${configuration}" != "Debug" && "${configuration}" != "Release" ]]; then
  echo "Android native configuration must be Debug or Release." >&2
  exit 1
fi
readonly configuration_dir="${configuration,,}"
if [[ "${configuration}" == "Debug" ]]; then
  readonly x86_64_build_testing="ON"
else
  readonly x86_64_build_testing="OFF"
fi

if [[ -z "${BEACON_ANDROID_NDK_ROOT:-}" ]]; then
  echo "BEACON_ANDROID_NDK_ROOT must name the pinned Linux NDK root." >&2
  exit 1
fi

export ANDROID_NDK_ROOT="${BEACON_ANDROID_NDK_ROOT}"
export ANDROID_NDK_HOME="${BEACON_ANDROID_NDK_ROOT}"
export PATH="${BEACON_ANDROID_NDK_ROOT}/toolchains/llvm/prebuilt/linux-x86_64/bin:${PATH}"
export CMAKE_BUILD_PARALLEL_LEVEL="${CMAKE_BUILD_PARALLEL_LEVEL:-$(nproc)}"

bash "${script_dir}/bootstrap-native-dependencies-linux.sh"

export BEACON_PROTOC_EXECUTABLE="$(bash "${script_dir}/install-protoc-linux.sh")"

cd "${repository_root}/native"
cmake --fresh --preset android-x86_64 \
  -B "${HOME}/.cache/beacon/build/android-x86_64-${configuration_dir}" \
  -DCMAKE_BUILD_TYPE="${configuration}" \
  -DBUILD_TESTING="${x86_64_build_testing}"
cmake --build "${HOME}/.cache/beacon/build/android-x86_64-${configuration_dir}"
cmake --fresh --preset android-arm64 \
  -B "${HOME}/.cache/beacon/build/android-arm64-${configuration_dir}" \
  -DCMAKE_BUILD_TYPE="${configuration}" \
  -DBUILD_TESTING=OFF
cmake --build "${HOME}/.cache/beacon/build/android-arm64-${configuration_dir}"

readonly staging_parent="${repository_root}/src/Beacon.Android/app/build/generated/jniLibs"
readonly staging_root="${staging_parent}/${configuration_dir}"
readonly temporary_root="${staging_parent}/.${configuration_dir}.tmp.$$"
trap 'rm -rf "${temporary_root}"' EXIT
rm -rf "${temporary_root}"
mkdir -p "${temporary_root}"
stage_abi() {
  local preset="$1"
  local abi="$2"
  local build_root="${HOME}/.cache/beacon/build/${preset}-${configuration_dir}"
  local destination="${temporary_root}/${abi}"
  mkdir -p "${destination}"
  install -m 0644 \
    "${build_root}/Beacon.Android.StreamCore/libbeacon_streamcore.so" \
    "${destination}/libbeacon_streamcore.so"
  install -m 0644 \
    "${build_root}/msquic/bin/${configuration}/libmsquic.so" \
    "${destination}/libmsquic.so"
}

stage_abi android-x86_64 x86_64
stage_abi android-arm64 arm64-v8a

rm -rf "${staging_root}"
mv "${temporary_root}" "${staging_root}"
trap - EXIT
