#!/usr/bin/env bash
set -euo pipefail
setup_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if ! command -v python3 >/dev/null 2>&1; then
    echo 'Python 3.10 or newer is required for setup.' >&2
    exit 1
fi
exec python3 "$setup_dir/setup.py" "$@"
