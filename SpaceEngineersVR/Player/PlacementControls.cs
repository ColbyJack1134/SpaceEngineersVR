using System;
using Sandbox.Game;
using Sandbox.Game.SessionComponents.Clipboard;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class PlacementControls
    {
        public static bool ClipboardActive => MyClipboardComponent.Static?.IsActive == true;
        public static bool OwnsTools => InputRouter.Mode == InputMode.Building || InputRouter.Mode == InputMode.Clipboard;
        private static object poseOwner;
        private static MatrixD? lastPose;

        public static bool Adjusting { get; private set; }
        private static bool leftReady,rightReady;
        private static DateTime sampled;
        private static float elapsed,distanceFactor=1.1f;
        private static readonly DateTime[] repeat=new DateTime[4];
        public static void Update()
        {
            var now=DateTime.UtcNow;
            elapsed=(float)Math.Max(0,Math.Min(.05,(now-sampled).TotalSeconds)); sampled=now;
            distanceFactor=1.1f;
            var c=Controls.Static;
            bool active=OwnsTools && c.Secondary.IsPressed && !Main.MenuOpen;
            if(active && !Adjusting)
            {
                leftReady=rightReady=false; Array.Clear(repeat,0,repeat.Length);
                c.Primary.BlockUntilRelease(); Components.VRMovementComponent.StopActive();
            }
            if(!active && Adjusting)
            { c.Primary.BlockUntilRelease(); c.WalkLongitudinal.BlockUntilRelease(); c.WalkRotate.BlockUntilRelease(); c.ThrustLRFB.BlockUntilRelease(); c.ThrustRotate.BlockUntilRelease(); }
            Adjusting=active;
            if(!active) return;
            Vector2 left=InputRouter.Flying ? c.ThrustLRFB.Position:c.WalkLongitudinal.Position;
            Vector2 right=InputRouter.Flying ? c.ThrustRotate.Position:c.WalkRotate.Position;
            if(left.LengthSquared()<.09f) leftReady=true;
            if(right.LengthSquared()<.09f) rightReady=true;
            if(rightReady) { Axis(0,right.X,MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE,MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE); Axis(1,right.Y,MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE,MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE); }
            if(leftReady) { Axis(2,left.X,MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE,MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE); Axis(3,left.Y,MyControlsSpace.MOVE_FURTHER,MyControlsSpace.MOVE_CLOSER); }
        }
        private static void Axis(int index,float value,VRage.Utils.MyStringId positive,VRage.Utils.MyStringId negative)
        {
            var clipboard=MyClipboardComponent.Static?.Clipboard;
            bool continuous=InputRouter.Mode==InputMode.Clipboard ? index==3 || clipboard!=null && (clipboard.EnableStationRotation && !clipboard.IsSnapped || clipboard.EnablePreciseRotationWhenSnapped) :
                Sandbox.Game.Entities.MyCubeBuilder.Static?.DynamicMode==true;
            if(index==3 && continuous) distanceFactor=DistanceStep(value,elapsed);
            if(AxisDue(ref repeat[index],value,continuous,DateTime.UtcNow))
                NativeActions.Pulse(value>0 ? positive:negative);
        }
        internal static float NativeDistanceFactor(float original) => Adjusting ? distanceFactor : original;
        internal static float DistanceStep(float axis,float dt)
        {
            if(float.IsNaN(axis) || float.IsNaN(dt) || Math.Abs(axis)<.55f || dt<=0) return 1;
            double strength=Math.Min(1,(Math.Abs(axis)-.55)/.45);
            return (float)Math.Exp(Math.Log(1.1)/.22*Math.Min(.05,dt)*strength);
        }
        internal static bool AxisDue(ref DateTime next,float value,bool continuous,DateTime now)
        {
            if(float.IsNaN(value) || Math.Abs(value)<.55f) { next=DateTime.MinValue; return false; }
            if(continuous) { next=DateTime.MinValue; return true; }
            if(now<next) return false;
            next=now.AddSeconds(.22); return true;
        }
        // Preserve the preview while a wheel/menu owns the hands. Never use a stale
        // pose from another character, or move a preview with untracked input.
        public static bool TryPose(out MatrixD pose)
        {
            object owner = MySession.Static?.ControlledEntity;
            if (!ReferenceEquals(owner, poseOwner)) { lastPose = null; poseOwner = owner; }
            bool active = ClipboardActive || Sandbox.Game.Entities.MyCubeBuilder.Static?.IsActivated == true;
            if (!Main.VrActive || !active) { lastPose = null; pose = MatrixD.Identity; return false; }
            if ((OwnsTools || InputRouter.Mode==InputMode.Radial) && !Main.MenuOpen && HandInteraction.TryWorldPose(Player.HandR, out pose)) lastPose = pose;
            pose = lastPose ?? MatrixD.Identity;
            return lastPose.HasValue;
        }

        internal static void Queue(ActionFrame frame, InputMode mode, bool primary, bool secondary, bool alternate)
        {
            if (mode == InputMode.Clipboard)
            {
                if (primary && !secondary) frame.Queue(MyControlsSpace.COPY_PASTE_ACTION);
            }
            else if (mode == InputMode.Building)
            {
                if (primary && secondary) frame.Queue(MyControlsSpace.SECONDARY_TOOL_ACTION);
                else if (primary) frame.Queue(MyControlsSpace.PRIMARY_TOOL_ACTION);
            }
        }
        public static void Cancel() { if (ClipboardActive) NativeActions.Pulse(MyControlsSpace.COPY_PASTE_CANCEL); }
        public static void PreviewClipboard()
        {
            if (!ClipboardActive) NativeActions.Pulse(MyControlsSpace.PASTE_OBJECT);
        }
        public static void FreeRotation() { if (ClipboardActive) NativeActions.Pulse(MyControlsSpace.FREE_ROTATION); }
        public static void AlignGravity()
        {
            if(!ClipboardActive) return;
            var clipboard=MyClipboardComponent.Static.Clipboard;
            clipboard.EnableStationRotation=true;
            clipboard.AlignClipboardToGravity();
        }
    }
}
