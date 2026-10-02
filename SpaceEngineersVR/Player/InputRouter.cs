using Sandbox;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;

namespace SpaceEngineersVR.Player
{
    public enum InputMode { Blocked, Menu, Walking, Building, Jetpack, Piloting, Radial, Clipboard, Turret }

    public static class InputRouter
    {
        private static object owner;
        private static bool wasFlying;
        public static InputMode Mode { get; private set; } = InputMode.Blocked;
        public static bool RadialOpen { get; set; }
        public static bool CockpitInteraction => Mode==InputMode.Piloting || Mode==InputMode.Turret && RemoteView.HomeSeat!=null;
        public static bool Flying => RemoteView.Turret || MySession.Static?.ControlledEntity is MyShipController ||
            (MySession.Static?.LocalCharacter is IMyCharacter character && character.EnabledThrusts);
        public static bool Gameplay => Mode == InputMode.Walking || Mode == InputMode.Building ||
            Mode == InputMode.Jetpack || Mode == InputMode.Piloting || Mode == InputMode.Turret || Mode == InputMode.Clipboard;
        internal static bool TrackedItems => AllowsTrackedItems(Mode,Main.MenuOpen);
        internal static bool AllowsTrackedItems(InputMode mode,bool menuOpen) => !menuOpen &&
            (mode==InputMode.Walking || mode==InputMode.Building || mode==InputMode.Jetpack ||
             mode==InputMode.Clipboard || mode==InputMode.Radial);

        public static void Update()
        {
            var character = MySession.Static?.LocalCharacter;
            object nextOwner = Main.MenuOpen ? (object)VRGUIManager.TopScreen : MySession.Static?.ControlledEntity;
            InputMode next;
            if (!Player.Headset.pose.isTracked || !MenuPointer.GameFocused ||
                !Valve.VR.OpenVR.System.IsInputAvailable()) next = InputMode.Blocked;
            else if (Main.MenuOpen) next = InputMode.Menu;
            else if (Player.IsCalibrating || character == null || character.IsDead || MySandboxGame.IsPaused ||
                !Player.HandL.pose.isTracked || !Player.HandR.pose.isTracked) next = InputMode.Blocked;
            else if (RadialOpen) next = InputMode.Radial;
            else if (RemoteView.Turret) next = InputMode.Turret;
            else if (MySession.Static.ControlledEntity is MyShipController) next = InputMode.Piloting;
            else if (MySession.Static.ControlledEntity != character) next = InputMode.Blocked;
            else if (PlacementControls.ClipboardActive) next = InputMode.Clipboard;
            else if (MyCubeBuilder.Static?.IsActivated == true) next = InputMode.Building;
            else next = Flying ? InputMode.Jetpack : InputMode.Walking;
            bool flying = Flying;
            if (next != Mode || !ReferenceEquals(nextOwner, owner) || flying != wasFlying)
            {
                if (!ReferenceEquals(nextOwner, owner) || next == InputMode.Blocked) GameActions.Reset();
                Reset(CanContinueLocomotion(Mode, next, owner, nextOwner));
                Mode = next;
                owner = nextOwner;
                wasFlying = flying;
            }
        }

        internal static bool CanContinueLocomotion(InputMode previous, InputMode next, object previousOwner, object nextOwner)
        {
            return previousOwner != null && ReferenceEquals(previousOwner, nextOwner) &&
                IsCharacterLocomotion(previous) && IsCharacterLocomotion(next);
        }

        private static bool IsCharacterLocomotion(InputMode mode) =>
            mode == InputMode.Walking || mode == InputMode.Jetpack || mode == InputMode.Building;

        public static void Reset() => Reset(false);

        private static void Reset(bool continueLocomotion)
        {
            GameActions.ResetJumpHold();
            Controls.Static.BlockUntilRelease(continueLocomotion);
            SpatialUi.ReleaseInput();
            WeaponHandling.Reset();
            CockpitControls.Release();
            VRMovementComponent.StopActive();
            NativeActions.Reset();
            MenuPointer.Release();
            Mode = InputMode.Blocked;
            owner = null;
        }
    }
}
