#!/usr/bin/env bash
set -euo pipefail

readonly script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_directory}/.." && pwd)"

bash "${script_directory}/bootstrap-native-dependencies-linux.sh"
export BEACON_PROTOC_EXECUTABLE="$(bash "${script_directory}/install-protoc-linux.sh")"

cd "${repository_root}/native"
cmake --fresh --preset linux-x64
cmake --build --preset linux-x64-debug
ctest --preset linux-x64-debug
