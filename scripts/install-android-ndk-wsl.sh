#!/usr/bin/env bash
set -euo pipefail

readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_dir}/.." && pwd)"
readonly lock_path="${repository_root}/native/dependencies.lock.json"
mapfile -t ndk_lock < <(
  python3 - "${lock_path}" <<'PY'
import json
import pathlib
import sys

ndk = json.loads(pathlib.Path(sys.argv[1]).read_text())["toolchains"]["androidNdk"]
print(ndk["revision"])
print(ndk["linuxArchive"])
print(ndk["linuxSha1"])
PY
)
readonly ndk_revision="${ndk_lock[0]}"
readonly ndk_archive_url="${ndk_lock[1]}"
readonly ndk_sha1="${ndk_lock[2]}"
readonly root="${HOME}/.cache/beacon/android"
readonly archive="${root}/android-ndk-${ndk_revision}-linux.zip"
readonly ndk="${root}/android-ndk-${ndk_revision}"
readonly install_marker="${ndk}/.beacon-install-complete"
readonly install_format="hardlink-v2"

mkdir -p "${root}"
if [[ ! -f "${archive}" ]] || ! echo "${ndk_sha1}  ${archive}" | sha1sum --check --status; then
  curl --continue-at - \
    --fail \
    --location \
    --output "${archive}" \
    "${ndk_archive_url}"
fi

echo "${ndk_sha1}  ${archive}" | sha1sum --check -
if [[ ! -f "${install_marker}" ]] || [[ "$(cat "${install_marker}")" != "${install_format}" ]]; then
  rm -rf "${ndk}"
  python3 - "${archive}" "${root}" <<'PY'
import os
import pathlib
import shutil
import stat
import sys
import zipfile

archive = pathlib.Path(sys.argv[1])
destination = pathlib.Path(sys.argv[2])
with zipfile.ZipFile(archive) as package:
    package.extractall(destination)
    links = {}
    for entry in package.infolist():
        mode = entry.external_attr >> 16
        if stat.S_ISLNK(mode):
            links[destination / entry.filename] = package.read(entry).decode()
        elif mode:
            os.chmod(destination / entry.filename, mode)

    def final_target(path):
        visited = set()
        while path in links:
            if path in visited:
                raise RuntimeError(f"Archive link cycle at {path}")
            visited.add(path)
            path = pathlib.Path(os.path.normpath(path.parent / links[path]))
        return path

    for path in links:
        target = final_target(path)
        path.unlink()
        if target.is_dir():
            shutil.copytree(target, path)
        else:
            os.link(target, path)
PY
  echo "${install_format}" > "${install_marker}"
fi

"${ndk}/toolchains/llvm/prebuilt/linux-x86_64/bin/clang" --version | head -n 1
