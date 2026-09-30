using Sandbox.Game.World;
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
        internal static bool NearTemple(Matrix hand, Matrix head)
        {
            Vector3 p=(hand*Matrix.Invert(head)).Translation;
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
            if (!InputRouter.Gameplay || Main.MenuOpen || !Player.HandR.pose.isTracked || !Player.Headset.pose.isTracked ||
                !NearTemple(Player.HandR.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix)) return;
            var c=Controls.Static;
            if (c.Primary.HasPressed)
            {
                Common.Config.HelmetHudMode=(Mode+1)%4;
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
