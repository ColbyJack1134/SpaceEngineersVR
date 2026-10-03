#!/usr/bin/env python3
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile


ROOT = Path(__file__).resolve().parent.parent
RUNTIME = (
    "SpaceEngineersVR.dll", "SpaceEngineersVR.dll.config", "0Harmony.dll",
    "OVRSharp.dll", "openvr_api.dll", "System.Drawing.Common.dll", "System.Numerics.Vectors.dll",
)
LICENSES = {
    "Lib.Harmony": ("LICENSE",),
    "OVRSharp": ("LICENSE",),
    "System.Drawing.Common": ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"),
    "System.Numerics.Vectors": ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"),
}


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def digest(data):
    return hashlib.sha256(data).hexdigest()


def archive(path, files):
    hashes = {}
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as output:
        for name, source in sorted(files.items()):
            data = source.read_bytes() if isinstance(source, Path) else source
            output.writestr(name, data)
            hashes[name] = digest(data)
    with zipfile.ZipFile(path) as check:
        if check.testzip() is not None:
            raise RuntimeError(f"Corrupt archive: {path}")
        for name, expected in hashes.items():
            if digest(check.read(name)) != expected:
                raise RuntimeError(f"Archive mismatch: {name}")
    return {"file": path.name, "sha256": digest(path.read_bytes()), "files": hashes}


def main():
    parser = argparse.ArgumentParser(description="Package a built plugin and its matching source locally.")
    parser.add_argument("--version", required=True)
    parser.add_argument("--multiplayer", action="store_true", help="Also package the standalone flatscreen/host companion.")
    parser.add_argument("--configuration", choices=("Debug", "Release"), default="Release")
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".tools/releases")
    args = parser.parse_args()
    if not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", args.version):
        parser.error("Version must be a filename-safe label.")
    build = ROOT / "SpaceEngineersVR/bin" / args.configuration / "net48"
    paths = sorted({p for p in git("ls-files", "-c", "-o", "--exclude-standard", "-z").decode().split("\0") if p and (ROOT / p).is_file()})
    files = {"SpaceEngineersVR/" + name: build / name for name in RUNTIME}
    for path in paths:
        if path.startswith("SpaceEngineersVR/Assets/"):
            relative = "SEVRAssets/" + path.removeprefix("SpaceEngineersVR/Assets/")
            files["SpaceEngineersVR/" + relative] = build / relative
    for name in ("LICENSE", "NOTICE"):
        files[name] = ROOT / name
    files["licenses/OpenVR-LICENSE"] = ROOT / "licenses/OpenVR-LICENSE"
    assets = json.loads((ROOT / "SpaceEngineersVR/obj/project.assets.json").read_text())
    for package, names in LICENSES.items():
        library = next(value for key, value in assets["libraries"].items() if key.split("/")[0] == package)
        folder = next(Path(root) / library["path"] for root in assets["packageFolders"] if (Path(root) / library["path"]).is_dir())
        for name in names:
            files[f"licenses/{package}/{name}"] = folder / name
    for path in files.values():
        if not path.is_file():
            raise FileNotFoundError(f"Missing package input: {path}")
    metadata = {
        "version": args.version, "configuration": args.configuration,
        "commit": git("rev-parse", "HEAD").decode().strip(),
        "uncommitted_changes": bool(git("status", "--porcelain")),
        "plugin_sha256": digest((build / "SpaceEngineersVR.dll").read_bytes()),
    }
    files["build.json"] = (json.dumps(metadata, indent=2) + "\n").encode()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    stem = "SpaceEngineersVR-" + args.version
    binary = archive(args.output_dir / (stem + ".zip"), files)
    source = archive(args.output_dir / (stem + "-source.zip"), {"SpaceEngineersVR/" + p: ROOT / p for p in paths})
    archives = [binary, source]
    if args.multiplayer:
        companion = ROOT / "SpaceEngineersVR.Multiplayer/bin" / args.configuration / "net48"
        companion_files = {"SpaceEngineersVR.Multiplayer/" + name: companion / name
                           for name in ("SpaceEngineersVR.Multiplayer.dll", "0Harmony.dll")}
        companion_files.update({name: files[name] for name in ("LICENSE", "NOTICE", "licenses/Lib.Harmony/LICENSE")})
        companion_metadata = dict(metadata, plugin_sha256=digest((companion / "SpaceEngineersVR.Multiplayer.dll").read_bytes()))
        companion_files["build.json"] = (json.dumps(companion_metadata, indent=2) + "\n").encode()
        archives.append(archive(args.output_dir / ("SpaceEngineersVR.Multiplayer-" + args.version + ".zip"), companion_files))
    manifest = {"build": metadata, "archives": archives}
    (args.output_dir / (stem + "-manifest.json")).write_text(json.dumps(manifest, indent=2) + "\n")
    (args.output_dir / (stem + ".sha256")).write_text("".join(f"{a['sha256']}  {a['file']}\n" for a in archives))
    for item in archives:
        print(f"Verified {item['file']}: {len(item['files'])} files, SHA256 {item['sha256']}")


if __name__ == "__main__":
    main()
