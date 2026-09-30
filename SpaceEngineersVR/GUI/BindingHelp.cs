using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using Valve.VR;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class BindingHelp : MyGuiScreenBase
    {
        private readonly List<string> lines = new List<string>();
        private int page;
        public override string GetFriendlyName() => "SEVR controller help";
        public BindingHelp() : base(new Vector2(0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.94f, 0.84f))
        {
            m_closeOnEsc = true; CloseButtonEnabled = true;
            var mode = InputRouter.Mode;
            lines.Add("Context: " + mode + ". Origins below come from your active SteamVR binding.");
            Add("Recenter", "common/Recenter");
            Add("Tap unequip / hold wheel; Back in menus", "common/Unequip");
            Add("Tool / place; click and drag in menus", "common/Primary");
            Add("Use; Enter in menus", "common/Interact");
            Add("Jetpack on engineer; seated inventory; keyboard in menus", "common/Jetpack");
            lines.Add("Right hand by right temple: trigger cycles HUD, grip opens/closes visor.");
            lines.Add("Visor open hides helmet HUD; closing restores your selected HUD mode.");
            lines.Add("Left wrist: tap the small display to unfold; touch icons or point + trigger.");
            lines.Add("Fighter/bridge chair: hold console arrows to fit the seat; centre arrow returns to neutral.");
            lines.Add("Light right-trigger squeeze shows the world aiming ray; release hides it. A still uses blocks.");
            if (InputRouter.Flying)
            {
                Add("Translate forward/back/sideways", "flying/ThrustLRFB");
                Add("Rise", "flying/ThrustUp"); Add("Descend", "flying/ThrustDown");
                Add("Pitch / yaw; modified horizontal input rolls", "flying/ThrustRotate");
                Add(Plugin.Common.Config.LegacyShipTilt ? "Roll modifier / legacy ship tilt stick" : "Roll modifier (stick horizontal)", "flying/ThrustRoll");
                Add("Dampeners", "flying/Dampener");
                Add("Terminal while seated", "flying/SeatTerminal");
                lines.Add("Fighter Cockpit: squeeze near the matching side stick to grab; release to stop.");
                lines.Add("RIGHT tilt: pitch/roll; twist: yaw. LEFT tilt: forward/strafe; twist: lift.");
                lines.Add("LEFT clockwise twist rises; counterclockwise descends. Arm position is not thrust.");
                lines.Add("Each grab captures neutral. Center/release button controls for flight fallback.");
                lines.Add("Flight > Physical sticks: response, deadzone and enable/disable.");
                lines.Add("Wheel > Actions > page 2 toggles primary/secondary trigger for flight tools.");
            }
            else
            {
                Add("Walk", "walking/WalkLongitudinal"); Add("Turn", "walking/WalkRotate");
                Add("Jump / climb up", "walking/JumpOrClimbUp");
                Add("Crouch / climb down", "walking/CrouchOrClimbDown");
                Add("Tool secondary; right click in menus", "common/Secondary");
            }
            lines.Add("Rifle / launcher: hold LEFT grip near the foregrip for two-handed aiming.");
            lines.Add("Release returns to one hand. Reload keeps hand tracking; grip owns crouch/down.");
            Add("Native menu scroll", "menu/Navigate");
            Add("Wheel sector selection (left stick)", "menu/Page");
            Add("Wheel next page", "menu/WheelNextPage");
            Add("Wheel previous page", "menu/WheelPreviousPage");
            Add("Open / reposition floating keyboard", "menu/KeyboardFallback");
            lines.Add("Wheel: hold B, select with LEFT stick; release B confirms, center cancels.");
            lines.Add("Wheel: left trigger / grip change page; right trigger changes category.");
            lines.Add("Empty hotbar slot: release B to assign. A overrides selection and opens G, even for occupied slots.");
            lines.Add("G menu: hold B until the wheel opens, then press A from any category. Drag onto the desired slot.");
            lines.Add("G menu: click search or press X to type; Y brings the keyboard back in front.");
            lines.Add("Keyboard: tap with the right controller tip, or point + trigger; right stick + A selects keys.");
            lines.Add("Hold trigger on the small bottom bar to move; bottom-right corner resizes. B / DONE closes.");
            lines.Add("Floating menus: hold trigger on the bar BELOW the window to move/rotate; lower-right corner resizes.");
            lines.Add("Recenter recovers the menu window. X/Y open the keyboard; layouts save when released.");
            lines.Add("Drag items to slots with right trigger. Left trigger/grip: next/previous toolbar page.");
            lines.Add($"Roll: jetpack {Plugin.Common.Config.JetpackRollSensitivity:P0}, ship {Plugin.Common.Config.ShipRollSensitivity:P0}. Adjust in VR Options > Flight.");
            lines.Add("Build wheel: all rotation axes, distance, sizes, variants, paint and symmetry.");
            lines.Add("Creative: select a block from the toolbar, aim at the block to remove, squeeze RIGHT grip.");
            lines.Add("Building grip removes even with jetpack on; it does not modify roll. Survival rules stay native.");
            lines.Add("Actions page 3 / Building page 3 > Blueprints opens the native library. X searches.");
            lines.Add("Select blueprint > To clipboard. Aim RIGHT hand; Build page 1 rotates / changes distance.");
            lines.Add("Preview: trigger pastes, RIGHT grip or tap B cancels. Release after menus before placing.");
            lines.Add("Fighter centre seat panel: green/amber padlock locks/unlocks sticks. Either hand can touch or point + trigger.");
            lines.Add("Padlock again locks/saves positions; the small lower arrow while unlocked resets both sticks.");
            lines.Add("Unlocked sticks suspend flight/shooting. Menus, tracking loss and recenter cancel unsaved moves.");
            lines.Add("Fighter: 13 switches with movable covers. Control Seat: four assignable keys.");
            lines.Add("When a free hand adopts the pointing pose near controls, lightly squeeze for the ray; fully squeeze to activate.");
            lines.Add("Tap B while a switch badge is showing to assign it; hold B still opens the wheel. Drag ship-toolbar actions into switch slots.");
            lines.Add("At the seat panel, trigger confirms the nearby fingertip highlight; farther away, use the ray.");
            lines.Add("Touch LCDs: enable TouchScreenAPI in a test world; choose VR Touch Test or VR Stopwatch in the LCD Script list.");
            lines.Add("Stow tools, then touch with the right fingertip or point + trigger. Grip supplies an app's secondary click.");
            lines.Add("A changed context requires release/neutral before a control can act again.");
            RecreateControls(true);
        }

        private void Add(string label, string action)
        {
            int slash = action.IndexOf('/');
            string setName = "/actions/" + action.Substring(0, slash);
            string actionName = setName + "/in/" + action.Substring(slash + 1);
            ulong set = 0, handle = 0;
            var origins = new ulong[16];
            var names = new List<string>();
            if (OpenVR.Input.GetActionSetHandle(setName, ref set) == EVRInputError.None &&
                OpenVR.Input.GetActionHandle(actionName, ref handle) == EVRInputError.None &&
                OpenVR.Input.GetActionOrigins(set, handle, origins) == EVRInputError.None)
                foreach (ulong origin in origins)
                {
                    if (origin == 0) continue;
                    var name = new StringBuilder(256);
                    if (OpenVR.Input.GetOriginLocalizedName(origin, name, 256, -1) == EVRInputError.None)
                        names.Add(name.ToString());
                }
            lines.Add(label + "  —  " + (names.Count == 0 ? "No binding reported" : string.Join(", ", names)));
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            AddCaption("Controller help — " + (page + 1) + "/" + ((lines.Count + 7) / 8));
            for (int i = page * 8; i < Math.Min(lines.Count, (page + 1) * 8); i++)
                Controls.Add(new MyGuiControlLabel(new Vector2(-0.43f, -0.27f + (i % 8) * 0.07f),
                    text: lines[i], textScale: 0.60f, originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(-0.25f, 0.32f), text: new StringBuilder("Previous"),
                onButtonClick: _ => { page = (page + (lines.Count + 7) / 8 - 1) % ((lines.Count + 7) / 8); RecreateControls(false); }));
            Controls.Add(new MyGuiControlButton(new Vector2(0, 0.32f), text: new StringBuilder("Done"), onButtonClick: _ => CloseScreen()));
            Controls.Add(new MyGuiControlButton(new Vector2(0.25f, 0.32f), text: new StringBuilder("Next"),
                onButtonClick: _ => { page = (page + 1) % ((lines.Count + 7) / 8); RecreateControls(false); }));
        }
    }
}
