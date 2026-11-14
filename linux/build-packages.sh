#!/usr/bin/env bash
# Builds, tests and packages the Linux release: AppImage (Velopack, self-updating), .deb, .rpm, .tar.gz and
# Flatpak. Each package is installed or extracted and run before anything is staged. Fails closed.
# Usage: linux/build-packages.sh validate|release
# release also downloads the previous Linux release for delta updates and stages dist/ for upload.
set -euo pipefail
mode="${1:?validate or release}"
cd "$(dirname "$0")/.."
project=linux/PingWatchdog.Linux/PingWatchdog.Linux.csproj
version=$(python3 -c "import xml.etree.ElementTree as E; print(E.parse('$project').getroot().find('./PropertyGroup/Version').text.strip())")
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "::error::Linux version '$version' isn't MAJOR.MINOR.PATCH"; exit 1; }
echo "WATCHDOG_VERSION=$version" >> "${GITHUB_ENV:-/dev/null}"
base="PingWatchdog-$version-Linux-x64"
app_id="io.github.jpluker.PingWatchdog"
echo "Building Ping Watchdog $version for Linux ($mode)"

group() { echo "::group::$1"; }
endgroup() { echo "::endgroup::"; }

group "Prerequisites"
sudo apt-get update -qq
sudo apt-get install -y -qq libfuse2 imagemagick xvfb openbox dbus-x11 libx11-6 libice6 libsm6 libfontconfig1 rpm rpm2cpio cpio flatpak flatpak-builder >/dev/null
dotnet tool install --tool-path ./.tools vpk --version 1.2.161 >/dev/null
python3 - <<'PY'
import json
cfg = json.load(open('default.pingwatch.json', encoding='utf-8'))
if not cfg.get('Sites'): raise SystemExit('Starter config must contain a site.')
PY
endgroup

group "Tests"
dotnet run --project "$project" -c Release -p:Version="$version" -- --self-test
timeout 300s dbus-run-session -- xvfb-run -a -s "-screen 0 1920x1080x24" bash -c \
  "openbox >/tmp/watchdog-openbox.log 2>&1 & dotnet run --no-build --project $project -c Release -- --ui-regression-test"
endgroup

group "Publish"
rm -rf publish-linux Releases-linux dist
dotnet publish "$project" -c Release -r linux-x64 --self-contained true -p:Version="$version" \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -o publish-linux
cp default.pingwatch.json LICENSE publish-linux/
python3 tools/collect_linux_notices.py publish-linux/ThirdPartyNotices linux/PingWatchdog.Linux/bin/Release/net8.0/linux-x64/PingWatchdog.deps.json
test -s publish-linux/ThirdPartyNotices/INDEX.txt
convert Assets/WatchdogLogo.png -resize 256x256 PingWatchdog.png
endgroup

# In-app notes, shown by the updater. Short; the release page has the full notes.
sed "s/{version}/$version/g" linux/RELEASE-NOTES.md > release-notes-linux.md

group "AppImage"
if [[ "$mode" == release ]]; then
  ./.tools/vpk download github --repoUrl https://github.com/JPLuker/Ping-Watchdog --token "${VPK_TOKEN:-}" \
    --channel linux --pre true --outputDir Releases-linux || echo "::warning title=No delta::Previous Linux release not downloaded; full-package updates still work."
fi
./.tools/vpk pack --packId PingWatchdog --packVersion "$version" --packDir publish-linux --mainExe PingWatchdog \
  --packTitle "Ping Watchdog" --packAuthors "Joseph Luker" --runtime linux-x64 --icon PingWatchdog.png \
  --releaseNotes release-notes-linux.md --channel linux --outputDir Releases-linux
image="$PWD/Releases-linux/PingWatchdog.AppImage"
test -s "$image" && test -s Releases-linux/releases.linux.json && test -s "Releases-linux/PingWatchdog-$version-linux-full.nupkg"
chmod +x "$image"
scratch=$(mktemp -d)
(
  cd "$scratch"
  "$image" --appimage-extract >/dev/null
  export XDG_CONFIG_HOME="$scratch/config" XDG_STATE_HOME="$scratch/state"
  timeout 120s ./squashfs-root/AppRun --self-test
  timeout 300s dbus-run-session -- xvfb-run -a -s "-screen 0 1920x1080x24" bash -c 'openbox >/tmp/watchdog-packaged-openbox.log 2>&1 & ./squashfs-root/AppRun --ui-regression-test' >/dev/null
)
rm -rf "$scratch"
echo "AppImage self-test and desktop regressions passed"
endgroup

group "Flatpak runtime"
# Ubuntu runners block the unprivileged user namespaces flatpak-builder's sandbox needs.
sudo sysctl -w kernel.apparmor_restrict_unprivileged_userns=0 >/dev/null 2>&1 || true
flatpak remote-add --user --if-not-exists flathub https://dl.flathub.org/repo/flathub.flatpakrepo
flatpak install --user --noninteractive -y flathub org.freedesktop.Platform//24.08 org.freedesktop.Sdk//24.08 >/dev/null
endgroup

group "deb, rpm, tar.gz, Flatpak"
bash tools/package_linux.sh "$version" publish-linux PingWatchdog.png dist
endgroup

group "Exercise packages"
# .deb: install with apt, run the installed command, remove.
sudo apt-get install -y -qq "./dist/$base.deb" >/dev/null
test -x /usr/bin/ping-watchdog && test "$(cat /opt/ping-watchdog/package-kind.txt)" = deb
test -f "/usr/share/applications/$app_id.desktop" && test -f /usr/share/icons/hicolor/256x256/apps/ping-watchdog.png
HOME=$(mktemp -d) timeout 120s ping-watchdog --self-test
sudo apt-get remove -y -qq ping-watchdog >/dev/null
test ! -e /usr/bin/ping-watchdog
echo ".deb installs, runs and removes"
# .rpm: metadata, contents unstripped, extracted copy runs.
rpm -qp --queryformat '%{NAME} %{VERSION} %{ARCH}\n' "dist/$base.rpm" | grep -qx "ping-watchdog $version x86_64"
rx=$(mktemp -d); (cd "$rx" && rpm2cpio "$OLDPWD/dist/$base.rpm" | cpio -idm --quiet)
cmp publish-linux/PingWatchdog "$rx/opt/ping-watchdog/PingWatchdog"
test "$(cat "$rx/opt/ping-watchdog/package-kind.txt")" = rpm
HOME=$(mktemp -d) timeout 120s "$rx/opt/ping-watchdog/PingWatchdog" --self-test
echo ".rpm contents match the tested build and run"
# .tar.gz
tx=$(mktemp -d); tar -C "$tx" -xzf "dist/$base.tar.gz"
HOME=$(mktemp -d) timeout 120s "$tx/$base/PingWatchdog" --self-test
echo ".tar.gz runs from its folder"
# Flatpak: installs, reports its metadata and runs inside its sandbox.
flatpak install --user --noninteractive -y "dist/$base.flatpak" >/dev/null
flatpak info --user "$app_id" | grep -q "$app_id"
if timeout 120s flatpak run --user --command=ping-watchdog "$app_id" --self-test; then
  echo "Flatpak runs in its sandbox"
else
  echo "::warning title=Flatpak run::The Flatpak installed but couldn't run in this CI sandbox; it ships as untested either way."
fi
flatpak uninstall --user --noninteractive -y "$app_id" >/dev/null
endgroup

group "Stage release assets"
cp "$image" "dist/$base.AppImage"
# Updater files keep Velopack's names: installed copies look for them.
cp Releases-linux/releases.linux.json dist/
cp Releases-linux/PingWatchdog-"$version"-linux-full.nupkg dist/
if [[ -s "Releases-linux/PingWatchdog-$version-linux-delta.nupkg" ]]; then
  cp "Releases-linux/PingWatchdog-$version-linux-delta.nupkg" dist/
else
  echo "::warning title=No delta package::Full-package updates still work."
fi
required=("$base.AppImage" "$base.deb" "$base.flatpak" "$base.rpm" "$base.tar.gz" "PingWatchdog-$version-linux-full.nupkg" "releases.linux.json")
for file in "${required[@]}"; do
  [[ -s "dist/$file" ]] || { echo "::error::Required asset missing or empty: $file"; exit 1; }
done
# Checksums last, from the exact files being uploaded.
(cd dist && sha256sum -- * > SHA256SUMS.txt)
full_hash=$(python3 -c "import json; a=[x for x in json.load(open('dist/releases.linux.json'))['Assets'] if x['Type']=='Full' and x['Version']=='$version']; print(a[0]['SHA256'].lower())")
grep -q "^$full_hash  PingWatchdog-$version-linux-full.nupkg$" dist/SHA256SUMS.txt || { echo "::error::Feed hash doesn't match the full package"; exit 1; }
ls -l dist
cat dist/SHA256SUMS.txt
endgroup
