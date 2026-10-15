<img width="1280" height="640" alt="SEVR-social-3" src="https://github.com/user-attachments/assets/cc85a7b4-460d-445f-afbd-cc008968c509" />

[Trailer](https://youtu.be/UGdSjBTu-30) • [Raw Gameplay](https://www.youtube.com/watch?v=4BAt21If5LI)

## Space Engineers 1 VR Plugin

Nearly at a 1.0 release with some polish and bugfixes remaining. The goal of this plugin is to be able to fully play Space Engineers in VR with [VTOL VR](https://store.steampowered.com/app/667970/VTOL_VR/) inspired interactable cockpits and in-game joysticks.

Features:
- Stereo VR rendering, 6DOF tracking, motion controls, and roomscale
- Interactive vanilla and DLC cockpits using existing model controls (Up to Prosperity update)
- Grab and operate in-game joysticks, steering wheels, handlebars, and throttles with motion controllers
- Player tools and weapons with two-handed support
- VR building with support for painting, symmetry, blueprints, planets, and 3rd person building
- 3rd person and spectator views with hand-controlled camera movement
- Wrist HUD and tablet, radial menus, floating game menus, and VR keyboard
- Interactive LCDs, including Arthur and TouchScreenAPI support
- Camera, turret, and remote-grid VR support using picture-in-picture
- Multiplayer support with an optional flatscreen host companion

If you run into any bugs or problems feel free to create a new [issue](https://github.com/ColbyJack1134/SpaceEngineersVR/issues) on GitHub.

## Installation:

1. Install [Pulsar](https://github.com/SpaceGT/Pulsar).
3. Download `SpaceEngineersVR-vXXXX.zip` from the [latest release](https://github.com/ColbyJack1134/SpaceEngineersVR/releases/latest).
4. Extract the ZIP into Pulsar's `Legacy\Local` folder. Launch `Legacy.exe` at least once to create this folder.
5. Launch `Legacy.exe`, go to `Plugins`, click **+** to Add Plugin, search for **SpaceEngineersVR**, and click the checkbox to enable
<img width="1243" height="636" alt="image" src="https://github.com/user-attachments/assets/c7d39a03-e121-4512-a78a-fbfd957359cf" />

5. Don't forget to click **Apply**!
6. Launch Pulsar Legacy again with SteamVR running.

**Note**: If you extract the ZIP and it fails to load via Pulsar, it may be blocked. Try: right click each DLL -> Properties -> Unblock

Set **Model Quality** to **High** to avoid artifacts with your in-game hand model.

Flatscreen multiplayer hosts and clients can install the optional `SpaceEngineersVR.FlatscreenCompanion.dll` plugin.

## Controls

[Controls guide](CONTROLS.md). Also available in game.

Controls guide made for Quest 3. Touch controllers (All quests and Rift S) are fully supported. Full Steam Frame support coming soon once mine arrives.

Other bindings provided but untested:
- Index Controllers
- HTC Vive (pro, cosmos, focus)
- PICO controllers
- WMR controllers
- HP Reverb G2
- Legacy Knuckles
- Steam Frame Controllers

Based on [SpaceEngineersVR](https://github.com/Math0424/SpaceEngineersVR) by Math0424 and contributors, licensed under GPL-2.0.
