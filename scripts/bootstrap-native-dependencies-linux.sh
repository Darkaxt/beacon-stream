#!/usr/bin/env bash
set -euo pipefail

readonly script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly repository_root="$(cd "${script_dir}/.." && pwd)"
readonly lock_path="${repository_root}/native/dependencies.lock.json"
readonly destination="${HOME}/.cache/beacon/native/_deps"

mkdir -p "${destination}"

while IFS=$'\t' read -r name repository revision reference path; do
  target="${destination}/${path}"
  if [[ ! -d "${target}/.git" ]]; then
    mkdir -p "${target}"
    git -C "${target}" init --quiet
    git -C "${target}" remote add origin "${repository}"
  fi

  actual_remote="$(git -C "${target}" remote get-url origin)"
  if [[ "${actual_remote}" != "${repository}" ]]; then
    echo "Dependency '${name}' has unexpected remote '${actual_remote}'." >&2
    exit 1
  fi

  git -C "${target}" fetch --quiet --depth 1 origin "${reference}"
  git -C "${target}" checkout --quiet --detach "${revision}"
  actual_revision="$(git -C "${target}" rev-parse HEAD)"
  if [[ "${actual_revision}" != "${revision}" ]]; then
    echo "Dependency '${name}' resolved to '${actual_revision}', expected '${revision}'." >&2
    exit 1
  fi

  echo "${name} ${actual_revision}"
done < <(
  python3 - "${lock_path}" <<'PY'
import json
import pathlib
import sys

lock = json.loads(pathlib.Path(sys.argv[1]).read_text())
for name in ("msquic", "quictls", "protobuf"):
    item = lock["dependencies"][name]
    print("\t".join((name, item["repository"], item["revision"], item["ref"], item["path"])))
PY
)
