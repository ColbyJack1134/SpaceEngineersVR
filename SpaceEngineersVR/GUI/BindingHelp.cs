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
            lines.Add("Quest defaults; active SteamVR origins are shown where available.");
            Add("Recenter / fit third-person view", "common/Recenter");
            Add("Tap: return / cancel. Hold: toolbar", "common/Unequip");
            Add("Tap in flight: dampeners. Hold: quick actions", "common/QuickMenu");
            Add("Use object / enter or exit cockpit", "common/Interact");
            Add("Jetpack on foot / typing in menus", "common/Jetpack");
            Add("Tool / place / menu click and drag", "common/Primary");
            Add("Secondary tool / right click", "common/Secondary");
            lines.Add("TOOLBAR: hold B, select with LEFT stick, release B to use. Center cancels.");
            lines.Add("Left trigger: previous page. Right trigger: next page. A: assign in G menu.");
            lines.Add("QUICK ACTIONS: hold Y, select with RIGHT stick, release Y to use.");
            lines.Add("After the Y ring opens, X opens inventory. B cancels the ring.");
            lines.Add("Building: Y opens variants first. Left/right triggers page to more variants and actions. Right grip + X changes size.");
            lines.Add("Tap B closes an open tablet. Search results stay on the tablet; its search field opens the movable physical keyboard.");
            lines.Add("Quick actions appear at the left hand; use the right stick. Tablet > Search lists all actions with search.");
            Add("Walk / turn", "walking/WalkLongitudinal");
            Add("Turn on foot", "walking/WalkRotate");
            Add("Jump / climb up", "walking/JumpOrClimbUp");
            Add("Crouch / climb down", "walking/CrouchOrClimbDown");
            Add("Flight translation", "flying/ThrustLRFB");
            Add("Pitch / yaw", "flying/ThrustRotate");
            Add("Rise", "flying/ThrustUp");
            Add("Descend", "flying/ThrustDown");
            lines.Add("In flight, right grip + right stick sideways rolls. Auto dampeners: right grip + tap Y while jetpacking, or quick actions.");
            lines.Add("Invert jetpack pitch in Character settings; invert ship pitch in Flight settings.");
            lines.Add("HELMET: left-temple trigger toggles light; left-temple grip toggles ship third person.");
            lines.Add("Right-temple trigger cycles HUD: off, vitals, markers, details. Grip opens/closes visor.");
            lines.Add("HUD & Interface: separate vitals/waypoints and show HUD with visor open.");
            lines.Add("THIRD PERSON: tap B to return. Quick actions cycle the camera mode.");
            lines.Add("Hold both grips: pan, zoom, rotate. Release one: pan with the remaining hand.");
            lines.Add("Moving releases glide; slow releases stop. Grip catches glide; fresh flight brakes it.");
            lines.Add("Release both grips and neutralize controls to resume flight. Recenter fits the ship.");
            lines.Add("Third person settings: pan/zoom/rotation sensitivity; Release glide has separate gains.");
            lines.Add("BUILDING: right trigger places. Hold right grip, then pull right trigger to remove.");
            lines.Add("While holding right grip: right stick yaw/pitch; left stick roll/distance.");
            lines.Add("Center sticks before adjusting; release/center again to resume moving.");
            lines.Add("Quick actions: alignment, placement mode, size, variants, palette and symmetry.");
            lines.Add("Blueprint preview: right trigger pastes; right grip or tap B cancels.");
            lines.Add("MENUS: point + right trigger clicks/drags. Right grip right-clicks; right stick scrolls.");
            lines.Add("Hold LEFT grip for Shift; LEFT trigger for Ctrl; combine with right-trigger click.");
            lines.Add("Use both for Shift+Ctrl. These modifiers also apply in the terminal/control panel.");
            lines.Add("X opens typing; Y brings the keyboard forward. B / Done closes typing.");
            lines.Add("Keyboard: tap keys, point + trigger, or select with right stick + A.");
            lines.Add("Hold the bar BELOW a window/keyboard to move it; bottom-right handle resizes.");
            lines.Add("G menu: drag items to slots. Use its page arrows; left-hand controls are modifiers.");
            lines.Add("TABLET: bring the right finger capsule near a button, then click the trigger.");
            lines.Add("Controls: direct actions. Toolbar: slots/pages. Search: all actions. Close: fold.");
            lines.Add("COCKPIT: bring a fingertip near a control, lightly squeeze trigger, then complete click.");
            lines.Add("Hold and move levers/covers. Release early to cancel; move well away to detach.");
            lines.Add("Hover a switch and tap B to assign it. Holding B still opens the toolbar.");
            lines.Add("Fighter: 41 switches and pull bar. Control Seat: four assignable keypad controls.");
            lines.Add("Grip physical sticks: RIGHT tilt pitch/roll, thumbstick yaw. LEFT tilt thrust, thumbstick lift.");
            lines.Add("Single-stick cockpits: stick rotates, right thumbstick yaws; controller translation stays.");
            lines.Add("Rover/buggy wheels and speeder handlebars are not active. Their seat panel works.");
            lines.Add("Each grab captures neutral. Flight options: physical sticks only in first person.");
            lines.Add("Seat panel padlock: unlock/move/lock stick placement; reset is available unlocked.");
            lines.Add("Rifle / launcher: hold left grip near foregrip for two-handed aiming.");
            lines.Add("Reload and developer tools are in Tablet > Search. Tools remain hand-tracked.");
            lines.Add("Release controls after menus, tracking loss, recentering or a changed context.");
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
            if (OpenVR.Input!=null && OpenVR.Input.GetActionSetHandle(setName, ref set) == EVRInputError.None &&
                OpenVR.Input.GetActionHandle(actionName, ref handle) == EVRInputError.None &&
                OpenVR.Input.GetActionOrigins(set, handle, origins) == EVRInputError.None)
                foreach (ulong origin in origins)
                {
                    if (origin == 0) continue;
                    var name = new StringBuilder(256);
                    if (OpenVR.Input.GetOriginLocalizedName(origin, name, 256, -1) == EVRInputError.None)
                        names.Add(name.ToString());
                }
            lines.Add(label + " : " + (names.Count == 0 ? "No binding reported" : string.Join(", ", names)));
        }

        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            AddCaption("Controller help " + (page + 1) + "/" + ((lines.Count + 7) / 8));
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
