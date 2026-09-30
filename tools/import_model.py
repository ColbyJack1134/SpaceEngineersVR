"""Run in Blender: set MODEL to an exported JSON file, then Run Script."""
import json
from pathlib import Path

import bpy
from mathutils import Matrix

MODEL = ""  # Path to the JSON produced by Inspect-Model.ps1.


def matrix(values):
    # VRage uses row vectors; Blender uses column vectors.
    return Matrix([values[i:i + 4] for i in range(0, 16, 4)]).transposed()


def load_model(path):
    data = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    collection = bpy.data.collections.new(Path(data["Model"]).stem)
    bpy.context.scene.collection.children.link(collection)
    root = bpy.data.objects.new("Model origin (meters)", None)
    root.empty_display_type = "ARROWS"
    root.empty_display_size = 0.15
    root["source"] = data["Model"]
    root["sha256"] = data["Sha256"]
    root["authoring_rescale_factor"] = data["RescaleFactor"]
    collection.objects.link(root)

    for part in data["Parts"]:
        mesh = bpy.data.meshes.new(part["Material"])
        ids = part["Indices"]
        mesh.from_pydata(data["Vertices"], [], [ids[i:i + 3] for i in range(0, len(ids), 3)])
        mesh.update()
        obj = bpy.data.objects.new(part["Material"], mesh)
        collection.objects.link(obj)
        obj.parent = root
        if len(data["UV"]) == len(data["Vertices"]):
            uv = mesh.uv_layers.new()
            for loop in mesh.loops:
                u, v = data["UV"][loop.vertex_index]
                uv.data[loop.index].uv = (u, 1 - v)

    bones = []
    for bone in data["Bones"]:
        obj = bpy.data.objects.new("Bone: " + bone["Name"], None)
        obj.empty_display_type = "ARROWS"
        obj.empty_display_size = 0.035
        collection.objects.link(obj)
        bones.append(obj)
    for obj, bone in zip(bones, data["Bones"]):
        obj.parent = bones[bone["Parent"]] if bone["Parent"] >= 0 else root
        obj.matrix_local = matrix(bone["Transform"])

    for dummy in data["Dummies"]:
        obj = bpy.data.objects.new("Dummy: " + dummy["Name"], None)
        obj.empty_display_type = "ARROWS"
        obj.empty_display_size = 0.1
        collection.objects.link(obj)
        obj.parent = root
        obj.matrix_local = matrix(dummy["Transform"])
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    return root


if __name__ == "__main__":
    if not MODEL:
        raise ValueError("Set MODEL to an exported JSON file first.")
    load_model(MODEL)
