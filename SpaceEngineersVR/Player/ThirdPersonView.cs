using System;
using System.Diagnostics;
using System.Threading;
using Sandbox.Game.Entities;
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
        private static readonly ObserverFollow follow=new ObserverFollow();
        private static readonly object sync=new object();
        private static MyCockpit seat;
        private static CameraRig.Frame frame;
        private static int epoch;
        private static Action transition;
        private static DateTime changeAt;
        private static volatile bool consumed;
        private static bool traceRequested;
        private static long inputTime,renderTime;
        private static readonly double[] renderTrace=new double[7];
        private static ObserverMode Mode => (ObserverMode)(Common.Config?.ThirdPersonMode ?? 0);
        internal static string Label(ObserverMode mode) => "Camera: "+(mode==ObserverMode.Ship ? "Ship" : mode==ObserverMode.Heading ? "Heading" : "Fixed");
        public static string ModeLabel => Label(Mode);
        public static CameraRig.Frame Current => Volatile.Read(ref frame);
        public static bool Active => Current!=null;
        public static bool Manipulating => Active && (consumed || transition!=null);
        public static bool Owns(MyCockpit cockpit) => Active && cockpit==seat;

        private static MyCockpit Candidate
        {
            get
            {
                var session=MySession.Static;
                var cockpit=session?.ControlledEntity as MyCockpit;
                return Main.VrActive && session!=null && session.Enable3RdPersonView && cockpit!=null && !cockpit.Closed && !cockpit.MarkedForClose &&
                    cockpit.Pilot==session.LocalCharacter && cockpit.Pilot?.IsDead==false && session.CameraController==cockpit ? cockpit : null;
            }
        }
        public static void Toggle()
        {
            var cockpit=Candidate;
            if(cockpit==null) { EssentialHud.Notify("Third person requires a cockpit and world permission"); return; }
            Fade(()=> {
                if(Candidate!=cockpit) return;
                if(Owns(cockpit)) { Reset(); cockpit.IsInFirstPersonView=true; }
                else { seat=cockpit; Fit(); cockpit.IsInFirstPersonView=false; }
            });
        }
        public static void ResetView()
        {
            if(Active) Fade(Fit);
        }
        public static void CycleMode()
        {
            lock(sync)
            {
                var before=follow.Reference(Mode);
                Common.Config.ThirdPersonMode=(Common.Config.ThirdPersonMode+1)%3;
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
            Controls.Static.BlockUntilRelease(); VRMovementComponent.StopActive();
            transition=action; changeAt=DateTime.UtcNow.AddSeconds(.1);
            OpenVR.Compositor?.FadeToColor(.09f,0,0,0,1,false);
        }
        private static void Fit()
        {
            if(seat==null || Candidate!=seat || !Player.Headset.pose.isTracked) return;
            var head=seat.GetHeadMatrix(false,false);
            var up=seat.CubeGrid.Physics?.Gravity ?? Vector3.Zero;
            Vector3D vertical=up.LengthSquared()>.01 ? -(Vector3D)Vector3.Normalize(up) : head.Up;
            Matrix tracking=Player.Headset.deviceToPlayer;
            var orientation=Mode==ObserverMode.Ship ? head.GetOrientation() : VrMath.Level(head,vertical).GetOrientation();
            var facing=VrMath.Level(tracking,Vector3D.Up);
            var center=tracking.Translation+facing.Forward*1.15-facing.Up*.2;
            lock(sync)
            {
                follow.Reset(seat.WorldMatrix,vertical);
                view.Fit(seat.CubeGrid.PositionComp.LocalAABB.Size.Length(),orientation*MatrixD.Transpose(follow.Reference(Mode)),center);
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
            if(seat!=null && candidate!=seat) Reset();
            if(transition!=null && DateTime.UtcNow>=changeAt)
            {
                var action=transition; transition=null;
                try { action(); }
                finally { OpenVR.Compositor?.FadeToColor(.15f,0,0,0,0,false); }
            }
            if(candidate!=null && !candidate.IsInFirstPersonView && seat==null && transition==null)
            {
                // Also handle a saved third-person seat or a native camera toggle.
                seat=candidate; Fit();
            }
            if(!Active) return;
            if(seat.IsInFirstPersonView) { Reset(); return; }
            var c=Controls.Static;
            float left=c.LeftGripPressure.RawPosition.X,right=c.RightGripPressure.RawPosition.X;
            if(left<=.025f && right<=.025f) consumed=false;
            bool allowed=InputRouter.Mode==InputMode.Piloting && !Main.MenuOpen && transition==null &&
                Player.Headset.pose.isTracked && Player.HandL.pose.isTracked && Player.HandR.pose.isTracked && MenuPointer.GameFocused;
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
            if(seat==null || Candidate!=seat) return;
            var target=Vector3D.Transform(seat.CubeGrid.PositionComp.LocalAABB.Center,seat.CubeGrid.WorldMatrix);
            lock(sync)
            {
                follow.Advance(seat.WorldMatrix);
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
                if(!Player.Headset.renderPose.isTracked || !Player.HandL.renderPose.isTracked || !Player.HandR.renderPose.isTracked)
                    view.Cancel();
                else view.Move(VrMath.Affine(Player.HandL.RenderGripTracking*packet.OriginInverse),
                    VrMath.Affine(Player.HandR.RenderGripTracking*packet.OriginInverse),seconds);
                // Keep the ship paired with its native scene batch; late-update only the user's view offset.
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
                seat=null; Volatile.Write(ref frame,null); view.Cancel(); consumed=false;
                transition=null; epoch--; renderTime=0; traceRequested=false;
            }
            OpenVR.Compositor?.FadeToColor(.1f,0,0,0,0,false);
        }
    }
}
