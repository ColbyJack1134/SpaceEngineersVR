using System;
using System.Diagnostics;
using System.Threading;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using VRage.Game.Entity;
using VRage.Game.ModAPI.Interfaces;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Player.Components;
using SpaceEngineersVR.Plugin;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class ThirdPersonView
    {
        private static readonly Diorama view=new Diorama();
        private static readonly GripDescent descent=new GripDescent();
        internal static bool DescentReady => !Active || descent.Ready;
        private static readonly ObserverFollow follow=new ObserverFollow();
        private static readonly object sync=new object();
        private static MyEntity subject;
        private static IMyCameraController SubjectCamera => subject as IMyCameraController;
        private static MyEntity Target => subject is MyCockpit cockpit ? cockpit.CubeGrid : subject;
        private static CameraRig.Frame frame;
        private static int epoch;
        private static Action transition;
        private static DateTime changeAt;
        private static volatile bool consumed;
        private static bool traceRequested;
        private static long inputTime,renderTime;
        private static readonly double[] renderTrace=new double[7];
        private static ObserverMode Mode => (ObserverMode)(Common.Config?.ThirdPersonMode ?? 0);
        internal static string Label(ObserverMode mode) => "Camera: "+(mode==ObserverMode.Ship ? "follow ship" : mode==ObserverMode.Heading ? "heading only" : "fixed");
        public static string ModeLabel => Character && Mode==ObserverMode.Ship ? "Camera: follow character" : Label(Mode);
        public static CameraRig.Frame Current => Volatile.Read(ref frame);
        public static bool Active => Current!=null;
        public static bool Manipulating => Active && (consumed || transition!=null);
        public static bool Character => Active && subject is MyCharacter;
        public static bool Owns(MyEntity entity) => Active && entity==subject;

        private static MyEntity Candidate
        {
            get
            {
                var session=MySession.Static;
                var character=session?.LocalCharacter;
                if(!Main.VrActive || session==null || !session.Enable3RdPersonView || !RemoteView.Live(character) || character.IsDead) return null;
                var cockpit=RemoteView.HomeSeat ?? session.ControlledEntity as MyCockpit;
                if(cockpit!=null) return RemoteView.Live(cockpit) && cockpit.Pilot==character &&
                    (session.CameraController==cockpit || RemoteView.UsesSeat(cockpit)) ? cockpit : null;
                return !character.IsSitting && (session.ControlledEntity==character && session.CameraController==character || RemoteView.CharacterAnchor) ? character : null;
            }
        }
        public static void Toggle()
        {
            var candidate=Candidate;
            if(candidate==null) { EssentialHud.Notify("Third person is unavailable here"); return; }
            Fade(()=> {
                if(Candidate!=candidate) return;
                var camera=(IMyCameraController)candidate;
                if(Owns(candidate))
                {
                    if(Character)
                    {
                        var offset=Player.Headset.deviceToPlayer.Translation; offset.Y=0;
                        Player.ConsumeRoomscale(offset);
                    }
                    Reset(); camera.IsInFirstPersonView=true;
                }
                else { subject=candidate; Fit(); camera.IsInFirstPersonView=false; }
            });
        }
        public static void ResetView()
        {
            if(Active) Fade(Fit);
        }
        public static void CycleMode() => SetMode((Common.Config.ThirdPersonMode+1)%3);
        public static void SetMode(int mode)
        {
            lock(sync)
            {
                var before=follow.Reference(Mode);
                Common.Config.ThirdPersonMode=mode;
                if(Active) { view.ChangeReference(before,follow.Reference(Mode)); epoch--; }
            }
            if(Active)
            {
                Controls.Static.BlockUntilRelease(); VRMovementComponent.StopActive();
                Publish();
            }
        }
        private static void Fade(Action action)
        {
            if(transition!=null) return;
            lock(sync) view.Cancel();
            NativeActions.Reset(); RemoteView.ReleaseInput();
            transition=action; changeAt=DateTime.UtcNow.AddSeconds(.1);
            OpenVR.Compositor?.FadeToColor(.09f,0,0,0,1,false);
        }
        private static void Fit()
        {
            if(subject==null || Candidate!=subject || !Player.Headset.pose.isTracked) return;
            var head=subject is MyCockpit cockpit ? cockpit.GetHeadMatrix(false,false) : ((MyCharacter)subject).GetHeadMatrix(false);
            var up=Target.Physics?.Gravity ?? Vector3.Zero;
            Vector3D vertical=up.LengthSquared()>.01 ? -(Vector3D)Vector3.Normalize(up) : head.Up;
            Matrix tracking=Player.Headset.deviceToPlayer;
            var orientation=Mode==ObserverMode.Ship ? head.GetOrientation() : VrMath.Level(head,vertical).GetOrientation();
            var facing=VrMath.Level(tracking,Vector3D.Up);
            var center=tracking.Translation+facing.Forward*1.15-facing.Up*.2;
            lock(sync)
            {
                follow.Reset(subject.WorldMatrix,vertical);
                view.Fit(Target.PositionComp.LocalAABB.Size.Length(),orientation*MatrixD.Transpose(follow.Reference(Mode)),center);
                epoch--;
            }
            TrackedArms.Reset(); CockpitControls.Release(); SpatialUi.ReleaseInput();
            Publish();
        }
        public static void Recenter(Matrix oldOrigin,Matrix newOrigin)
        {
            if(!Active) return;
            lock(sync) { view.Rebase(oldOrigin,newOrigin); epoch--; }
            ResetView();
        }
        public static void Update()
        {
            var candidate=Candidate;
            if(subject!=null && candidate!=subject) Reset();
            if(transition!=null && DateTime.UtcNow>=changeAt)
            {
                var action=transition; transition=null;
                try { action(); }
                finally { OpenVR.Compositor?.FadeToColor(.15f,0,0,0,0,false); }
            }
            if(candidate!=null && !((IMyCameraController)candidate).IsInFirstPersonView && subject==null && transition==null)
            {
                // Also handle a saved view or a native camera toggle.
                subject=candidate; Fit();
            }
            if(!Active) return;
            if(SubjectCamera.IsInFirstPersonView) { Reset(); return; }
            var c=Controls.Static;
            float left=c.LeftGripPressure.RawPosition.X,right=c.RightGripPressure.RawPosition.X;
            if(left<=.025f && right<=.025f) consumed=false;
            bool previouslyConsumed=consumed;
            bool allowed=InputRouter.Gameplay && !Main.MenuOpen && !HelmetHud.ViewGestureHeld && transition==null &&
                !CockpitTouch.OwnsRight && !CockpitTouch.Owns(Player.HandL) && !SpatialUi.OwnsRight && !RemoteView.OwnsInput &&
                !CockpitControls.Held(Player.HandL) && !CockpitControls.Held(Player.HandR) &&
                !CockpitControls.NearGrip(Player.HandL) && !CockpitControls.NearGrip(Player.HandR) &&
                Player.Headset.pose.isTracked && Player.HandL.pose.isTracked && Player.HandR.pose.isTracked && MenuPointer.GameFocused;
            descent.Update(allowed,left,DateTime.UtcNow);
            if(allowed && left>.025f && right>.025f) consumed=true;
            bool started;
            lock(sync)
            {
                inputTime=Stopwatch.GetTimestamp();
                bool flight=c.Primary.IsPressed || c.Secondary.IsPressed || c.ThrustRoll.IsPressed ||
                    c.ThrustLRFB.Position!=Vector2.Zero || c.ThrustLRUD.Position!=Vector2.Zero || c.ThrustRotate.Position!=Vector2.Zero ||
                    c.ThrustUp.Position.X!=0 || c.ThrustDown.Position.X!=0 || c.ThrustForward.Position.X!=0 || c.ThrustBackward.Position.X!=0;
                started=view.Input(allowed,left,right,flight);
                if(view.Held) consumed=true;
            }
            if(consumed && !previouslyConsumed) NativeActions.Reset();
            if(started)
            {
                VRMovementComponent.StopActive();
                Player.HandL.Vibrate(0,.018f,100,.15f); Player.HandR.Vibrate(0,.018f,100,.15f);
                if(!traceRequested) { traceRequested=true; StereoRenderState.RequestTrace(); }
            }
            if(consumed || transition!=null)
            {
                c.ThrustDown.BlockUntilRelease(); c.ThrustRoll.BlockUntilRelease(); c.Secondary.BlockUntilRelease();
                c.Primary.BlockUntilRelease(); c.ThrustUp.BlockUntilRelease(); c.ThrustLRFB.BlockUntilRelease();
                c.ThrustLRUD.BlockUntilRelease(); c.ThrustRotate.BlockUntilRelease();
                c.ThrustForward.BlockUntilRelease(); c.ThrustBackward.BlockUntilRelease();
            }
            Publish();
        }
        public static void Publish()
        {
            if(subject==null || Candidate!=subject) return;
            var target=Vector3D.Transform(Target.PositionComp.LocalAABB.Center,Target.WorldMatrix);
            lock(sync)
            {
                follow.Advance(subject.WorldMatrix);
                var scene=new Diorama.Scene(target,follow.Reference(Mode),Stopwatch.GetTimestamp());
                Volatile.Write(ref frame,new CameraRig.Frame(view.Anchor(target,scene.Reference),Player.PlayerToAbsolute.inverted,epoch,view.UnitsPerMeter,true,scene));
            }
        }
        public static CameraRig.Frame RenderFrame(CameraRig.Frame packet)
        {
            if(packet?.Observer==null) return packet;
            lock(sync)
            {
                if(epoch!=packet.Epoch || Current?.Epoch!=packet.Epoch) return packet;
                long now=Stopwatch.GetTimestamp();
                double seconds=renderTime==0 ? 1d/90 : (double)(now-renderTime)/Stopwatch.Frequency;
                renderTime=now;
                var config=Common.Config;
                view.PanSensitivity=config.ThirdPersonPanSensitivity; view.ZoomSensitivity=config.ThirdPersonZoomSensitivity;
                view.RotationSensitivity=config.ThirdPersonRotationSensitivity; view.PanGlide=config.ThirdPersonPanGlide;
                view.ZoomGlide=config.ThirdPersonZoomGlide; view.RotationGlide=config.ThirdPersonRotationGlide;
                if(!Player.Headset.renderPose.isTracked || !Player.HandL.renderPose.isTracked || !Player.HandR.renderPose.isTracked)
                    view.Cancel();
                else view.Move(VrMath.Affine(Player.HandL.RenderGripTracking*packet.OriginInverse),
                    VrMath.Affine(Player.HandR.RenderGripTracking*packet.OriginInverse),seconds);
                // Keep the subject paired with its native scene batch; late-update only the view offset.
                var scene=packet.Observer;
                renderTrace[0]=(double)Mode; renderTrace[1]=view.Hands; renderTrace[2]=view.UnitsPerMeter;
                renderTrace[3]=(now-inputTime)*1000d/Stopwatch.Frequency;
                renderTrace[4]=(now-scene.Timestamp)*1000d/Stopwatch.Frequency;
                renderTrace[5]=view.LastTranslation; renderTrace[6]=view.LastRotation;
                return new CameraRig.Frame(view.Anchor(scene.Target,scene.Reference),packet.OriginInverse,packet.Epoch,view.UnitsPerMeter,true,scene);
            }
        }
        public static void RecordTrace(CameraRig.Frame packet)
        {
            if(packet?.Observer!=null) StereoRenderState.Record("view_gesture",renderTrace);
        }
        public static void Reset()
        {
            lock(sync)
            {
                subject=null; Volatile.Write(ref frame,null); view.Cancel(); consumed=false; descent.Update(false,0,DateTime.UtcNow);
                transition=null; epoch--; renderTime=0; traceRequested=false;
            }
            OpenVR.Compositor?.FadeToColor(.1f,0,0,0,0,false);
        }
    }
}
