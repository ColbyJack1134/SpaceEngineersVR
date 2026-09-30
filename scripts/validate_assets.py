"""Check the shipped Quest/Touch binding against the action manifest and C# consumers."""
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
assets = root / "SpaceEngineersVR/Assets/Controls"
manifest = json.loads((assets / "actions.json").read_text())
actions = {a["name"].lower() for a in manifest["actions"]}
sets = {s["name"].lower() for s in manifest["action_sets"]}
assert len(actions) == len(manifest["actions"]), "Duplicate actions"
for action in manifest["actions"]:
    direction = "/out/" if action["type"] == "vibration" else "/in/"
    assert direction in action["name"], f"Wrong action direction: {action['name']}"
for binding in manifest["default_bindings"]:
    assert (assets / binding["binding_url"]).is_file(), binding["binding_url"]

def check(node):
    if isinstance(node, dict):
        for key, value in node.items():
            if key == "output":
                assert value.lower() in actions, f"Unknown Quest binding output: {value}"
            check(value)
    elif isinstance(node, list):
        for item in node:
            check(item)

quest = json.loads((assets / "binding_oculus_touch.json").read_text())
assert quest["controller_type"] == "oculus_touch"
assert set(quest["bindings"]).issubset(sets)
check(quest)
for path in (root / "SpaceEngineersVR/Player").rglob("*.cs"):
    for name in re.findall(r'"(/actions/[^"\s]+/(?:in|out)/[^"\s]+)"', path.read_text(encoding="utf-8-sig")):
        assert name.lower() in actions, f"Undefined action in {path.name}: {name}"
print(f"PASS: {len(actions)} actions, {len(sets)} action sets, Quest/Touch bindings and C# consumers")
