using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRageMath;
using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;

namespace SpaceEngineersVR.Player
{
    internal static class HelmetHud
    {
        // Zero really means empty; notifications do not temporarily resurrect the HUD.
        public static int Mode => Common.Config.HelmetHudMode;
        public static bool VisorClosed { get; private set; }
        public static bool Visible => VisorClosed && Mode > 0;
        public static bool Markers => Visible && Mode >= 2;
        public static bool Names => Visible && Mode == 3;
        private static int lastMode=-1;
        private static readonly InputGate leftTrigger=new InputGate();
        private static bool leftConsumed,rightConsumed;
        internal static bool Consumes(Controller hand) => hand==Player.HandL ? leftConsumed : rightConsumed;
        internal static bool NearTemple(Matrix hand, Matrix head,bool left=false)
        {
            Vector3 p=(hand*Matrix.Invert(head)).Translation;
            if(left) p.X=-p.X;
            return p.X>0.10f && p.X<0.34f && p.Y> -0.18f && p.Y<0.17f && p.Z> -0.18f && p.Z<0.20f;
        }
        public static void Update()
        {
            VisorClosed=MySession.Static?.LocalCharacter?.OxygenComponent?.HelmetEnabled == true;
            if(Mode!=lastMode)
            {
                if(Mode>=2) AccessTools.Property(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.SignalDisplayMode))
                    .SetValue(null,Mode==3 ? MyHudMarkerRender.SignalMode.FullDisplay : MyHudMarkerRender.SignalMode.NoNames,null);
                lastMode=Mode;
            }
            var c=Controls.Static;
            float leftPressure=c.LeftTriggerPressure.RawPosition.X;
            if(leftPressure<=.025f) leftConsumed=false;
            if(c.PointerPressure.RawPosition.X<=.025f && !c.Primary.RawPressed) rightConsumed=false;
            bool active=InputRouter.Gameplay && !Main.MenuOpen && !ThirdPersonView.Manipulating && MenuPointer.GameFocused && Player.Headset.pose.isTracked;
            leftTrigger.Update(active && Player.HandL.pose.isTracked && c.LeftTriggerPressure.Active,leftPressure>.55f);
            if(active && Player.HandL.pose.isTracked && !leftConsumed && leftTrigger.Pressed &&
                !CockpitControls.Held(Player.HandL) && !CockpitTouch.Owns(Player.HandL) &&
                !WeaponHandling.ConsumesLeftGrip && NearTemple(Player.HandL.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix,true))
            {
                MySession.Static?.LocalCharacter?.SwitchLights();
                leftConsumed=true;
                Player.HandL.Vibrate(0,.035f,120,.25f);
            }
            if(leftConsumed)
            {
                c.LeftTriggerPressure.BlockUntilRelease(); c.ThrustUp.BlockUntilRelease();
                c.ThrustForward.BlockUntilRelease(); c.JumpOrClimbUp.BlockUntilRelease();
            }
            if(!active || !Player.HandR.pose.isTracked ||
                !NearTemple(Player.HandR.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix)) return;
            if (c.Primary.HasPressed && !CockpitTouch.OwnsRight && !CockpitControls.Held(Player.HandR))
            {
                Common.Config.HelmetHudMode=(Mode+1)%4;
                rightConsumed=true;
                c.Primary.BlockUntilRelease();
                Player.HandR.Vibrate(0,0.035f,130,0.35f);
            }
            if (c.Secondary.HasPressed)
            {
                GameActions.HelmetAction.Run();
                c.Secondary.BlockUntilRelease(); c.ThrustRoll.BlockUntilRelease();
                Player.HandR.Vibrate(0,0.06f,95,0.45f);
            }
        }
    }
}
