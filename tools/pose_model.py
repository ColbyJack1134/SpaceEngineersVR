"""Pose an exported skeleton and optionally merge it into another model for inspection."""
import argparse
import copy
import fnmatch
import json
from pathlib import Path

import numpy as np


def read(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def pose_model(model, pose, root=None, attach=None):
    bones = model["Bones"]
    bind, posed, included = [], [], []
    for bone in bones:
        parent = bone["Parent"]
        local = np.array(bone["Transform"]).reshape(4, 4)
        bind.append(local @ bind[parent] if parent >= 0 else local)
        included.append(root is None or bone["Name"] == root or (parent >= 0 and included[parent]))
        absolute = pose.get("absolute", {}).get(bone["Name"])
        if absolute is not None:
            posed.append(np.array(absolute).reshape(4, 4))
            continue
        rotation = np.eye(4)
        for pattern, degrees in pose.get("rotate_z", {}).items():
            if fnmatch.fnmatchcase(bone["Name"], pattern):
                angle = np.radians(degrees)
                c, s = np.cos(angle), np.sin(angle)
                rotation = np.array([[c, s, 0, 0], [-s, c, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]])
        local = rotation @ local
        posed.append(local @ posed[parent] if parent >= 0 else local)
    if root and root not in {b["Name"] for b in bones}:
        raise ValueError(f"Bone not found: {root}")
    vertices = np.column_stack((np.array(model["Vertices"])[:, :3], np.ones(len(model["Vertices"]))))
    indices = np.array(model["BlendIndices"])
    weights = np.array(model["BlendWeights"])
    transforms = np.array([np.linalg.inv(a) @ b for a, b in zip(bind, posed)])
    skinned = np.einsum("ni,nkij,nk->nj", vertices, transforms[indices], weights)
    if not np.all(np.isfinite(skinned)):
        raise ValueError("Invalid skinned vertices")
    # Exclude seam vertices weighted to unposed forearm bones in isolated hand views.
    mask = np.sum(np.array(included)[indices] * weights, axis=1) > .999
    result = copy.deepcopy(attach) if attach else {"Vertices": [], "UV": [], "Parts": []}
    offset = len(result["Vertices"])
    result["Vertices"].extend(skinned[:, :3].tolist())
    result["UV"].extend(model["UV"])
    for part in model["Parts"]:
        faces = np.array(part["Indices"]).reshape(-1, 3)
        if root:
            faces = faces[np.all(mask[faces], axis=1)]
        if len(faces):
            new_part = copy.deepcopy(part)
            new_part["Indices"] = (faces + offset).flatten().tolist()
            result["Parts"].append(new_part)
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("model", type=Path)
    parser.add_argument("--pose", type=Path, required=True, help="JSON: absolute bone matrices and rotate_z pattern/degrees")
    parser.add_argument("--root", help="Include only triangles fully weighted to this bone and its descendants")
    parser.add_argument("--attach", type=Path, help="Merge with another exported model, e.g. a cockpit")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = pose_model(read(args.model), read(args.pose), args.root, read(args.attach) if args.attach else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result))
    print(args.output)
