#!/usr/bin/env bash
set -euo pipefail

readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_dir}/.." && pwd)"

if [[ -z "${BEACON_ANDROID_NDK_ROOT:-}" ]]; then
  echo "BEACON_ANDROID_NDK_ROOT must name the pinned Linux NDK root." >&2
  exit 1
fi

export ANDROID_NDK_ROOT="${BEACON_ANDROID_NDK_ROOT}"
export ANDROID_NDK_HOME="${BEACON_ANDROID_NDK_ROOT}"
export PATH="${BEACON_ANDROID_NDK_ROOT}/toolchains/llvm/prebuilt/linux-x86_64/bin:${PATH}"

bash "${script_dir}/bootstrap-native-dependencies-linux.sh"

cd "${repository_root}/native"
cmake --fresh --preset android-x86_64
cmake --build --preset android-x86_64-debug
cmake --fresh --preset android-arm64
cmake --build --preset android-arm64-debug
