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
        public static int Mode => Common.Config.WaypointMode;
        public static bool VisorClosed { get; private set; }
        public static bool Visible
        {
            get
            {
                var current=transition;
                return Common.Config.HudWithVisorOpen || (VisorClosed && (current==null || !current.Closing || Elapsed(current)>=Duration));
            }
        }
        public static bool Markers => Visible && Mode >= 1;
        public static bool Names => Visible && Mode == 2;
        internal static bool Reveal { get; private set; }
        private static int lastMode=-1;
        private static float ownRange=-1,friendlyRange=-1,otherRange=-1;
        private static readonly InputGate leftTrigger=new InputGate(),leftGrip=new InputGate(),rightGrip=new InputGate();
        private static readonly HeadGesture rightGesture=new HeadGesture();
        internal static bool ProtectsRight { get; private set; }
        internal static bool ViewGestureHeld { get; private set; }
        private static bool leftConsumed,rightConsumed;
        // After cycling profiles the cycling hand is still at the helmet; hold off the proximity reveal so the new profile is visible.
        internal const double RevealPause=1.5;
        private static DateTime revealPauseUntil;
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
            return p.X>0.10f && InHeadZone(p);
        }
        internal static bool NearHead(Matrix hand,Matrix head)
        {
            Vector3 p=(hand*Matrix.Invert(head)).Translation;
            return InHeadZone(p);
        }
        private static bool InHeadZone(Vector3 p) => Math.Abs(p.X)<.24f && p.Y>-.12f && p.Y<.16f && p.Z>-.13f && p.Z<.27f;
        public static void Update()
        {
            var character=MySession.Static?.LocalCharacter;
            bool closed=character?.OxygenComponent?.HelmetEnabled == true;
            if(characterId!=character?.EntityId || !Main.WorldAvailable || character?.IsDead!=false)
            { characterId=character?.EntityId ?? 0; transition=null; }
            else if(closed!=VisorClosed) transition=new Transition(closed);
            VisorClosed=closed;
            if(Mode!=lastMode || MyHudMarkerRender.SignalDisplayMode!=(Mode>0 ? MyHudMarkerRender.SignalMode.NoNames:MyHudMarkerRender.SignalMode.Off))
            {
                AccessTools.Property(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.SignalDisplayMode))
                    .SetValue(null,Mode>0 ? MyHudMarkerRender.SignalMode.NoNames : MyHudMarkerRender.SignalMode.Off,null);
                lastMode=Mode;
            }
            var config=Common.Config;
            if(ownRange!=config.OwnSignalRange) MyHudMarkerRender.OwnerAntennaRange=ownRange=config.OwnSignalRange;
            if(friendlyRange!=config.FriendlySignalRange) MyHudMarkerRender.FriendAntennaRange=friendlyRange=config.FriendlySignalRange;
            if(otherRange!=config.OtherSignalRange) MyHudMarkerRender.EnemyAntennaRange=otherRange=config.OtherSignalRange;
            var c=Controls.Static;
            float leftPressure=c.LeftTriggerPressure.RawPosition.X;
            if(leftPressure<=.025f) leftConsumed=false;
            if(c.PointerPressure.RawPosition.X<=.025f && !c.Primary.RawPressed) rightConsumed=false;
            bool active=InputRouter.Gameplay && !Main.MenuOpen && !ThirdPersonView.Manipulating && Player.Headset.pose.isTracked;
            bool guard=InputRouter.Gameplay && !Main.MenuOpen && Player.Headset.pose.isTracked;
            rightGesture.Update(active && Player.HandR.pose.isTracked && c.Primary.Active,
                Player.HandR.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix,c.Primary.RawPressed);
            bool nearLeft=guard && Player.HandL.pose.isTracked && NearHead(Player.HandL.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix);
            bool nearRight=guard && Player.HandR.pose.isTracked && (rightGesture.Inside || NearHead(Player.HandR.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix));
            Reveal=active && Markers && (Mode==2 || (nearLeft || nearRight) && DateTime.UtcNow>=revealPauseUntil);
            rightGrip.Update(active && Player.HandR.pose.isTracked && c.Secondary.Active,c.Secondary.RawPressed);
            float grip=c.LeftGripPressure.RawPosition.X;
            if(grip<=.025f) ViewGestureHeld=false;
            leftGrip.Update(active && Player.HandL.pose.isTracked && c.LeftGripPressure.Active,grip>.55f);
            if(leftGrip.Pressed && InputRouter.Gameplay && !CockpitControls.Held(Player.HandL) &&
                !CockpitTouch.Owns(Player.HandL) && NearTemple(Player.HandL.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix,true))
            {
                ViewGestureHeld=true; ThirdPersonView.Toggle(); Player.HandL.Vibrate(0,.035f,120,.25f);
            }
            if(ViewGestureHeld) { c.LeftGripPressure.BlockUntilRelease(); c.ThrustDown.BlockUntilRelease(); c.CrouchOrClimbDown.BlockUntilRelease(); }
            leftTrigger.Update(active && Player.HandL.pose.isTracked && c.LeftTriggerPressure.Active,leftPressure>.55f);
            if(active && Player.HandL.pose.isTracked && leftTrigger.Pressed &&
                !CockpitControls.Held(Player.HandL) && !CockpitTouch.Owns(Player.HandL) &&
                !WeaponHandling.ConsumesLeftGrip && NearTemple(Player.HandL.GripTracking,Player.Headset.pose.deviceToAbsolute.matrix,true))
            {
                MySession.Static?.LocalCharacter?.SwitchLights();
                leftConsumed=true;
                Player.HandL.Vibrate(0,.035f,120,.25f);
            }
            if(nearLeft && leftPressure>.025f) leftConsumed=true;
            if(nearRight && (c.PointerPressure.RawPosition.X>.025f || c.Primary.RawPressed)) rightConsumed=true;
            ProtectsRight=nearRight || rightConsumed;
            if(nearLeft)
            { c.LeftGripPressure.BlockUntilRelease(); c.ThrustDown.BlockUntilRelease(); c.CrouchOrClimbDown.BlockUntilRelease(); }
            if(nearRight || rightConsumed) c.Primary.BlockUntilRelease();
            if(nearRight) { c.RightGripPressure.BlockUntilRelease(); c.Secondary.BlockUntilRelease(); c.ThrustRoll.BlockUntilRelease(); }
            if(leftConsumed)
            {
                c.LeftClick.BlockUntilRelease(); c.LeftTriggerPressure.BlockUntilRelease(); c.ThrustUp.BlockUntilRelease();
                c.ThrustForward.BlockUntilRelease(); c.JumpOrClimbUp.BlockUntilRelease();
            }
            if(!active || !Player.HandR.pose.isTracked || !rightGesture.Inside) return;
            if (rightGesture.Pressed && !CockpitTouch.OwnsRight && !CockpitControls.Held(Player.HandR))
            {
                Common.Config.CycleHud();
                revealPauseUntil=DateTime.UtcNow.AddSeconds(RevealPause);
                rightConsumed=true;
                c.Primary.BlockUntilRelease();
                Player.HandR.Vibrate(0,0.035f,130,0.35f);
            }
            if (rightGrip.Pressed)
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
