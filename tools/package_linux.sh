#!/usr/bin/env bash
# Builds the .deb, .rpm, .tar.gz and Flatpak packages from one self-contained publish folder.
# Usage: tools/package_linux.sh <version> <publish-dir> <icon-256.png> <output-dir> [--no-flatpak]
# Asset names follow InternalDesignGuidelines RELEASES.md Part 5: PingWatchdog-<version>-Linux-x64.<ext>.
set -euo pipefail

version="$1"; publish="$(realpath "$2")"; icon="$(realpath "$3")"; out="$(mkdir -p "$4" && realpath "$4")"
flatpak_build=1
[[ "${5:-}" == "--no-flatpak" ]] && flatpak_build=0
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Version must be MAJOR.MINOR.PATCH, got '$version'" >&2; exit 1; }
[[ -x "$publish/PingWatchdog" ]] || { echo "No PingWatchdog executable in $publish" >&2; exit 1; }

app_id="io.github.jpluker.PingWatchdog"
base="PingWatchdog-$version-Linux-x64"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

desktop_entry() { # <exec> <icon>
  cat <<EOF
[Desktop Entry]
Type=Application
Name=Ping Watchdog
Comment=Watch IP addresses and hostnames with ping
Exec=$1
Icon=$2
Terminal=false
Categories=Network;Monitor;
StartupWMClass=PingWatchdog
EOF
}

# The installed tree shared by .deb and .rpm. Each package says what it is, so the app can tell
# people where updates come from.
stage_system() { # <root> <kind>
  local root="$1" kind="$2"
  mkdir -p "$root/opt/ping-watchdog" "$root/usr/bin" "$root/usr/share/applications" \
    "$root/usr/share/icons/hicolor/256x256/apps" "$root/usr/share/doc/ping-watchdog"
  cp -a "$publish/." "$root/opt/ping-watchdog/"
  printf '%s\n' "$kind" > "$root/opt/ping-watchdog/package-kind.txt"
  ln -s /opt/ping-watchdog/PingWatchdog "$root/usr/bin/ping-watchdog"
  desktop_entry ping-watchdog ping-watchdog > "$root/usr/share/applications/$app_id.desktop"
  install -m644 "$icon" "$root/usr/share/icons/hicolor/256x256/apps/ping-watchdog.png"
  install -m644 "$publish/LICENSE" "$root/usr/share/doc/ping-watchdog/copyright"
}

# .deb
deb="$work/deb"
stage_system "$deb" deb
mkdir -p "$deb/DEBIAN"
size=$(du -sk "$deb" --exclude=DEBIAN | cut -f1)
cat > "$deb/DEBIAN/control" <<EOF
Package: ping-watchdog
Version: $version
Architecture: amd64
Maintainer: Joseph Luker <noreply@github.com>
Installed-Size: $size
Depends: libc6, libfontconfig1, libx11-6, libice6, libsm6
Recommends: libnotify-bin
Section: net
Priority: optional
Homepage: https://github.com/JPLuker/Ping-Watchdog
Description: Ping Watchdog
 Watches IP addresses and hostnames with ICMP ping, grouped by site, and shows
 outages and recoveries on a map, a list and a full-screen Wallboard.
EOF
dpkg-deb --root-owner-group --build "$deb" "$out/$base.deb" >/dev/null

# .rpm (no stripping: it breaks .NET single-file bundles)
rpmroot="$work/rpm"
stage_system "$work/rpm-stage" rpm
mkdir -p "$rpmroot"/{BUILD,RPMS,SPECS,SOURCES,SRPMS,BUILDROOT}
cat > "$rpmroot/SPECS/ping-watchdog.spec" <<'EOF'
Name:           ping-watchdog
Version:        %{pw_version}
Release:        1
Summary:        Ping Watchdog
License:        Proprietary
URL:            https://github.com/JPLuker/Ping-Watchdog
BuildArch:      x86_64
AutoReqProv:    no
Requires:       fontconfig, libX11, libICE, libSM
%global debug_package %{nil}
%global __os_install_post %{nil}
%global __strip /bin/true
%global _build_id_links none

%description
Watches IP addresses and hostnames with ICMP ping, grouped by site, and shows
outages and recoveries on a map, a list and a full-screen Wallboard.

%install
mkdir -p %{buildroot}
cp -a %{pw_stage}/. %{buildroot}/

%files
/opt/ping-watchdog
/usr/bin/ping-watchdog
/usr/share/applications/io.github.jpluker.PingWatchdog.desktop
/usr/share/icons/hicolor/256x256/apps/ping-watchdog.png
/usr/share/doc/ping-watchdog
EOF
rpmbuild -bb --quiet \
  --define "_topdir $rpmroot" --define "pw_version $version" --define "pw_stage $work/rpm-stage" \
  "$rpmroot/SPECS/ping-watchdog.spec"
cp "$rpmroot"/RPMS/x86_64/ping-watchdog-"$version"-1.x86_64.rpm "$out/$base.rpm"

# .tar.gz: a folder to run from anywhere, with a desktop entry to install by hand.
tarroot="$work/tar/$base"
mkdir -p "$tarroot"
cp -a "$publish/." "$tarroot/"
printf 'tar.gz\n' > "$tarroot/package-kind.txt"
install -m644 "$icon" "$tarroot/ping-watchdog.png"
desktop_entry "PingWatchdog" ping-watchdog > "$tarroot/ping-watchdog.desktop"
cat > "$tarroot/README.txt" <<'EOF'
PING WATCHDOG FOR LINUX

Run ./PingWatchdog from this folder.

Settings:  ~/.config/PingWatchdog/
History:   ~/.local/state/PingWatchdog/

To add it to your app menu, edit Exec= in ping-watchdog.desktop to the full path of
PingWatchdog, then copy the file to ~/.local/share/applications/.
EOF
tar -C "$work/tar" --owner=0 --group=0 -czf "$out/$base.tar.gz" "$base"

# Flatpak bundle (single file, installs with `flatpak install --user <file>`).
if [[ "$flatpak_build" == 1 ]]; then
  src="$work/flatpak-src"
  mkdir -p "$src/files"
  cp -a "$publish/." "$src/files/"
  printf 'flatpak\n' > "$src/files/package-kind.txt"
  install -m644 "$icon" "$src/$app_id.png"
  desktop_entry ping-watchdog "$app_id" > "$src/$app_id.desktop"
  cat > "$work/$app_id.yml" <<EOF
app-id: $app_id
runtime: org.freedesktop.Platform
runtime-version: '24.08'
sdk: org.freedesktop.Sdk
command: ping-watchdog
finish-args:
  - --share=network
  - --share=ipc
  - --socket=x11
  - --device=dri
  - --talk-name=org.freedesktop.Notifications
  - --talk-name=org.kde.StatusNotifierWatcher
  - --env=DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
build-options:
  no-debuginfo: true
  strip: false
modules:
  - name: ping-watchdog
    buildsystem: simple
    build-commands:
      - mkdir -p /app/lib/ping-watchdog /app/bin
      - cp -a files/. /app/lib/ping-watchdog/
      - ln -s /app/lib/ping-watchdog/PingWatchdog /app/bin/ping-watchdog
      - install -Dm644 $app_id.desktop /app/share/applications/$app_id.desktop
      - install -Dm644 $app_id.png /app/share/icons/hicolor/256x256/apps/$app_id.png
    sources:
      - type: dir
        path: flatpak-src
EOF
  flatpak-builder --user --force-clean --disable-rofiles-fuse --repo="$work/repo" "$work/build" "$work/$app_id.yml" >/dev/null
  flatpak build-bundle "$work/repo" "$out/$base.flatpak" "$app_id"
fi

ls -l "$out"
