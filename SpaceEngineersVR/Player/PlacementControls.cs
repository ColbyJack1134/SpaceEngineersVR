using System;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using VRage.Game;
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
        internal static bool GridClipboardActive => MyClipboardComponent.Static?.Clipboard?.IsActive==true;
        public static bool ClipboardActive => MyClipboardComponent.Static?.IsActive == true;
        internal static InputMode Mode => ToolMode(InputRouter.Mode,ClipboardActive,CockpitBuilding.Active);
        internal static InputMode ToolMode(InputMode mode,bool clipboard,bool cockpitBuilding) =>
            mode==InputMode.Spectator && clipboard ? InputMode.Clipboard : cockpitBuilding && mode==InputMode.Piloting ? InputMode.Building : mode;
        public static bool OwnsTools => Mode==InputMode.Building || Mode==InputMode.Clipboard;
        internal static bool BlockTool => Mode==InputMode.Building && !VoxelTools.Active;
        public static bool Painting { get; private set; }
        internal static bool Observer => Main.VrActive && !RemoteView.Active && ThirdPersonView.Active && (ThirdPersonView.Character || CockpitBuilding.Active);
        internal static bool Creative => MySession.Static!=null && (MySession.Static.CreativeMode ||
            MySession.Static.HasCreativeRights && MySession.Static.CreativeToolsEnabled(Sandbox.Game.Multiplayer.Sync.MyId));
        internal static string ShapeLabel => "Build shape: "+(Sandbox.MySandboxGame.Config==null ? "Single":
            MyCubeBuilder.BuildingMode==MyCubeBuilder.BuildingModeEnum.SingleBlock ? "Single":MyCubeBuilder.BuildingMode.ToString());
        internal static void CycleShape()
        {
            if(!Creative || MyCubeBuilder.Static?.IsActivated!=true || !MyCubeBuilder.Static.IsBuildToolActive()) return;
            NativeActions.Reset(); Controls.Static.Fire.BlockUntilRelease();
            MyCubeBuilder.BuildingMode=(MyCubeBuilder.BuildingModeEnum)(((int)MyCubeBuilder.BuildingMode+1)%3);
        }
        private static object poseOwner;
        private static MatrixD? lastPose;
        [ThreadStatic] internal static bool EditingDistance;

        // Rotate mode: a right grip tap lets the sticks rotate the preview, so a held grip still rolls in flight.
        // The optional hold mode rotates only while the grip is held; grip + trigger still removes.
        public static bool Adjusting { get; private set; }
        private static readonly GripTap rotateTap=new GripTap();
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
            var hand=Handedness.ActionHand;
            bool grip=c.ActionGripClick.IsPressed && !(hand==Player.HandR ? CockpitControls.RotationOwned:(CockpitControls.Adjusting || CockpitControls.Held(hand))) && !CockpitTouch.Owns(hand) &&
                !SpatialUi.Owns(hand) && !TouchScreenBridge.OwnsInput && !HandInteraction.Consumes(hand) &&
                !FloatingWindows.OwnsInput && !HelmetHud.Consumes(hand);
            bool wasPainting=Painting;
            Painting=PaintChord(Mode,grip,c.Interact.IsPressed,ThirdPersonView.Manipulating || Main.MenuOpen);
            if(Painting)
            {
                if(!wasPainting) NativeActions.Reset();
                c.Fire.BlockUntilRelease();
                NativeActions.Pulse(VoxelTools.Current?.PaintControl ?? MyControlsSpace.CUBE_COLOR_CHANGE);
            }
            bool tools=OwnsTools && !Main.MenuOpen && !SymmetrySetupControls.Active;
            Vector2 left=InputRouter.Flying ? c.ThrustLRFB.Position:c.WalkLongitudinal.Position;
            Vector2 right=InputRouter.Flying ? c.ThrustRotate.Position:c.WalkRotate.Position;
            // Grip chords (roll, remove, paint, size, view grab) are not taps.
            bool chord=c.Fire.IsPressed || c.Interact.IsPressed || c.Jetpack.IsPressed || ThirdPersonView.Manipulating ||
                left.LengthSquared()>=.09f || right.LengthSquared()>=.09f;
            bool hold=Common.Config.HoldGripRotation;
            bool tap=rotateTap.Update(grip,c.ActionGripClick.HasReleased,chord || !tools || hold,now);
            bool placed=Adjusting && c.Fire.HasPressed;
            bool active=hold ? tools && grip:Rotating(Adjusting,tools,tap,placed);
            if(active && !Adjusting)
            {
                leftReady=rightReady=false; Array.Clear(repeat,0,repeat.Length);
                Components.VRMovementComponent.StopActive();
            }
            if(!active && Adjusting)
            {
                // The placing press must reach the builder.
                if(!placed) c.Fire.BlockUntilRelease();
                c.WalkLongitudinal.BlockUntilRelease(); c.WalkRotate.BlockUntilRelease(); c.ThrustLRFB.BlockUntilRelease(); c.ThrustRotate.BlockUntilRelease();
            }
            Adjusting=active;
            VoxelTools.Update(active && !Painting && (Math.Abs(right.X)>=.55f || Math.Abs(right.Y)>=.55f || Math.Abs(left.X)>=.55f));
            if(!active || Painting) return;
            if(left.LengthSquared()<.09f) leftReady=true;
            if(right.LengthSquared()<.09f) rightReady=true;
            if(rightReady) { Axis(0,right.X,MyControlsSpace.CUBE_ROTATE_VERTICAL_POSITIVE,MyControlsSpace.CUBE_ROTATE_VERTICAL_NEGATIVE); Axis(1,right.Y,MyControlsSpace.CUBE_ROTATE_HORISONTAL_POSITIVE,MyControlsSpace.CUBE_ROTATE_HORISONTAL_NEGATIVE); }
            if(leftReady) { Axis(2,left.X,MyControlsSpace.CUBE_ROTATE_ROLL_POSITIVE,MyControlsSpace.CUBE_ROTATE_ROLL_NEGATIVE); Axis(3,left.Y,MyControlsSpace.MOVE_FURTHER,MyControlsSpace.MOVE_CLOSER); }
        }
        private static void Axis(int index,float value,VRage.Utils.MyStringId positive,VRage.Utils.MyStringId negative)
        {
            var tool=Mode==InputMode.Building ? VoxelTools.Current:null;
            if(tool!=null)
            {
                if(!AxisDue(ref repeat[index],value,true,DateTime.UtcNow)) return;
                if(index==3) tool.Adjust(AdjustFactor(value,elapsed));
                else tool.Rotate(index*2+(value>0 ? 0:1),elapsed);
                return;
            }
            var clipboard=MyClipboardComponent.Static?.Clipboard;
            bool continuous=Mode==InputMode.Clipboard ? index==3 || GridClipboardActive && clipboard!=null && (clipboard.EnableStationRotation && !clipboard.IsSnapped || clipboard.EnablePreciseRotationWhenSnapped) :
                Sandbox.Game.Entities.MyCubeBuilder.Static?.DynamicMode==true;
            if(index==3 && continuous) distanceFactor=DistanceStep(value,elapsed);
            if(AxisDue(ref repeat[index],value,continuous,DateTime.UtcNow,index==3 ? DistanceRepeat:RotationRepeat))
                NativeActions.Pulse(value>0 ? positive:negative);
        }
        // DistanceStep is a magnitude; the native builder supplies direction by dividing for closer.
        internal static float AdjustFactor(float axis,float dt) => axis>0 ? DistanceStep(axis,dt):1/DistanceStep(axis,dt);
        internal static float NativeDistanceFactor(float original) => Adjusting ? distanceFactor : original;
        internal static float DistanceStep(float axis,float dt)
        {
            if(float.IsNaN(axis) || float.IsNaN(dt) || Math.Abs(axis)<.55f || dt<=0) return 1;
            double strength=Math.Min(1,(Math.Abs(axis)-.55)/.45);
            return (float)Math.Exp(Math.Log(1.1)/DistanceRepeat*Math.Min(.05,dt)*strength);
        }
        // Seconds per native 10% distance step at full deflection, and per stepped rotation.
        internal const double DistanceRepeat=.11,RotationRepeat=.22;
        internal static bool AxisDue(ref DateTime next,float value,bool continuous,DateTime now,double interval=RotationRepeat)
        {
            if(float.IsNaN(value) || Math.Abs(value)<.55f) { next=DateTime.MinValue; return false; }
            if(continuous) { next=DateTime.MinValue; return true; }
            if(now<next) return false;
            next=now.AddSeconds(interval); return true;
        }
        internal static bool Rotating(bool rotating,bool tools,bool tap,bool placed) => tools && !placed && (tap ? !rotating:rotating);
        internal static bool PaintChord(InputMode mode,bool grip,bool interact,bool blocked) =>
            mode==InputMode.Building && grip && interact && !blocked;

        // Retain the preview during menus; discard poses when the owner changes.
        public static bool TryPose(out MatrixD pose)
        {
            object owner = MySession.Static?.ControlledEntity;
            if (!ReferenceEquals(owner, poseOwner)) { lastPose = null; poseOwner = owner; }
            bool active = ClipboardActive || Sandbox.Game.Entities.MyCubeBuilder.Static?.IsActivated == true || VoxelTools.Active;
            if (!Main.VrActive || !active) { lastPose = null; pose = MatrixD.Identity; return false; }
            if ((OwnsTools || InputRouter.Mode==InputMode.Radial) && !Main.MenuOpen && HandInteraction.TryVisiblePointerPose(Handedness.ActionHand, out pose)) lastPose = pose;
            pose = lastPose ?? MatrixD.Identity;
            return lastPose.HasValue;
        }

        public static bool TryBuilderPose(out MatrixD pose)
        {
            pose=MatrixD.Identity;
            if(CockpitBuilding.Active && !ThirdPersonView.Active) return false;
            if(!TryPose(out pose)) return false;
            if(Observer) pose=VrMath.Rigid(pose);
            return true;
        }
        internal static float BuildDistance(float native)
        {
            if(!Observer || EditingDistance) return native;
            if(Creative) return (float)CreativeObserverDistance(native,ThirdPersonView.Current.UnitsPerMeter,
                MyBlockBuilderBase.CubeBuilderDefinition.DefaultBlockBuildingDistance);
            return (float)ObserverDistance(native,ThirdPersonView.Current.UnitsPerMeter);
        }
        // Third person keeps the first-person world distance from the finger; the scaled distance is only the grid search range.
        internal static bool TryFreeTarget(out Vector3D target)
        {
            target=Vector3D.Zero;
            if(!Observer || EditingDistance || MyCubeBuilder.Static?.HitInfo.HasValue!=false) return false;
            target=MyBlockBuilderBase.IntersectionStart+MyBlockBuilderBase.IntersectionDirection*RawDistance();
            return target.IsValid();
        }
        // Only grids use the longer observer search.
        internal static bool DropsVoxelHit(Vector3D hit,Vector3D start)
        {
            if(!Observer || EditingDistance) return false;
            double reach=RawDistance();
            return Vector3D.DistanceSquared(hit,start)>reach*reach;
        }
        // Stored metres, before observer scaling.
        internal static float RawDistance()
        {
            bool previous=BeginRawDistance();
            try { return MyBlockBuilderBase.IntersectionDistance; }
            finally { EditingDistance=previous; }
        }
        internal static bool BeginRawDistance() { bool previous=EditingDistance; EditingDistance=true; return previous; }
        internal static double CreativeObserverDistance(double nativeDistance,double scale,double defaultDistance) =>
            Math.Min(20000,nativeDistance*scale/Math.Max(1,defaultDistance));
        internal static double ObserverDistance(double nativeDistance,double scale) => Math.Min(20000,Math.Max(nativeDistance,nativeDistance*scale));
        internal static double SurvivalRange(float gridSize)
        {
            var definition=MyBlockBuilderBase.CubeBuilderDefinition;
            var size=MyCubeBuilder.Static?.CubeBuilderState?.CurrentBlockDefinition?.CubeSize;
            bool large=size.HasValue ? size==MyCubeSize.Large : gridSize>1;
            return MySession.Static?.ControlledEntity is MyShipController ?
                large ? definition.BuildingDistLargeSurvivalShip : definition.BuildingDistSmallSurvivalShip :
                large ? definition.BuildingDistLargeSurvivalCharacter : definition.BuildingDistSmallSurvivalCharacter;
        }
        internal static bool WithinReach(BoundingBoxD box,MatrixD inverse,Vector3D head,double range) =>
            box.Distance(Vector3D.Transform(head,inverse))<=range;
        internal static bool BuildReach(BoundingBoxD box,MatrixD inverse,Vector3D head,float gridSize,double range)
        {
            box.Inflate(.025f*gridSize);
            return WithinReach(box,inverse,head,range);
        }

        internal static void Queue(ActionFrame frame, InputMode mode, bool primary, bool secondary, bool alternate)
        {
            if (mode == InputMode.Clipboard)
            {
                if (primary && !secondary) frame.Queue(MyControlsSpace.COPY_PASTE_ACTION);
            }
            else if (mode == InputMode.Building && VoxelTools.Current is VoxelTool tool) tool.Queue(frame,primary,secondary);
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
        public static void FreeRotation() { if (GridClipboardActive) NativeActions.Pulse(MyControlsSpace.FREE_ROTATION); }
        public static void AlignGravity()
        {
            if(!GridClipboardActive) return;
            var clipboard=MyClipboardComponent.Static.Clipboard;
            clipboard.EnableStationRotation=true;
            clipboard.AlignClipboardToGravity();
        }
    }
}
