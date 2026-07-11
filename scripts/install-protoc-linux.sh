#!/usr/bin/env bash
set -euo pipefail

readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_dir}/.." && pwd)"
readonly lock_path="${repository_root}/native/dependencies.lock.json"

readarray -t toolchain < <(
  python3 - "${lock_path}" <<'PY'
import json
import pathlib
import sys

item = json.loads(pathlib.Path(sys.argv[1]).read_text())["toolchains"]["protobufCompiler"]
print(item["version"])
print(item["linuxArchive"])
print(item["linuxSha256"])
PY
)

readonly version="${toolchain[0]}"
readonly archive_url="${toolchain[1]}"
readonly expected_hash="${toolchain[2]}"
readonly destination="${HOME}/.cache/beacon/tools/protoc-${version}-linux-x86_64"
readonly archive="${destination}/protoc-${version}-linux-x86_64.zip"
readonly executable="${destination}/bin/protoc"

mkdir -p "${destination}"
if [[ ! -f "${archive}" ]]; then
  curl --fail --location --silent --show-error "${archive_url}" --output "${archive}"
fi

echo "${expected_hash}  ${archive}" | sha256sum --check --status
if [[ ! -f "${executable}" ]]; then
  python3 - "${archive}" "${destination}" <<'PY'
import pathlib
import sys
import zipfile

with zipfile.ZipFile(sys.argv[1]) as archive:
    archive.extractall(pathlib.Path(sys.argv[2]))
PY
fi
chmod u+x "${executable}"

actual_version="$("${executable}" --version)"
if [[ "${actual_version}" != "libprotoc ${version}" ]]; then
  echo "Pinned protoc reported '${actual_version}'." >&2
  exit 1
fi

printf '%s\n' "${executable}"
