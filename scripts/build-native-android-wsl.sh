#!/usr/bin/env bash
set -euo pipefail

readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

bash "${script_dir}/install-android-ndk-wsl.sh"
export BEACON_ANDROID_NDK_ROOT="${HOME}/.cache/beacon/android/android-ndk-r27d"
bash "${script_dir}/build-native-android.sh"
