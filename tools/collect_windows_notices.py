"""Collect attribution from the same restored packages used to publish Windows."""
import json
import os
import re
import shutil
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
assets = json.loads((root / "obj/project.assets.json").read_text(encoding="utf-8-sig"))
dest = Path(sys.argv[1])
dest.mkdir(parents=True, exist_ok=True)
caches = [Path(folder) for folder in assets["packageFolders"]]
packages = {key: value["path"] for key, value in assets["libraries"].items()
            if value.get("type") == "package"}
deps = root / "bin/Release/net8.0-windows10.0.19041.0/win-x64/PingWatchdog.deps.json"
runtime_keys = [key.removeprefix("runtimepack.") for key in
                json.loads(deps.read_text(encoding="utf-8-sig"))["libraries"]
                if key.startswith("runtimepack.")]
for prefix in ("Microsoft.NETCore.App.Runtime.", "Microsoft.WindowsDesktop.App.Runtime."):
    if not any(key.startswith(prefix) for key in runtime_keys):
        raise ValueError(f"Missing runtime pack identity for {prefix}")
for key in runtime_keys:
    packages.setdefault(key, key.lower())

inventory = ["PING WATCHDOG — WINDOWS THIRD-PARTY SOFTWARE",
             "Package names and versions come from the restored build graph.",
             "See the adjacent files for upstream terms and notices.", ""]
shutil.copytree(root / "docs/licenses", dest / "upstream", dirs_exist_ok=True)
notice_name = re.compile(r"licen[cs]e|third.?party|thirdpartynotices|^notice|copyright|redist\.txt", re.I)
records = {}
for key, relative in sorted(packages.items(), key=lambda item: item[0].lower()):
    package, version = key.rsplit("/", 1)
    candidates = [cache / relative for cache in caches]
    if key in runtime_keys:
        candidates.append(Path(os.environ["DOTNET_ROOT"]) / "packs" / package / version)
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
