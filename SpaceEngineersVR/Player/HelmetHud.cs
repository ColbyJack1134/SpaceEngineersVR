using System;
using System.Drawing;
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
        public static bool Visible
        {
            get
            {
                var current=transition;
                return VisorClosed && Mode > 0 && (current==null || !current.Closing || Elapsed(current)>=Duration);
            }
        }
        public static bool Markers => Visible && Mode >= 2;
        public static bool Names => Visible && Mode == 3;
        private static int lastMode=-1;
        private static readonly InputGate leftTrigger=new InputGate();
        private static bool leftConsumed,rightConsumed;
        private static long characterId;
        private sealed class Transition
        {
            public readonly DateTime Started=DateTime.UtcNow;
            public readonly bool Closing;
            public Transition(bool closing) { Closing=closing; }
        }
        internal const float Duration=.55f, FadeDuration=18f;
        private static float Elapsed(Transition value) => (float)(DateTime.UtcNow-value.Started).TotalSeconds;
        private static volatile Transition transition;
        private static OverlayCanvas visor;
        private static bool animationFailed;
        internal static bool Consumes(Controller hand) => hand==Player.HandL ? leftConsumed : rightConsumed;
        internal static bool NearTemple(Matrix hand, Matrix head,bool left=false)
        {
            Vector3 p=(hand*Matrix.Invert(head)).Translation;
            if(left) p.X=-p.X;
            return p.X>0.10f && p.X<0.34f && p.Y> -0.18f && p.Y<0.17f && p.Z> -0.18f && p.Z<0.20f;
        }
        public static void Update()
        {
            var character=MySession.Static?.LocalCharacter;
            bool closed=character?.OxygenComponent?.HelmetEnabled == true;
            if(characterId!=character?.EntityId || !Main.WorldAvailable || character?.IsDead!=false)
            { characterId=character?.EntityId ?? 0; transition=null; }
            else if(closed!=VisorClosed) transition=new Transition(closed);
            VisorClosed=closed;
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
        internal static float Closure(float elapsed,bool closing)
        {
            float t=MathHelper.Clamp(elapsed/Duration,0,1);
            float eased=t*t*(3-2*t);
            return closing ? eased : 1-eased;
        }
        internal static float TintAlpha(float elapsed) => 1-MathHelper.SmoothStep(0,1,MathHelper.Clamp((elapsed-Duration)/FadeDuration,0,1));
        internal static void PaintGlass(OverlayCanvas target,float bottom)
        {
            target.Clear(System.Drawing.Color.Transparent);
            using(var glass=new System.Drawing.Drawing2D.LinearGradientBrush(
                new System.Drawing.Rectangle(0,0,target.Width,target.Height),
                System.Drawing.Color.FromArgb(12,22,22,21),System.Drawing.Color.FromArgb(7,29,29,27),90f))
                target.Graphics.FillRectangle(glass,0,Math.Min(0,bottom-target.Height),target.Width,Math.Max(0,bottom));
            using(var rim=new Pen(System.Drawing.Color.FromArgb(110,45,45,42),3))
                target.Graphics.DrawBezier(rim,0,bottom-13,target.Width*.25f,bottom+3,target.Width*.75f,bottom+3,target.Width,bottom-13);
        }
        internal static void PaintTransition(OverlayCanvas target,float progress,bool closing)
        {
            float closure=Closure(progress*Duration,closing);
            PaintGlass(target,target.Height*(1.12f*closure-.06f));
        }
        public static void DrawTransition()
        {
            long started=FeatureTiming.Start();
            try { DrawTransitionCore(); }
            finally { FeatureTiming.End(FeatureTiming.Area.Visor,started); }
        }
        private static void DrawTransitionCore()
        {
            if(animationFailed || !Main.WorldAvailable || Main.MenuOpen || ThirdPersonView.Active)
            { visor?.Hide(); return; }
            try
            {
                if(visor==null)
                {
                    visor=new OverlayCanvas("Visor transition",1024,768,3.2f);
                    visor.Position(Matrix.CreateTranslation(0,3,-.8f),true); visor.Show(0);
                    PaintGlass(visor,visor.Height-1); visor.Upload();
                }
                var current=transition;
                if(current==null) { visor.Hide(); return; }
                float elapsed=Elapsed(current);
                float alpha=current.Closing ? TintAlpha(elapsed) : 1;
                if((!current.Closing && elapsed>=Duration) || alpha<=0) { visor.Hide(); return; }
                float closure=Closure(elapsed,current.Closing);
                visor.Position(Matrix.CreateTranslation(0,2.4f*(1-closure),-.8f),true);
                visor.Show(alpha);
            }
            catch(Exception ex) { animationFailed=true; visor?.Hide(); Logger.Warning(ex,"Visor transition unavailable"); }
        }
    }
}
