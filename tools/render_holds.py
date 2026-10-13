"""Render --hold-preview pose exports: installed astronaut arms posed by the plugin with the held item."""
import argparse
import copy
import json
import subprocess
import sys
from pathlib import Path

import numpy as np

TOOLS = Path(__file__).resolve().parent


def merged(astronaut, pose, models):
    bones = astronaut["Bones"]
    bind = []
    for bone in bones:
        local = np.array(bone["Transform"]).reshape(4, 4)
        bind.append(local @ bind[bone["Parent"]] if bone["Parent"] >= 0 else local)
    posed = [np.array(pose["absolute"][bone["Name"]]).reshape(4, 4) for bone in bones]
    included = np.zeros(len(bones), bool)
    for i, bone in enumerate(bones):
        included[i] = bone["Name"] in pose["roots"] or (bone["Parent"] >= 0 and included[bone["Parent"]])
    vertices = np.column_stack((np.array(astronaut["Vertices"])[:, :3], np.ones(len(astronaut["Vertices"]))))
    indices = np.array(astronaut["BlendIndices"])
    weights = np.array(astronaut["BlendWeights"])
    transforms = np.array([np.linalg.inv(a) @ b for a, b in zip(bind, posed)])
    skinned = np.einsum("ni,nkij,nk->nj", vertices, transforms[indices], weights)
    mask = np.sum(included[indices] * weights, axis=1) > .999
    item = json.loads((models / f"{pose['item']}.json").read_text(encoding="utf-8-sig"))
    model = np.array(pose["model"]).reshape(4, 4)
    positions = np.array(item["Vertices"])[:, :3]
    item["Vertices"] = (np.column_stack((positions, np.ones(len(positions)))) @ model)[:, :3].tolist()
    result = copy.deepcopy(item)
    offset = len(result["Vertices"])
    result["Vertices"].extend(skinned[:, :3].tolist())
    result["UV"].extend(astronaut["UV"])
    for part in astronaut["Parts"]:
        faces = np.array(part["Indices"]).reshape(-1, 3)
        faces = faces[np.all(mask[faces], axis=1)]
        if len(faces):
            copied = copy.deepcopy(part)
            copied["Indices"] = (faces + offset).flatten().tolist()
            result["Parts"].append(copied)
    return result, model[3, :3]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("poses", type=Path, help="--hold-preview output directory")
    parser.add_argument("output", type=Path)
    parser.add_argument("--content", type=Path, required=True)
    parser.add_argument("--models", type=Path, default=TOOLS.parent / ".tools" / "models", help="Inspect-Model.ps1 exports")
    parser.add_argument("--glob", default="*.json")
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    astronaut = json.loads((args.models / "SE_astronaut.json").read_text(encoding="utf-8-sig"))
    head = np.array([0, 1.62, .02])
    for path in sorted(args.poses.glob(args.glob)):
        pose = json.loads(path.read_text(encoding="utf-8-sig"))
        scene, item = merged(astronaut, pose, args.models)
        scene_path = args.output / f"{path.stem}.scene.json"
        scene_path.write_text(json.dumps(scene))
        side = -1 if "-left-" in path.stem else 1
        views = {"first": head, "close": item + np.array([.32 * side, .16, .30])}
        for name, eye in views.items():
            subprocess.run([sys.executable, str(TOOLS / "render_model.py"), str(scene_path), "--content", str(args.content),
                            "--eye", *map(str, eye), "--target", *map(str, item), "--fov", "55" if name == "first" else "45",
                            "--output", str(args.output / f"{path.stem}-{name}.png"), "--title", path.stem, "--subtitle", name + " view"], check=True)
        scene_path.unlink()
