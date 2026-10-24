<img width="1280" height="640" alt="Space Engineers VR plugin banner" src="https://github.com/user-attachments/assets/cc85a7b4-460d-445f-afbd-cc008968c509" />

[Trailer](https://youtu.be/UGdSjBTu-30) • [Raw Gameplay](https://www.youtube.com/watch?v=4BAt21If5LI)

## Space Engineers VR Plugin

The complete, native-feeling VR experience for Space Engineers.

Features:
- **Interactive cockpits** - every base game and DLC cockpit, with in-game flight sticks. Inspired by [VTOL VR](https://store.steampowered.com/app/667970/VTOL_VR/)
- **Tools and weapons** - two-handed holds and functional reticles
- **VR building** - painting, symmetry and blueprints
- **Third-person and spectator** - hand-controlled camera views
- **Camera, turret and remote-grid views** - picture-in-picture. Inspired by [CameraLCD](https://github.com/austinvaness/CameraLCD)
- **HUD built for VR** - maintaining base game art
- **Multiplayer** - including vanilla servers

Other features:
- Third-person diorama build mode
- Left-handed mode
- Wrist tablet, radial menus and VR keyboard
- [Mod API](MOD-API.md) and programmable block API
- Stereo 3D, 6DOF and roomscale (obviously)

With the 1.0 release, I consider this experience to be practically complete and stable. Of course there will always be bugs, polish, and new ideas to work on. I plan on maintaining this project until at least past the Warfare 3 update. If you run into any bugs or problems feel free to create a new [issue](https://github.com/ColbyJack1134/SpaceEngineersVR/issues) on GitHub.

## Installation

**Note:** The plugin only runs when SteamVR is running as the game starts. Otherwise, Space Engineers starts normally.

1. Install [Pulsar](https://github.com/SpaceGT/Pulsar)
2. Start SteamVR, then launch Space Engineers with Pulsar's `Legacy.exe` (or `Interim.exe`).<img width="742" height="538" alt="image" src="https://github.com/user-attachments/assets/2d068b81-083e-49e2-97ad-ca39c8e8ed8d" />
3. Open **Plugins** from the main menu, search for `Space Engineers VR`, enable it and click **Apply**. Click **Yes** to restart.

<img width="1918" height="1079" alt="image" src="https://github.com/user-attachments/assets/b92509c4-ca99-4ba0-afd8-740c407980a7" />
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/ed48a74a-7838-4723-b58c-b64b1febf015" />
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/06bf98f2-a87e-4cc8-98d9-7fed04cad1dd" />
<img width="1919" height="1079" alt="image" src="https://github.com/user-attachments/assets/df6ee082-cdf9-4c58-9038-162072083209" />


Set **Model Quality** to **High** to avoid artifacts with your in-game hand model.

To install from a GitHub release or the optional flatscreen multiplayer companion, see [Installing manually from a release](INSTALL-MANUAL.md).

### Linux (experimental)

Use Pulsar's `Interim.exe` and install your distribution's `libgdiplus` package. Untested.

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
- [Paint Gun](https://steamcommunity.com/sharedfiles/filedetails/?id=500818376)

Planned (at some point):
- BuildVision
- BuildInfo

If you want to see specific mods, feel free to reach out! Mod developers can add VR support with the [Mod API](MOD-API.md).

### Recommended Mods

Mods that I think improve the VR experience:
- [HD LCD Displays](https://steamcommunity.com/sharedfiles/filedetails/?id=3735691793)
- [Screen Gunk Be Gone!](https://steamcommunity.com/sharedfiles/filedetails/?id=1289657379)

## Credit

Based on [SpaceEngineersVR](https://github.com/Math0424/SpaceEngineersVR) by Math0424 and contributors, licensed under GPL-2.0.
