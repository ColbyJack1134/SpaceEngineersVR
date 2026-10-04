using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Utils;

namespace SpaceEngineersVR.Player
{
    internal static class NativeActions
    {
        private static readonly ActionFrame frame = new ActionFrame();
        private static IMyControllableEntity owner;
        public static void Pulse(MyStringId action) => frame.Queue(action);
        public static void Update()
        {
            bool enabled = InputRouter.Gameplay && !Main.MenuOpen;
            if (enabled)
            {
                owner = MySession.Static?.ControlledEntity;
                if(HelmetHud.Reveal) frame.Queue(MyControlsSpace.SIGNALS_FULLY_VISIBLE);
                if(WheelJumpAllowed && Controls.Static.Jetpack.IsPressed) frame.Queue(MyControlsSpace.WHEEL_JUMP);
                if(SprintAllowed && Controls.Static.CrouchOrClimbDown.IsPressed) frame.Queue(MyControlsSpace.SPRINT);
                PlacementControls.Queue(frame, PlacementControls.Mode, Controls.Static.Primary.IsPressed,
                    Controls.Static.Secondary.IsPressed, GameActions.AlternateTrigger);
            }
            frame.Advance(enabled);
        }
        // Left grip sprints on foot and descends on the jetpack; ladders use the stick.
        internal static bool SprintAllowed => (InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Building) &&
            MySession.Static?.ControlledEntity is Sandbox.Game.Entities.Character.MyCharacter character && !character.IsOnLadder &&
            !WeaponHandling.ConsumesLeftGrip && ThirdPersonView.DescentReady && !PlacementControls.Adjusting;
        internal static bool WheelJumpAllowed => InputRouter.Mode==InputMode.Piloting && MySession.Static?.ControlledEntity is MyShipController ship &&
            ship.ControlWheels && !ToolbarWheel.QuickPending && !RemoteView.OwnsInput && !PlacementControls.Adjusting && !CockpitControls.Adjusting && !ThirdPersonView.Manipulating;
        public static bool Read(MyStringId action, MyControlStateType type) =>
            Main.VrActive && InputRouter.Gameplay && !Main.MenuOpen && frame.Read(action, type);

        private static bool WasHeld(MyStringId action) => frame.Read(action, MyControlStateType.PRESSED) || frame.Read(action, MyControlStateType.NEW_RELEASED);
        public static void Reset()
        {
            // The wheel/dashboard do not necessarily invoke the native screen's
            // InputLost. Cancel pending line/plane build/remove strokes before any
            // synthetic release; StopBuilding would commit the pending operation.
            if (PlacementControls.Mode == InputMode.Building) MyCubeBuilder.Static?.InputLost();
            if (WasHeld(MyControlsSpace.PRIMARY_TOOL_ACTION)) owner?.EndShoot(MyShootActionEnum.PrimaryAction);
            if (WasHeld(MyControlsSpace.SECONDARY_TOOL_ACTION)) owner?.EndShoot(MyShootActionEnum.SecondaryAction);
            if(WasHeld(MyControlsSpace.WHEEL_JUMP)) (owner as MyShipController)?.WheelJump(false);
            frame.Reset(); owner = null;
        }
    }
}
