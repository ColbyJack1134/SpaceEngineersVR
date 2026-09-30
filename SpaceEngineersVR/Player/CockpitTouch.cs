using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitTouch
    {
        internal sealed class Target
        {
            public SurfaceView Surface;
            public bool Lever,Cover;
            public int Slot=-1;
            public float Position,Travel;
            public Vector3 Pivot,Axis;
            public bool Hinged => Lever || Cover;
        }
        internal sealed class Hand
        {
            private readonly InputGate press=new InputGate();
            private readonly PointerIntent pointer=new PointerIntent();
            public string Surface { get; private set; }
            public int Held { get; private set; }=-1;
            public bool Pressed { get; private set; }
            public bool Consumed { get; private set; }
            public void Reset()
            {
                press.Block(); pointer.Reset(); Surface=null; Held=-1; Pressed=false;
            }
            public void Sample(bool available,float pressure,bool down,string target,int key,bool canAcquire=true,bool reachable=true)
            {
                Pressed=false;
                if(pressure<=.025f && !down) Consumed=false;
                if(!available) { Reset(); return; }
                press.Update(true,down);
                pointer.Begin(true,pressure,down,target!=null);
                if(!down || !reachable) { Surface=null; Held=-1; }
                if(Surface==null && !Consumed && press.Pressed && canAcquire && target!=null && key>=0 && pointer.Capture(target,true))
                { Surface=target; Held=key; Pressed=true; Consumed=true; }
            }
        }
        internal struct Result
        {
            public int Hover,Held;
            public bool Pressed;
            public bool? Requested;
            public float? Position;
            public Controller Actor;
        }
        private sealed class Contact
        {
            public readonly Hand Input=new Hand();
            public readonly HingeDrag Drag=new HingeDrag();
            public Target Target,Hover;
            public int HoverKey=-1,Change=-1;
            public Vector3 StartHand,FitAtGrab,Anchor;
            public Matrix Wrist;
            public DateTime Grabbed;
            public float StartPosition;
        }
        private static readonly Contact[] hands={new Contact(),new Contact()};
        private static object owner;
        private static Matrix origin;
        public static bool LeftPointing { get; private set; }
        public static bool RightPointing { get; private set; }
        public static bool OwnsRight => hands[0].Input.Consumed;
        public static bool Owns(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Input.Consumed;
        public static bool Attached(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Input.Surface!=null;
        public static bool Pinching(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Target?.Hinged==true && Attached(hand);
        private static Controller Controller(int i) => i==0 ? Player.HandR : Player.HandL;
        private static void Consume(int i)
        {
            var c=Controls.Static;
            if(i==0) c.Primary.BlockUntilRelease();
            else
            {
                c.ThrustUp.BlockUntilRelease(); c.ThrustForward.BlockUntilRelease();
                c.JumpOrClimbUp.BlockUntilRelease();
            }
        }
        public static void Reset()
        {
            foreach(var h in hands) { h.Input.Reset(); h.Target=h.Hover=null; h.HoverKey=-1; }
            LeftPointing=RightPointing=false; owner=null; origin=Matrix.Identity;
        }
        internal static Vector3 Compensate(Vector3 hand,Vector3 fit,Vector3 startFit) => hand-(fit-startFit);
        internal static int NearKey(SurfaceView s,Vector3 point,out float distance)
        {
            distance=float.MaxValue;
            if(!point.IsValid() || point.Z<-.014f || point.Z>.045f) return -1;
            var uv=PhysicalSurface.UV(s,point);
            int key=s.KeyAt(uv);
            if(key<0 && s.Style==SurfaceStyle.ModelControl && Math.Abs(point.X)<s.Width/2+.003f && Math.Abs(point.Y)<s.Height/2+.003f) key=0;
            if(key<0) return -1;
            var b=s.Keys[key].Bounds;
            var center=new Vector3((b.Center.X-.5f)*s.Width,(.5f-b.Center.Y)*s.Height,0);
            distance=Vector3.Distance(point,center);
            return key;
        }
        public static void BeginFrame()
        {
            CockpitButtons.Prepare();
            LeftPointing=RightPointing=false;
            var seat=SeatFit.Seat;
            bool eligible=SeatFit.Eligible(seat);
            if(!eligible)
            {
                for(int i=0;i<2;i++) if(hands[i].Input.Consumed)
                {
                    var c=Controls.Static;
                    float pressure=i==0 ? c.PointerPressure.RawPosition.X : c.LeftTriggerPressure.RawPosition.X;
                    hands[i].Input.Sample(false,pressure,i==0 ? c.Primary.RawPressed : pressure>.55f,null,-1);
                    if(hands[i].Input.Consumed) Consume(i);
                }
                Reset();
                return;
            }
            bool changed=!ReferenceEquals(owner,seat) || origin!=Player.PlayerToAbsolute.matrix;
            owner=seat; origin=Player.PlayerToAbsolute.matrix;
            bool available=eligible && !changed && InputRouter.Mode==InputMode.Piloting && !Main.MenuOpen &&
                Player.Headset.pose.isTracked && Player.HandL.pose.isTracked && Player.HandR.pose.isTracked && MenuPointer.GameFocused;
            var targets=new List<Target>(CockpitButtons.Targets);
            var panel=available && !WeaponHandling.ConsumesLeftGrip ? SeatPanel.View() : null;
            if(panel!=null) targets.Add(new Target { Surface=panel });
            for(int i=0;i<2;i++)
            {
                var h=hands[i]; var hand=Controller(i); var c=Controls.Static;
                float pressure=i==0 ? c.PointerPressure.RawPosition.X : c.LeftTriggerPressure.RawPosition.X;
                bool down=i==0 ? c.Primary.RawPressed : pressure>.55f;
                bool flying=i==0 ? c.ThrustRotate.RawPosition.LengthSquared()>.04f :
                    c.ThrustLRUD.RawPosition.LengthSquared()>.04f || c.ThrustLRFB.RawPosition.LengthSquared()>.04f;
                bool free=available && !CockpitControls.Held(hand) && !flying && !HelmetHud.Consumes(hand);
                h.Change=-1;
                if(!free)
                {
                    h.Input.Sample(false,pressure,down,null,-1); h.Target=h.Hover=null; h.HoverKey=-1;
                    if(h.Input.Consumed) Consume(i);
                    continue;
                }
                MatrixD rawWrist=TrackedArms.FreeWristWorld(hand);
                Matrix localWrist=(Matrix)(rawWrist*seat.PositionComp.WorldMatrixNormalizedInv);
                Vector3 raw=localWrist.Translation;
                var aim=SpatialUi.DeviceWorld(hand.AimTracking);
                Vector3D tip=aim.Translation+aim.Forward*.025;
                if(TrackedArms.TryFreeFingertip(hand,out var finger)) tip=finger;
                Vector3D head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                Target chosen=null; int key=-1; float nearest=float.MaxValue;
                foreach(var target in targets)
                {
                    var s=target.Surface;
                    if(hands[1-i].Input.Surface==s.Id || Vector3D.Dot(s.Pose.Backward,head-s.Pose.Translation)<=.005) continue;
                    var point=PhysicalSurface.Point(s,tip);
                    int candidate=NearKey(s,point,out float distance);
                    if(candidate<0) continue;
                    if(h.Hover?.Surface.Id==s.Id && h.HoverKey==candidate) distance-=.003f;
                    if(distance<nearest) { chosen=target; key=candidate; nearest=distance; }
                }
                bool pointing=targets.Any(t=>Vector3D.Distance(rawWrist.Translation,t.Surface.Pose.Translation)<.4);
                if(i==0) RightPointing=pointing; else LeftPointing=pointing;
                if(chosen!=null && (h.Hover?.Surface.Id!=chosen.Surface.Id || h.HoverKey!=key) && !h.Input.Consumed)
                    CockpitFeedback.Hover(hand);
                h.Hover=chosen; h.HoverKey=key;
                var held=targets.FirstOrDefault(t=>t.Surface.Id==h.Input.Surface);
                Vector3 motion=Compensate(raw,SeatFit.Offset,h.FitAtGrab);
                bool reachable=held!=null && Vector3.Distance(motion,h.StartHand)<.28f;
                bool canAcquire=i==0 ? c.Primary.HasPressed : c.LeftTriggerPressure.Position.X>0;
                h.Input.Sample(true,pressure,down,chosen?.Surface.Id,key,canAcquire,reachable);
                if(h.Input.Pressed)
                {
                    held=chosen; h.StartHand=raw; h.FitAtGrab=SeatFit.Offset; h.Wrist=localWrist; h.Grabbed=DateTime.UtcNow;
                    var s=held.Surface; var b=s.Keys[h.Input.Held].Bounds;
                    Vector3D anchor=Vector3D.Transform(new Vector3D((b.Center.X-.5)*s.Width,(.5-b.Center.Y)*s.Height,0),s.Pose);
                    h.Anchor=(Vector3)Vector3D.Transform(anchor,seat.PositionComp.WorldMatrixNormalizedInv);
                    h.StartPosition=held.Position;
                    if(held.Hinged) h.Drag.Begin(raw,h.Anchor,held.Pivot,held.Axis,held.Position,held.Travel);
                    CockpitFeedback.Engage(hand);
                }
                h.Target=h.Input.Surface!=null ? held : null;
                if(h.Target?.Hinged==true && !h.Input.Pressed) h.Change=h.Drag.Move(motion);
                if(h.Input.Consumed)
                {
                    h.Hover=h.Target; h.HoverKey=h.Target!=null ? h.Input.Held : -1;
                    Consume(i);
                }
            }
        }
        public static Result Read(string surface)
        {
            var result=new Result { Hover=-1,Held=-1 };
            for(int i=0;i<2;i++)
            {
                var h=hands[i];
                if(h.Hover?.Surface.Id==surface) result.Hover=h.HoverKey;
                if(h.Input.Surface!=surface) continue;
                result.Held=h.Input.Held; result.Hover=result.Held; result.Actor=Controller(i);
                result.Pressed=h.Input.Pressed;
                if(h.Target?.Hinged==true)
                {
                    result.Position=h.Drag.Value;
                    if(h.Change>=0) result.Requested=h.Change==1;
                }
            }
            return result;
        }
        internal static bool TryAttachment(Controller hand,out MatrixD wrist,out Vector3D contact,out float blend)
        {
            var h=hands[hand==Player.HandL ? 1 : 0];
            wrist=MatrixD.Identity; contact=Vector3D.Zero; blend=0;
            if(h.Input.Surface==null || h.Target==null || !SeatFit.Eligible(SeatFit.Seat)) return false;
            Vector3 anchor=h.Anchor;
            if(h.Target.Hinged)
            {
                float value=h.Target.Cover ? CockpitButtons.CoverPosition(h.Target.Slot) : CockpitButtons.SwitchPosition(h.Target.Slot);
                anchor=Vector3.Transform(anchor,CockpitStickMath.Around(h.Target.Pivot,Matrix.CreateFromAxisAngle(h.Target.Axis,(value-h.StartPosition)*h.Target.Travel)));
            }
            wrist=(MatrixD)h.Wrist*SeatFit.Seat.WorldMatrix;
            contact=Vector3D.Transform(anchor,SeatFit.Seat.WorldMatrix);
            blend=MathHelper.Clamp((float)(DateTime.UtcNow-h.Grabbed).TotalSeconds/.09f,0,1);
            return true;
        }
        internal static MatrixD LabelPose(MatrixD head,Vector3D tip,float width)
        {
            var pose=head.GetOrientation();
            pose.Translation=tip+head.Right*(width/2+.018)+head.Up*-.035;
            return pose;
        }
        public static IEnumerable<SurfaceView> Labels()
        {
            if(Main.MenuOpen || !SeatFit.Eligible(SeatFit.Seat)) yield break;
            for(int i=0;i<2;i++)
            {
                var h=hands[i];
                if(h.Input.Consumed || h.Hover==null) continue;
                var target=h.Hover;
                var hand=Controller(i);
                var aim=SpatialUi.DeviceWorld(hand.AimTracking);
                Vector3D tip=aim.Translation+aim.Forward*.025;
                if(TrackedArms.TryFingertip(hand,out var finger)) tip=finger;
                var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
                var label=CockpitButtons.Label(target,h.HoverKey);
                label.Id="CockpitLabel"+i; label.Pose=LabelPose(head,tip,label.Width);
                yield return label;
            }
        }
    }
}
