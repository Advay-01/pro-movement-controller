#!/bin/sh
set -eu
app_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ -z "${DISPLAY:-}" ]; then
    printf '%s\n' 'Open an X11 desktop session and launch the controller there.' >&2
    exit 1
fi
if [ "${XDG_SESSION_TYPE:-}" = wayland ]; then
    printf '%s\n' 'Select an Xorg session at login. Wayland can restrict capture and global hotkeys.' >&2
    exit 1
fi
export OMP_THREAD_LIMIT="${OMP_THREAD_LIMIT:-1}"
exec mono "$app_dir/MovementController.exe" "$@"

