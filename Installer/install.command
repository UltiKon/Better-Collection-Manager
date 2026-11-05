#!/bin/bash
setup_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
/bin/bash "$setup_dir/install.sh" "$@"
setup_status=$?
if [[ -t 0 ]]; then
    read -r -p 'Press Enter to close ...' _bcm_finish
fi
exit "$setup_status"
