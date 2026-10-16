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
        private static Controls instance;
        // Created on first use from Main.Initialize; eager construction would open SteamVR input in test hosts.
        public static Controls Static => instance ?? (instance=new Controls());

        public Button Recenter;
        public Button Options;
        public readonly Button MenuKeyboardFallback;
        public readonly Button QuickMenu,ActionsWheel,ToolbarWheel;
        public readonly Analog MenuNavigate;
        public readonly Analog MenuPage;
        public readonly Button WheelNextPage, WheelPreviousPage;

        // Walking
        public readonly Analog WalkLongitudinal;
        public readonly Analog WalkLatitudinal;

        public readonly Analog WalkRotate;

        public readonly Button JumpOrClimbUp;
        public readonly Button Sprint;

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

        // No left grip click action exists; it is derived from the pull.
        private readonly Button leftGripClick=new Button(0);
        internal Button GripClick(Controller hand) => hand==Player.HandR ? Secondary : leftGripClick;
        internal Button Fire => Handedness.Swapped ? LeftClick : Primary;
        internal Analog FirePressure => Handedness.Swapped ? LeftTriggerPressure : PointerPressure;
        internal Analog ActionGripPressure => Handedness.Swapped ? LeftGripPressure : RightGripPressure;
        internal Button ActionGripClick => GripClick(Handedness.ActionHand);
        internal Analog SupportTriggerPressure => Handedness.Swapped ? PointerPressure : LeftTriggerPressure;
        internal Analog SupportGripPressure => Handedness.Swapped ? RightGripPressure : LeftGripPressure;
        private Analog ThrustTrigger => Handedness.Swapped && !Handedness.SeatedBuilding ? PointerPressure : LeftTriggerPressure;
        internal void BlockTriggerRoles(Controller hand)
        {
            if(hand==Handedness.SupportHand) ThrustUp.BlockUntilRelease();
        }
        internal void BlockGripRoles(Controller hand)
        {
            GripClick(hand).BlockUntilRelease();
            if(hand==Handedness.ActionHand) ThrustRoll.BlockUntilRelease();
            else { ThrustDown.BlockUntilRelease(); Sprint.BlockUntilRelease(); }
        }

        public Controls()
        {
            var error = OpenVR.Input.SetActionManifestPath(Common.ActionJsonPath);
            if (error != EVRInputError.None)
                throw new System.InvalidOperationException("SteamVR action manifest failed: " + error);
            Logger.Info("SteamVR action manifest loaded: " + Common.ActionJsonPath);

            QuickMenu = new Button("/actions/common/in/QuickMenu");
            ActionsWheel = new Button("/actions/common/in/ActionsWheel");
            ToolbarWheel = new Button("/actions/common/in/ToolbarWheel");
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
            // The action path keeps its original name so existing user bindings stay valid; the action is labelled Sprint.
            Sprint = new Button("/actions/walking/in/CrouchOrClimbDown");
            ThrustLRUD = new Analog("/actions/flying/in/ThrustLRUD");
            ThrustLRFB = new Analog("/actions/flying/in/ThrustLRFB");
            ThrustUp = new Analog("/actions/flying/in/ThrustUp");
            ThrustDown = new Analog("/actions/flying/in/ThrustDown");
            ThrustForward = new Analog("/actions/flying/in/ThrustForward");
            ThrustBackward = new Analog("/actions/flying/in/ThrustBackward");
            ThrustRotate = new Analog("/actions/flying/in/ThrustRotate");
            ThrustRoll = new Button("/actions/flying/in/ThrustRoll");
            Dampener = new Button("/actions/common/in/Dampener");
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
            bool swapped=Handedness.Swapped;
            foreach (var button in buttons) if(!swapped || button!=ThrustRoll && button!=Sprint) button.Update();
            // Common pressure keeps the accepted trigger source alive while its flight action is inactive.
            var thrustTrigger=ThrustTrigger;
            foreach (var analog in analogs)
                if(!swapped || analog!=ThrustUp && analog!=ThrustDown) analog.Update(locomotion.Contains(analog) || analog==thrustTrigger ? continuingOrigins : null);
            leftGripClick.UpdateFrom(LeftGripPressure,.55f,.45f);
            if(swapped && Handedness.SeatedBuilding)
            {
                ThrustUp.AcceptSample(default(InputAnalogActionData_t)); ThrustDown.AcceptSample(default(InputAnalogActionData_t));
                ThrustRoll.AcceptSample(default(InputDigitalActionData_t)); Sprint.AcceptSample(default(InputDigitalActionData_t));
            }
            else if(swapped)
            {
                ThrustUp.UpdateFrom(PointerPressure,continuingOrigins); ThrustDown.UpdateFrom(RightGripPressure,continuingOrigins);
                ThrustRoll.UpdateFrom(leftGripClick); Sprint.UpdateFrom(Secondary);
            }
            WindowFocus.Update(this);
            Player.HandL?.Fingers.Update(Player.HandL,true);
            Player.HandR?.Fingers.Update(Player.HandR,false);
            // The transfer applies only to the first sample after a character-mode switch.
            continuingOrigins.Clear();
            if (Recenter.HasPressed) Player.Headset.RequestRecenter();
            if (Common.Config.DeveloperTools && ++diagnosticTicks % 600 == 1)
                Logger.Info("INPUT context=" + mode + "; walk=" + WalkLongitudinal.Active + "; flight=" + ThrustLRFB.Active);
        }

        public void BlockUntilRelease(bool continueLocomotion = false,bool continueFire = false,bool continueBuild = false)
        {
            continuingOrigins.Clear();
            if (continueLocomotion)
                CaptureLocomotionOrigins(continuingOrigins,locomotion,ThrustTrigger);
            Button fire=Fire,build=ActionGripClick; Analog pressure=FirePressure,modifier=ActionGripPressure;
            foreach (var button in buttons) if(button!=fire && !(continueBuild && button==build)) button.BlockUntilRelease();
            if(!(continueBuild && build==leftGripClick)) leftGripClick.BlockUntilRelease();
            foreach (var analog in analogs)
                if(analog!=pressure && analog!=modifier) analog.BlockUntilRelease();
            BlockFireInput(fire,pressure,modifier,continueFire);
        }
        internal static void CaptureLocomotionOrigins(ISet<ulong> origins,IEnumerable<Analog> movement,Analog thrustTrigger)
        {
            foreach(var analog in movement)
                if(analog.HeldOrigin!=OpenVR.k_ulInvalidInputValueHandle) origins.Add(analog.HeldOrigin);
            // ThrustUp is inactive while walking; common trigger pressure supplies its physical origin.
            if(thrustTrigger.HeldOrigin!=OpenVR.k_ulInvalidInputValueHandle) origins.Add(thrustTrigger.HeldOrigin);
        }
        internal static void BlockFireInput(Button trigger,Analog pressure,Analog modifier,bool continuing)
        {
            if(continuing) return;
            trigger.BlockUntilRelease(); pressure.BlockUntilRelease(); modifier.BlockUntilRelease();
        }
    }
}
