"""Check controller defaults against action types, gameplay coverage and optional SteamVR profiles."""
import argparse
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[1]
assets = root / "SpaceEngineersVR/Assets/Controls"
manifest = json.loads((assets / "actions.json").read_text())
actions = {a["name"].lower(): a["type"] for a in manifest["actions"]}
sets = {s["name"].lower() for s in manifest["action_sets"]}
assert len(actions) == len(manifest["actions"]), "Duplicate actions"
for action in manifest["actions"]:
    direction = "/out/" if action["type"] == "vibration" else "/in/"
    assert direction in action["name"], f"Wrong action direction: {action['name']}"
controllers = [b["controller_type"] for b in manifest["default_bindings"]]
assert len(controllers) == len(set(controllers)), "Duplicate controller defaults"

mode_types = {
    "button": {"click": "boolean", "touch": "boolean"},
    "trigger": {"pull": "vector1", "click": "boolean", "touch": "boolean"},
    "force_sensor": {"force": "vector1"},
    "scalar_constant": {"value": "vector1"},
    "joystick": {"position": "vector2", "click": "boolean"},
    "trackpad": {"position": "vector2", "click": "boolean", "touch": "boolean"},
    "dpad": {key: "boolean" for key in ("north", "south", "east", "west", "center")},
}
required = {
    "/actions/common": ("Primary", "LeftClick", "Secondary", "Interact", "Unequip", "Jetpack", "Recenter",
                        "QuickMenu", "PointerPressure", "LeftTriggerPressure", "LeftGripPressure", "RightGripPressure"),
    "/actions/walking": ("WalkLongitudinal", "WalkRotate", "JumpOrClimbUp", "CrouchOrClimbDown"),
    "/actions/flying": ("ThrustLRFB", "ThrustRotate", "ThrustUp", "ThrustDown", "ThrustRoll", "SeatTerminal"),
    "/actions/menu": ("Navigate", "Page", "KeyboardFallback", "WheelNextPage", "WheelPreviousPage"),
}

def check(node, name):
    if isinstance(node, dict):
        for key, value in node.items():
            if key == "output":
                assert value.lower() in actions, f"Unknown binding output in {name}: {value}"
            check(value, name)
    elif isinstance(node, list):
        for item in node:
            check(item, name)


def validate(binding, name):
    assert set(binding["bindings"]) == sets, f"Missing action sets in {name}"
    check(binding, name)
    for action_set, group in binding["bindings"].items():
        outputs = set()
        for item in group.get("sources", []):
            mode = item["mode"]
            assert mode in mode_types, f"Unknown mode {mode} in {name}"
            assert item["inputs"], f"Empty input source in {name}"
            for key, value in item["inputs"].items():
                action = value["output"].lower()
                assert key in mode_types[mode], f"Invalid {mode}/{key} in {name}"
                assert actions[action] == mode_types[mode][key], f"Wrong output type for {action} in {name}"
                assert action.startswith(action_set + "/in/"), f"Wrong action set for {action} in {name}"
                outputs.add(action)
            if mode == "dpad":
                assert item["parameters"]["sub_mode"] == "click", f"Touch activates buttons in {name}"
        for action in required.get(action_set, ()):
            assert (action_set + "/in/" + action).lower() in outputs, f"Missing {action} in {name}"
        for kind, action_type in (("skeleton", "skeleton"), ("haptics", "vibration")):
            for item in group.get(kind, []):
                assert actions[item["output"].lower()] == action_type, f"Wrong {kind} output in {name}"
        if action_set == "/actions/feedback":
            haptics = {(item["path"], item["output"].lower()) for item in group.get("haptics", [])}
            for hand in ("left", "right"):
                assert (f"/user/hand/{hand}/output/haptic", f"/actions/feedback/out/{hand}haptic") in haptics, name
    common = binding["bindings"]["/actions/common"]["sources"]
    for hand, action in (("right", "primary"), ("left", "leftclick")):
        item = next(s for s in common if s.get("inputs", {}).get("click", {}).get("output", "").lower() == "/actions/common/in/" + action)
        assert item["path"] == f"/user/hand/{hand}/input/trigger", name
        assert item["parameters"] == {"click_activate_threshold": "0.25", "click_deactivate_threshold": "0.20"}, name
    for item in common:
        for value in item["inputs"].values():
            if value["output"].lower().endswith("/quickmenu"):
                assert item["path"].startswith("/user/hand/left/"), f"Action wheel moved off left hand in {name}"
    for hand in ("left", "right"):
        action = f"/actions/common/in/{hand}grippressure"
        grip = next(s for s in common if any(v["output"].lower() == action for v in s["inputs"].values()))
        assert grip["path"] == f"/user/hand/{hand}/input/grip", f"Wrong grip hand in {name}"
        if grip["mode"] == "scalar_constant":
            assert float(grip["parameters"]["on/x"]) == 1 and float(grip["parameters"]["off/x"]) == 0, name
    for hand, action_set, action in (("left", "/actions/walking", "WalkLongitudinal"),
                                   ("right", "/actions/walking", "WalkRotate"),
                                   ("left", "/actions/flying", "ThrustLRFB"),
                                   ("right", "/actions/flying", "ThrustRotate"),
                                   ("left", "/actions/menu", "Page"),
                                   ("right", "/actions/menu", "Navigate")):
        sources = binding["bindings"][action_set]["sources"]
        axis = next(s for s in sources if any(v["output"].lower() == (action_set + "/in/" + action).lower()
                                            for v in s["inputs"].values()))
        assert axis["path"].startswith(f"/user/hand/{hand}/"), f"Wrong axis hand in {name}: {action}"
    # A held wheel must survive the gameplay-to-menu action-set transition.
    menu = binding["bindings"]["/actions/menu"]["sources"]
    common_outputs = {v["output"].lower() for s in common for v in s["inputs"].values()}
    assert "/actions/common/in/jetpack" in common_outputs, f"Inventory chord unavailable in {name}"
    wheel = {(s["path"], key) for s in common for key, v in s["inputs"].items()
             if v["output"].lower().endswith("/quickmenu")}
    keyboard = {(s["path"], key) for s in menu for key, v in s["inputs"].items()
                if v["output"].lower().endswith("/keyboardfallback")}
    assert wheel == keyboard, f"Wheel/menu button differs in {name}"


def validate_device(binding, profile, name):
    inputs = profile["input_source"]
    for group in binding["bindings"].values():
        for kind in ("sources", "skeleton", "haptics"):
            for item in group.get(kind, []):
                match = re.fullmatch(r"/user/hand/(left|right)(/.*)", item["path"])
                assert match, f"Invalid device path in {name}: {item['path']}"
                hand, path = match.groups()
                assert path in inputs, f"Unsupported device input in {name}: {path}"
                capabilities = inputs[path]
                assert capabilities.get("side", hand) == hand, f"Wrong input hand in {name}: {path}"
                if kind != "sources":
                    assert capabilities["type"] == ("skeleton" if kind == "skeleton" else "vibration"), name
                    continue
                mode = item["mode"]
                device_type = capabilities["type"]
                expected = {"trigger": ("trigger",), "force_sensor": ("trigger", "trackpad"),
                            "joystick": ("joystick",), "trackpad": ("trackpad",), "dpad": ("trackpad", "joystick"),
                            "scalar_constant": ("button",), "button": ("button", "trigger", "trackpad", "joystick")}
                assert device_type in expected[mode], f"Unsupported {mode} on {path} in {name}"
                if mode == "force_sensor" or item.get("parameters", {}).get("force_input") == "force":
                    assert capabilities.get("force", False), f"No force sensor on {path} in {name}"


parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--steamvr", type=Path, help="Installed SteamVR directory for device-path checks")
args = parser.parse_args()
device_profiles = {}
if args.steamvr:
    for path in args.steamvr.glob("drivers/**/input/*profile*.json"):
        data = json.loads(path.read_text())
        if "controller_type" in data and "input_source" in data:
            device_profiles[data["controller_type"]] = data
checked_devices = []
for default in manifest["default_bindings"]:
    path = assets / default["binding_url"]
    binding = json.loads(path.read_text())
    assert binding["controller_type"] == default["controller_type"], path.name
    validate(binding, path.name)
    if binding["controller_type"] in device_profiles:
        validate_device(binding, device_profiles[binding["controller_type"]], path.name)
        checked_devices.append(binding["controller_type"])

quest = json.loads((assets / "binding_oculus_touch.json").read_text())
assert quest["controller_type"] == "oculus_touch"
for path in (root / "SpaceEngineersVR/Player").rglob("*.cs"):
    for name in re.findall(r'"(/actions/[^"\s]+/(?:in|out)/[^"\s]+)"', path.read_text(encoding="utf-8-sig")):
        assert name.lower() in actions, f"Undefined action in {path.name}: {name}"
print(f"PASS: {len(actions)} actions, {len(sets)} action sets, {len(controllers)} controller defaults and C# consumers")
print("PASS: action types, gameplay/menu coverage, left action wheel, trigger thresholds and haptics")
if args.steamvr:
    assert checked_devices, "No matching SteamVR device profiles found"
    print("PASS: installed SteamVR input paths and capabilities for " + ", ".join(checked_devices))
    missing = set(controllers) - set(checked_devices)
    if missing:
        print("Device profiles unavailable locally: " + ", ".join(sorted(missing)))
