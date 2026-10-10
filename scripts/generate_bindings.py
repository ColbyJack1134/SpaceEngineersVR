"""Derive motion-controller defaults from the shipped Touch action layout."""
import copy
import json
from pathlib import Path

ASSETS = Path(__file__).resolve().parents[1] / "SpaceEngineersVR/Assets/Controls"
TOUCH = json.loads((ASSETS / "binding_oculus_touch.json").read_text())
MANIFEST = json.loads((ASSETS / "actions.json").read_text())
NAMES = {a["name"].lower(): a["name"] for a in MANIFEST["actions"]}


def source(path, mode, inputs, parameters=None):
    result = {"path": path, "mode": mode,
              "inputs": {key: {"output": NAMES[value.lower()]} for key, value in inputs.items()}}
    if parameters:
        result["parameters"] = parameters
    return result


def profile(controller, name, paths=None, digital_grip=False, skeleton=True):
    result = copy.deepcopy(TOUCH)
    result["controller_type"] = controller
    result["name"] = "SEVR " + name
    result["description"] = "Default motion-controller bindings."
    for group in result["bindings"].values():
        if not skeleton:
            group.pop("skeleton", None)
        mapped = []
        for item in group.get("sources", []):
            old = item["path"]
            replacement = (paths or {}).get(old, old)
            if replacement is None:
                continue
            item["path"] = replacement
            for output in item["inputs"].values():
                output["output"] = NAMES[output["output"].lower()]
            if digital_grip and old.endswith("/input/grip") and item["mode"] == "trigger":
                item["mode"] = "scalar_constant"
                item["inputs"] = {"value": item["inputs"]["pull"]}
                item["parameters"] = {"on/x": "1", "off/x": "0"}
            mapped.append(item)
        group["sources"] = mapped
    return result


def dpad(path, inputs):
    return source(path, "dpad", inputs,
                  {"sub_mode": "click", "deadzone_pct": "50", "overlap_pct": "0", "sticky": "false"})


def save(result):
    for group in result["bindings"].values():
        merged = {}
        for item in group["sources"]:
            key = (item["path"], item["mode"], json.dumps(item.get("parameters", {}), sort_keys=True))
            if key in merged:
                assert not set(merged[key]["inputs"]) & set(item["inputs"]), key
                merged[key]["inputs"].update(item["inputs"])
            else:
                merged[key] = item
        group["sources"] = list(merged.values())
    path = ASSETS / ("binding_" + result["controller_type"] + ".json")
    path.write_text(json.dumps(result, indent=2) + "\n")


def terminal(result, path):
    result["bindings"]["/actions/common"]["sources"].append(
        source(path, "button", {"click": "/actions/common/in/Terminal"}))


def generate():
    left = "/user/hand/left/input/"
    right = "/user/hand/right/input/"
    index_paths = {left + "x": left + "a", left + "y": left + "b",
                   left + "joystick": left + "thumbstick", right + "joystick": right + "thumbstick"}
    index = profile("knuckles", "Index", index_paths)
    terminal(index, left + "trackpad")
    for group in index["bindings"].values():
        for item in group["sources"]:
            if item["path"].endswith("/input/grip"):
                if item["mode"] == "trigger":
                    item["mode"] = "force_sensor"
                    item["inputs"] = {"force": item["inputs"]["pull"]}
                else:
                    item["parameters"] = {"force_input": "force", "click_activate_threshold": "0.8",
                                          "click_deactivate_threshold": "0.7"}
            elif item["path"] == left + "trackpad":
                item["parameters"] = {"force_input": "force", "click_activate_threshold": "0.5",
                                      "click_deactivate_threshold": "0.4"}
    save(index)

    ev1_paths = {left + "x": left + "a", left + "y": left + "b",
                 left + "joystick": left + "trackpad", right + "joystick": right + "trackpad"}
    ev1 = profile("knuckles_ev1", "Knuckles EV1", ev1_paths)
    for group in ev1["bindings"].values():
        for item in group["sources"]:
            if item["mode"] == "joystick":
                item["mode"] = "trackpad"
    save(ev1)

    save(profile("hpmotioncontroller", "HP Reverb G2", skeleton=False))
    cosmos = profile("vive_cosmos_controller", "Vive Cosmos", digital_grip=True, skeleton=False)
    terminal(cosmos, left + "application_menu")
    save(cosmos)
    for controller, name in (("pico_controller", "PICO"), ("vive_focus3_controller", "Vive Focus")):
        save(profile(controller, name))

    frame_paths = {left + "x": left + "dpad_down", left + "y": left + "dpad_up",
                   left + "joystick": left + "thumbstick",
                   right + "joystick": right + "thumbstick"}
    frame = profile("frame_controller", "Steam Frame", frame_paths)
    terminal(frame, left + "view")
    frame["description"] = "Touch-style controls: left D-pad up is Y; left D-pad down is X."
    save(frame)

    wmr_paths = {left + "x": left + "trackpad", left + "y": left + "trackpad",
                 right + "a": right + "trackpad", right + "b": right + "trackpad"}
    wmr = profile("holographic_controller", "Windows Mixed Reality", wmr_paths, digital_grip=True, skeleton=False)
    terminal(wmr, left + "application_menu")
    for group in wmr["bindings"].values():
        for i, item in enumerate(group["sources"]):
            if item["path"].endswith("/input/trackpad"):
                action = item["inputs"]["click"]["output"].lower()
                direction = "north" if action.endswith(("/quickmenu", "/keyboardfallback", "/unequip")) else "south"
                group["sources"][i] = dpad(item["path"], {direction: next(iter(item["inputs"].values()))["output"]})
    save(wmr)

    vive_paths = {left + "x": None, right + "a": None, right + "joystick": right + "trackpad",
                  left + "joystick": left + "trackpad", left + "y": left + "application_menu",
                  right + "b": right + "application_menu"}
    vive = profile("vive_controller", "Vive wands", vive_paths, digital_grip=True)
    for group in vive["bindings"].values():
        mapped = []
        for item in group["sources"]:
            if item["path"].endswith("/input/trackpad"):
                if item["mode"] == "joystick":
                    item["mode"] = "trackpad"
                    click = item["inputs"].pop("click", None)
                    if click:
                        mapped.append(dpad(item["path"], {"center": click["output"]}))
                else:
                    item = dpad(item["path"], {"north": item["inputs"]["click"]["output"]})
            mapped.append(item)
        group["sources"] = mapped
    common = vive["bindings"]["/actions/common"]["sources"]
    common.append(dpad(left + "trackpad", {"south": "/actions/common/in/Jetpack", "north": "/actions/common/in/Terminal"}))
    common.append(dpad(right + "trackpad", {"center": "/actions/common/in/Interact"}))
    vive["description"] = "Touchpads move/select. Left Menu: actions. Right Menu: Back/toolbar. Pad center clicks: Jump/Use. Left pad down: X."
    save(vive)


if __name__ == "__main__":
    generate()
