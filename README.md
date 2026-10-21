<img width="1280" height="640" alt="Space Engineers VR plugin banner" src="https://github.com/user-attachments/assets/cc85a7b4-460d-445f-afbd-cc008968c509" />

[Trailer](https://youtu.be/UGdSjBTu-30) • [Raw Gameplay](https://www.youtube.com/watch?v=4BAt21If5LI)

## Space Engineers VR Plugin (SE1)

The complete VR experience for Space Engineers. 

Features:
- Every base game and DLC cockpit is interactive, including in-game flight sticks. Inspired by [VTOL VR](https://store.steampowered.com/app/667970/VTOL_VR/)
- Camera, turret and remote-grid views in picture-in-picture. Inspired by [CameraLCD](https://github.com/austinvaness/CameraLCD)
- Multiplayer: VR players see each other's heads, hands and fingers, and VR works on vanilla servers
- Tools and weapons with two-handed support. Properly zeroed iron sights and working holo sights
- VR building with painting, symmetry, blueprints, planets, and third-person/diorama building
- Third-person and spectator views with hand-controlled camera movement
- HUD redesigned for VR while maintaining base game art
- Wrist tablet, radial menus, floating game menus and a VR keyboard
- Stereo 3D rendering, 6DOF tracking, motion controls and roomscale

With the 1.0 release, I consider this experience to be practically complete and stable. Of course there will always be bugs, polish, and new ideas to work on. I plan on maintaining this project until at least past the Warfare 3 update. If you run into any bugs or problems feel free to create a new [issue](https://github.com/ColbyJack1134/SpaceEngineersVR/issues) on GitHub.

## Installation

**Note:** The plugin only runs when SteamVR is running as the game starts. Otherwise, Space Engineers starts normally.

1. Install [Pulsar](https://github.com/SpaceGT/Pulsar)
2. Start SteamVR, then launch Space Engineers with Pulsar's `Legacy.exe`.<img width="742" height="538" alt="image" src="https://github.com/user-attachments/assets/2d068b81-083e-49e2-97ad-ca39c8e8ed8d" />
4. Open **Plugins** from the main menu, search for `Space Engineers VR`, enable it and click **Apply**. Click **Yes** to restart.

<img width="1918" height="1079" alt="image" src="https://github.com/user-attachments/assets/b92509c4-ca99-4ba0-afd8-740c407980a7" />
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/ed48a74a-7838-4723-b58c-b64b1febf015" />
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/06bf98f2-a87e-4cc8-98d9-7fed04cad1dd" />
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/df6ee082-cdf9-4c58-9038-162072083209" />


Set **Model Quality** to **High** to avoid artifacts with your in-game hand model.

To install from a GitHub release or the optional flatscreen multiplayer companion, see [Installing manually from a release](INSTALL-MANUAL.md).

## Controls

[Controls guide](CONTROLS.md) • [Steam Frame controls guide](CONTROLS-FRAME.md). Also available in game.

Controls guide made for Quest 3. Touch controllers (All quests and Rift S) and Steam Frame controllers are fully supported.

Other bindings provided but untested:
- Index Controllers
- HTC Vive (pro, cosmos, focus)
- PICO controllers
- WMR controllers
- HP Reverb G2
- Legacy Knuckles

## Mod Support

Some mods may already work with no support required.

Supported:
- Interactive LCDs with [Arthur's Lcd Mod](https://steamcommunity.com/workshop/filedetails/?id=3730092649) and [TouchScreenAPI](https://steamcommunity.com/workshop/filedetails/?id=2668820525)
- [Terrain Tool](https://steamcommunity.com/sharedfiles/filedetails/?id=2820001381) and [Concrete Tool](https://steamcommunity.com/sharedfiles/filedetails/?id=396679430)
- [Improvised Experimentation](https://steamcommunity.com/workshop/filedetails/?id=2891367014) (formerly improvised engineering)
  - Grip plus trigger on a grid to hold it. With trigger held, rotate with the sticks, B to throw. Release trigger only to use the sticks to move around like normal.
- [WeaponCore](https://steamcommunity.com/workshop/filedetails/?id=3154371364) and [Defense Shields](https://steamcommunity.com/workshop/filedetails/?id=3154379105)
  - [Controls guide](CONTROLS.md) and guide in game with mod enabled show the relevant controls.

Planned (at some point):
- BuildVision
- BuildInfo

If you want to see specific mods, or are a mod developer that would like to add VR support, feel free to reach out!

### Recommended Mods

Mods that I think improve the VR experience:
- [HD LCD Displays](https://steamcommunity.com/sharedfiles/filedetails/?id=3735691793)
- [Screen Gunk Be Gone!](https://steamcommunity.com/sharedfiles/filedetails/?id=1289657379)

## Credit

Based on [SpaceEngineersVR](https://github.com/Math0424/SpaceEngineersVR) by Math0424 and contributors, licensed under GPL-2.0.
