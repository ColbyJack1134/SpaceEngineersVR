using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Valve.VR;

// See:
// https://github.com/ValveSoftware/openvr/wiki/SteamVR-Input
// https://github.com/ValveSoftware/openvr/wiki/Action-manifest

namespace SpaceEngineersVR.Player
{

    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class Controls
    {
        public static Controls Static = new Controls();

        public Button Recenter;
        public Button Options;
        public readonly Button MenuKeyboardFallback;
        public readonly Button QuickMenu;
        public readonly Analog MenuNavigate;
        public readonly Analog MenuPage;
        public readonly Button WheelNextPage, WheelPreviousPage;

        // Walking
        public readonly Analog WalkLongitudinal;
        public readonly Analog WalkLatitudinal;

        public readonly Analog WalkRotate;

        public readonly Button JumpOrClimbUp;
        public readonly Button CrouchOrClimbDown;

        // Flying
        public readonly Analog ThrustLRUD;
        public readonly Analog ThrustLRFB;
        public readonly Analog ThrustUp;
        public readonly Analog ThrustDown;
        public readonly Analog ThrustForward;
        public readonly Analog ThrustBackward;
        public readonly Analog ThrustRotate;
        public readonly Button ThrustRoll;
        public readonly Button Dampener;
        public readonly Button FlightJump;

        // Tool
        public readonly Button Primary;
        public readonly Button LeftClick;
        internal TriggerClick Click(Controller hand) => hand==Player.HandR ? new TriggerClick(Primary,PointerPressure):new TriggerClick(LeftClick,LeftTriggerPressure);
        public readonly Analog PointerPressure;
        public readonly Analog LeftTriggerPressure;
        public readonly Analog LeftGripPressure,RightGripPressure;
        public readonly Button Secondary;
        public readonly Button Reload;
        public readonly Button Unequip;
        public readonly Button CutGrid;
        public readonly Button CopyGrid;
        public readonly Button PasteGrid;

        // System
        public readonly Button Interact;
        public readonly Button Helmet;
        public readonly Button Jetpack;
        public readonly Button Broadcasting;
        public readonly Button Park;
        public readonly Button Power;
        public readonly Button Lights;
        public readonly Button Respawn;
        public readonly Button ToggleSignals;

        // Placement
        public readonly Button ToggleSymmetry;
        public readonly Button SymmetrySetup;
        public readonly Button PlacementMode;
        public readonly Button CubeSize;

        // Wrist tablet
        public readonly Button Terminal;
        public readonly Button Inventory;
        public readonly Button ColorSelector;
        public readonly Button ColorPicker;
        public readonly Button BuildPlanner;
        public readonly Button ToolbarConfig;
        public readonly Button BlockSelector;
        public readonly Button Contract;
        public readonly Button Chat;

        // Game
        public readonly Button ToggleView;
        public readonly Button Pause;
        public readonly Button VoiceChat;
        public readonly Button SignalMode;
        public readonly Button SpectatorMode;
        public readonly Button Teleport;

        // Action sets
        private readonly ActionSets WalkingSets;
        private readonly ActionSets FlyingSets;
        private readonly ActionSets MenuSets;
        private readonly Button[] buttons;
        private readonly Analog[] analogs;
        private readonly HashSet<Analog> locomotion;
        private readonly HashSet<ulong> continuingOrigins = new HashSet<ulong>();
        private int diagnosticTicks;

        public Controls()
        {
            var error = OpenVR.Input.SetActionManifestPath(Common.ActionJsonPath);
            if (error != EVRInputError.None)
                throw new System.InvalidOperationException("SteamVR action manifest failed: " + error);
            Logger.Info("SteamVR action manifest loaded: " + Common.ActionJsonPath);

            QuickMenu = new Button("/actions/common/in/QuickMenu");
            MenuKeyboardFallback = new Button("/actions/menu/in/KeyboardFallback");
            MenuNavigate = new Analog("/actions/menu/in/Navigate");
            MenuPage = new Analog("/actions/menu/in/Page");
            WheelNextPage = new Button("/actions/menu/in/WheelNextPage");
            WheelPreviousPage = new Button("/actions/menu/in/WheelPreviousPage");
            Options = new Button("/actions/common/in/Options");
            Recenter = new Button("/actions/common/in/Recenter");
            WalkLongitudinal = new Analog("/actions/walking/in/WalkLongitudinal");
            WalkLatitudinal = new Analog("/actions/walking/in/WalkLatitudinal");
            WalkRotate = new Analog("/actions/walking/in/WalkRotate");

            JumpOrClimbUp = new Button("/actions/walking/in/JumpOrClimbUp");
            CrouchOrClimbDown = new Button("/actions/walking/in/CrouchOrClimbDown");
            ThrustLRUD = new Analog("/actions/flying/in/ThrustLRUD");
            ThrustLRFB = new Analog("/actions/flying/in/ThrustLRFB");
            ThrustUp = new Analog("/actions/flying/in/ThrustUp");
            ThrustDown = new Analog("/actions/flying/in/ThrustDown");
            ThrustForward = new Analog("/actions/flying/in/ThrustForward");
            ThrustBackward = new Analog("/actions/flying/in/ThrustBackward");
            ThrustRotate = new Analog("/actions/flying/in/ThrustRotate");
            ThrustRoll = new Button("/actions/flying/in/ThrustRoll");
            Dampener = new Button("/actions/flying/in/Dampener");
            FlightJump = new Button("/actions/flying/in/SeatTerminal");
            Primary = new Button("/actions/common/in/Primary");
            LeftClick = new Button("/actions/common/in/LeftClick");
            PointerPressure = new Analog("/actions/common/in/PointerPressure",.55f);
            LeftTriggerPressure = new Analog("/actions/common/in/LeftTriggerPressure",.55f);
            LeftGripPressure = new Analog("/actions/common/in/LeftGripPressure",InteractionInput.GripThreshold);
            RightGripPressure = new Analog("/actions/common/in/RightGripPressure",InteractionInput.GripThreshold);
            Secondary = new Button("/actions/common/in/Secondary");
            Reload = new Button("/actions/common/in/Reload");
            Unequip = new Button("/actions/common/in/Unequip");
            CutGrid = new Button("/actions/common/in/CutGrid");
            CopyGrid = new Button("/actions/common/in/CopyGrid");
            PasteGrid = new Button("/actions/common/in/PasteGrid");
            Interact = new Button("/actions/common/in/Interact");
            Helmet = new Button("/actions/common/in/Helmet");
            Jetpack = new Button("/actions/common/in/Jetpack");
            Broadcasting = new Button("/actions/common/in/Broadcasting");
            Park = new Button("/actions/common/in/Park");
            Power = new Button("/actions/common/in/Power");
            Lights = new Button("/actions/common/in/Lights");
            Respawn = new Button("/actions/common/in/Respawn");
            ToggleSignals = new Button("/actions/common/in/ToggleSignals");
            ToggleSymmetry = new Button("/actions/common/in/ToggleSymmetry");
            SymmetrySetup = new Button("/actions/common/in/SymmetrySetup");
            PlacementMode = new Button("/actions/common/in/PlacementMode");
            CubeSize = new Button("/actions/common/in/CubeSize");
            Terminal = new Button("/actions/common/in/Terminal");
            Inventory = new Button("/actions/common/in/Inventory");
            ColorSelector = new Button("/actions/common/in/ColorSelector");
            ColorPicker = new Button("/actions/common/in/ColorPicker");
            BuildPlanner = new Button("/actions/common/in/BuildPlanner");
            ToolbarConfig = new Button("/actions/common/in/ToolbarConfig");
            BlockSelector = new Button("/actions/common/in/BlockSelector");
            Contract = new Button("/actions/common/in/Contract");
            Chat = new Button("/actions/common/in/Chat");
            ToggleView = new Button("/actions/common/in/ToggleView");
            Pause = new Button("/actions/common/in/Pause");
            VoiceChat = new Button("/actions/common/in/VoiceChat");
            SignalMode = new Button("/actions/common/in/SignalMode");
            SpectatorMode = new Button("/actions/common/in/SpectatorMode");
            Teleport = new Button("/actions/common/in/Teleport");

            WalkingSets = new ActionSets("/actions/walking", "/actions/common", "/actions/feedback");
            FlyingSets = new ActionSets("/actions/flying", "/actions/common", "/actions/feedback");
            MenuSets = new ActionSets("/actions/menu", "/actions/common", "/actions/feedback");
            var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
            buttons = fields.Where(f => f.FieldType == typeof(Button)).Select(f => (Button)f.GetValue(this)).ToArray();
            analogs = fields.Where(f => f.FieldType == typeof(Analog)).Select(f => (Analog)f.GetValue(this)).ToArray();
            // Continuous movement includes turning as well as translation. Transfer
            // the shared rotation stick; each mode still decides which axes it uses.
            locomotion = new HashSet<Analog> { WalkLongitudinal, WalkLatitudinal, WalkRotate,
                ThrustLRUD, ThrustLRFB, ThrustUp, ThrustDown, ThrustForward, ThrustBackward, ThrustRotate };
        }

        public void Poll(InputMode mode)
        {
            if (mode == InputMode.Menu || mode == InputMode.Radial || mode == InputMode.Blocked) MenuSets.Update();
            else if (mode == InputMode.Jetpack || mode == InputMode.Piloting || InputRouter.Flying) FlyingSets.Update();
            else WalkingSets.Update();
            foreach (var button in buttons) button.Update();
            foreach (var analog in analogs) analog.Update(locomotion.Contains(analog) ? continuingOrigins : null);
            WindowFocus.Update(this);
            Player.HandL?.Fingers.Update(Player.HandL,true);
            Player.HandR?.Fingers.Update(Player.HandR,false);
            // The transfer applies only to the first sample after a character-mode switch.
            continuingOrigins.Clear();
            if (Recenter.HasPressed) Player.Headset.RequestRecenter();
            if (Common.Config.DeveloperTools && ++diagnosticTicks % 600 == 1)
                Logger.Info("INPUT context=" + mode + "; walk=" + WalkLongitudinal.Active + "; flight=" + ThrustLRFB.Active);
        }

        public void BlockUntilRelease(bool continueLocomotion = false,bool continueFire = false)
        {
            continuingOrigins.Clear();
            if (continueLocomotion)
                foreach (var analog in locomotion)
                    if (analog.HeldOrigin != OpenVR.k_ulInvalidInputValueHandle)
                        continuingOrigins.Add(analog.HeldOrigin);
            foreach (var button in buttons) if(button!=Primary) button.BlockUntilRelease();
            foreach (var analog in analogs)
                if(analog!=PointerPressure && analog!=RightGripPressure) analog.BlockUntilRelease();
            BlockFireInput(Primary,PointerPressure,RightGripPressure,continueFire);
        }
        internal static void BlockFireInput(Button trigger,Analog pressure,Analog modifier,bool continuing)
        {
            if(continuing) return;
            trigger.BlockUntilRelease(); pressure.BlockUntilRelease(); modifier.BlockUntilRelease();
        }
    }
}
