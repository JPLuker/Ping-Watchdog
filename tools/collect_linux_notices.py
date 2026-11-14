"""Collect attribution from the same restored packages used to publish the Linux build.

Usage: python3 tools/collect_linux_notices.py <publish-dir>/ThirdPartyNotices <deps.json>
Fails if a package in the build graph can't be found, so a release never ships without its notices.
"""
import json
import os
import re
import shutil
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
project = root / "linux/PingWatchdog.Linux"
assets = json.loads((project / "obj/project.assets.json").read_text(encoding="utf-8-sig"))
dest = Path(sys.argv[1])
deps = Path(sys.argv[2])
dest.mkdir(parents=True, exist_ok=True)
caches = [Path(folder) for folder in assets["packageFolders"]]
packages = {key: value["path"] for key, value in assets["libraries"].items() if value.get("type") == "package"}
runtime_keys = [key.removeprefix("runtimepack.") for key in
                json.loads(deps.read_text(encoding="utf-8-sig"))["libraries"]
                if key.startswith("runtimepack.")]
if not any(key.startswith("Microsoft.NETCore.App.Runtime.") for key in runtime_keys):
    raise ValueError("Missing the .NET runtime pack identity; publish self-contained for linux-x64 first.")
for key in runtime_keys:
    packages.setdefault(key, key.lower())

inventory = ["PING WATCHDOG — LINUX THIRD-PARTY SOFTWARE",
             "Package names and versions come from the restored build graph.",
             "See the adjacent files for upstream terms and notices.", ""]
upstream = root / "docs/licenses/Velopack-MIT.txt"
(dest / "upstream").mkdir(exist_ok=True)
shutil.copyfile(upstream, dest / "upstream" / upstream.name)
shutil.copyfile(root / "docs/licenses/Velopack-vendor-notices.md", dest / "upstream" / "Velopack-vendor-notices.md")
notice_name = re.compile(r"licen[cs]e|third.?party|thirdpartynotices|^notice|copyright|redist\.txt", re.I)
records = {}
dotnet_root = Path(os.environ.get("DOTNET_ROOT", "/usr/share/dotnet"))
for key, relative in sorted(packages.items(), key=lambda item: item[0].lower()):
    package, version = key.rsplit("/", 1)
    candidates = [cache / relative for cache in caches] + [cache / package.lower() / version for cache in caches]
    if key in runtime_keys:
        candidates.append(dotnet_root / "packs" / package / version)
    folder = next((candidate for candidate in candidates if candidate.is_dir()), None)
    if folder is None:
        raise FileNotFoundError(key)
    inventory.append(f"{package} {version}")
    inventory.append(f"https://www.nuget.org/packages/{package}/{version}")
    specs = list(folder.glob("*.nuspec"))
    declared = None
    term = "see package files"
    if specs:
        metadata = ET.parse(specs[0]).find("{*}metadata")
        for field in ("authors", "copyright", "license", "licenseUrl"):
            node = metadata.find("{*}" + field)
            if node is not None and node.text:
                inventory.append(f"{field}: {node.text.strip()}")
                if field == "license":
                    term = node.text.strip()
                if field == "license" and node.get("type") == "file":
                    declared = folder / node.text.strip()
                    if not declared.is_file():
                        raise FileNotFoundError(declared)
    files = [file for file in folder.rglob("*") if file.is_file()
             and notice_name.search(file.name)
             and file.suffix.lower() in ("", ".txt", ".md", ".rtf", ".html", ".htm", ".pdf", ".xml")]
    if declared:
        files.append(declared)
    for file in sorted(set(files)):
        target = dest / package / file.relative_to(folder)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(file, target)
        inventory.append("  " + target.relative_to(dest).as_posix())
    records[key] = len(set(files))
    print(f"Notice review | {key} | {term} | {records[key]} files")
    inventory.append("")
(dest / "INDEX.txt").write_text("\n".join(inventory), encoding="utf-8")
print("Packaged notices for " + str(len(records)) + " resolved packages including " + ", ".join(runtime_keys))
