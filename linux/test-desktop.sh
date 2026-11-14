#!/usr/bin/env bash
set -euo pipefail

if [[ "${WATCHDOG_BACKEND:-x11}" == wayland ]]; then
  export XDG_RUNTIME_DIR
  XDG_RUNTIME_DIR=$(mktemp -d)
  chmod 700 "$XDG_RUNTIME_DIR"
  export WAYLAND_DISPLAY=watchdog-test
  export LIBGL_ALWAYS_SOFTWARE=1
  weston --backend=headless-backend.so --renderer=gl --width=1920 --height=1080 --socket="$WAYLAND_DISPLAY" --idle-time=0 >/tmp/watchdog-weston.log 2>&1 &
  compositor_pid=$!
  trap 'kill "$compositor_pid" 2>/dev/null || true; rm -rf "$XDG_RUNTIME_DIR"' EXIT
  for attempt in {1..50}; do
    [[ -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ]] && break
    kill -0 "$compositor_pid" 2>/dev/null || { cat /tmp/watchdog-weston.log; exit 1; }
    sleep .1
  done
  [[ -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ]] || { cat /tmp/watchdog-weston.log; exit 1; }
  dotnet run --no-build --project linux/PingWatchdog.Linux/PingWatchdog.Linux.csproj -c Release -- --ui-regression-test
else
  xvfb-run -a -s "-screen 0 3840x2160x24" bash -c 'openbox >/tmp/watchdog-openbox.log 2>&1 & dotnet run --no-build --project linux/PingWatchdog.Linux/PingWatchdog.Linux.csproj -c Release -- --ui-regression-test'
fi
