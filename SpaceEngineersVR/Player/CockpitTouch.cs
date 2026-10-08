using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            public bool Lever,Cover,Pull,Analog,FingerSlide;
            public CockpitRig.Handle Handle;
            public int Slot=-1;
            public float Position,Travel;
            public Vector3 Pivot,Axis;
            public bool Hinged => Lever && !FingerSlide || Cover || Handle?.Hinged==true;
            public bool Draggable => Hinged || Pull || Analog || FingerSlide;
            public bool Pinch => Draggable && !FingerSlide;
        }
        internal sealed class Hand
        {
            private readonly InputGate press=new InputGate();
            private readonly InputGate squeeze=new InputGate();
            private readonly PointerIntent pointer=new PointerIntent();
            private bool guardedSqueeze;
            private readonly HoldGesture holdGesture=new HoldGesture();
            private bool? gripSource,heldGrip;
            public string Surface { get; private set; }
            public int Held { get; private set; }=-1;
            public bool Captured { get; private set; }
            public bool Committed { get; private set; }
            public bool Pressed { get; private set; }
            public bool Consumed { get; private set; }
            public void Reset()
            {
                holdGesture.Reset(); press.Block(); squeeze.Block(); pointer.Reset(); heldGrip=null; guardedSqueeze=false; Surface=null; Held=-1; Captured=Committed=Pressed=false;
            }
            internal void Sample(bool available,InteractionInput input,string target,int key,bool reachable=true,bool guarded=false,bool softCapture=true,bool retainSqueeze=false,bool tapHold=false)
            {
                if(gripSource!=input.Near) { Reset(); gripSource=input.Near; }
                if(input.Near && Surface!=null && heldGrip.HasValue) input=input.Select(heldGrip.Value);
                bool down=holdGesture.Update(input.Down,Surface!=null && Committed,tapHold && input.Near && input.Grip && available && reachable,Multiplayer.MultiplayerRuntime.Now);
                float pressure=down && !input.Down ? 1 : !down && input.Down ? 0:input.Pressure;
                Sample(available,pressure,down,target,key,input.CanAcquire,reachable,guarded,softCapture && !input.Near,retainSqueeze,input.Near || !softCapture);
                if(input.Down && !down) Consumed=true;
                if(Captured) heldGrip=input.Near ? input.Grip:(bool?)null;
            }
            public void Sample(bool available,float pressure,bool down,string target,int key,bool canAcquire=true,bool reachable=true,bool guarded=false,bool softCapture=true,bool retainSqueeze=false,bool clickOnly=false)
            {
                Captured=Pressed=false;
                if(clickOnly && !down && !(retainSqueeze && Surface!=null && Committed))
                { Surface=null; Held=-1; Committed=Consumed=guardedSqueeze=false; }
                if(pressure<=.025f && !down) Consumed=guardedSqueeze=false;
                if(!available || float.IsNaN(pressure)) { Reset(); return; }
                press.Update(true,down);
                squeeze.Update(true,VrMath.Deadzone(pressure)>0 || down);
                pointer.Begin(true,clickOnly ? down ? 1:0:pressure,down,target!=null);
                if(!reachable || (Committed && !retainSqueeze ? !down : pressure<.08f && !down))
                { Surface=null; Held=-1; Committed=false; }
                if(Surface==null && (!Consumed || guardedSqueeze) && (softCapture && pressure>=.20f || press.Pressed) && canAcquire &&
                    target!=null && key>=0 && pointer.Capture(target,true))
                { Surface=target; Held=key; Captured=Consumed=true; guardedSqueeze=false; }
                if(Surface!=null && !Committed && press.Pressed) { Committed=Pressed=true; }
                if(press.Pressed && canAcquire && guarded) Consumed=true;
                if(press.Pressed) guardedSqueeze=false;
                // Reserve a nearby squeeze before its flight-action threshold.
                if(!Consumed && (squeeze.Pressed || clickOnly && pressure>.025f) && !down && canAcquire && guarded) Consumed=guardedSqueeze=true;
            }
        }
        internal sealed class HoverGrace
        {
            internal string Surface { get; private set; }
            private int key;
            private Vector3 contact;
            private Vector3D tip;
            private double last;
            internal void Reset() { Surface=null; }
            internal void Remember(string surface,int index,Vector3D localTip,Vector3 localContact,double now)
            { Surface=surface; key=index; tip=localTip; contact=localContact; last=now; }
            internal bool TryGet(Vector3D localTip,double now,out int index,out Vector3 localContact)
            {
                index=-1; localContact=Vector3.Zero;
                if(Surface==null || !localTip.IsValid() || now<last || now-last>.1 || Vector3D.DistanceSquared(tip,localTip)>.0001)
                { Reset(); return false; }
                index=key; localContact=contact; return true;
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
        internal class SurfaceHold
        {
            public readonly Hand Input=new Hand();
            public Vector3 StartHand,Anchor;
            public Matrix Wrist;
            public DateTime Grabbed;
            public void Capture(Matrix localWrist,Vector3 anchor)
            { Wrist=localWrist; StartHand=localWrist.Translation; Anchor=anchor; Grabbed=DateTime.UtcNow; }
            public bool Reachable(Vector3 localHand) => Vector3.Distance(localHand,StartHand)<.28f;
            public bool Attachment(MatrixD surface,out MatrixD wrist,out Vector3D contact,out float blend)
            {
                wrist=(MatrixD)Wrist*surface; contact=Vector3D.Transform(Anchor,surface);
                blend=MathHelper.Clamp((float)(DateTime.UtcNow-Grabbed).TotalSeconds/.09f,0,1);
                return Input.Surface!=null;
            }
        }
        private sealed class Contact : SurfaceHold
        {
            public readonly ControlDrag Drag=new ControlDrag();
            public readonly HoverGrace Grace=new HoverGrace();
            public Target Target,Hover;
            public int HoverKey=-1,Change=-1;
            public Vector3 FitAtGrab;
            public float GripOffset;
            public readonly CockpitFeedback.ValuePulse ValuePulse=new CockpitFeedback.ValuePulse();
            public float StartPosition;
        }
        private static readonly Contact[] hands={new Contact(),new Contact()};
        private static object owner;
        private static Matrix origin;
        public static bool LeftPointing { get; private set; }
        public static bool RightPointing { get; private set; }
        public static bool OwnsRight => hands[0].Input.Consumed;
        public static bool Owns(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Input.Consumed;
        internal static bool Hovering(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Hover!=null;
        public static bool Attached(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Input.Surface!=null;
        internal static bool HoldingBar(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Target?.Analog==true && hands[hand==Player.HandL ? 1 : 0].Target.Handle.Pinch==false && Attached(hand);
        public static bool Pinching(Controller hand) => hands[hand==Player.HandL ? 1 : 0].Target?.Pinch==true && Attached(hand);
        private static Controller Controller(int i) => i==0 ? Player.HandR : Player.HandL;
        private static void Consume(int i)
        {
            InteractionInput.Read(Controller(i),true).Consume();
        }
        public static void Reset()
        {
            foreach(var h in hands) { h.Input.Reset(); h.Grace.Reset(); h.Target=h.Hover=null; h.HoverKey=-1; }
            LeftPointing=RightPointing=false; owner=null; origin=Matrix.Identity;
        }
        internal static Vector3 Compensate(Vector3 hand,Vector3 fit,Vector3 startFit) => hand-(fit-startFit);
        internal const float BarGrabRadius=.045f;
        internal static bool NearBar(CockpitProbe finger,CockpitProbe? palm,out float distance,out Vector3 contact,float halfWidth=.06f,float radius=.0155f,float margin=0)
        {
            var bounds=new BoundingBox(new Vector3(-halfWidth,-radius,-2*radius),new Vector3(halfWidth,radius,0));
            bounds.Min-=new Vector3(margin); bounds.Max+=new Vector3(margin);
            float squared=finger.DistanceSquared(bounds,out contact);
            distance=(float)Math.Sqrt(squared);
            bool hit=squared<=CockpitProbe.Radius*CockpitProbe.Radius;
            if(palm.HasValue)
            {
                var axis=new BoundingBox(new Vector3(-halfWidth,0,-radius),new Vector3(halfWidth,0,-radius));
                float separation=(float)Math.Sqrt(palm.Value.DistanceSquared(axis,out var nearest));
                if(separation<=BarGrabRadius+margin && (!hit || separation<distance))
                { hit=true; distance=separation; contact=new Vector3(nearest.X,0,0); }
            }
            return hit;
        }
        internal static int NearKey(SurfaceView s,CockpitProbe probe,out float distance,out Vector3 contact,float padding=.001f,float margin=0)
        {
            distance=float.MaxValue; contact=Vector3.Zero; int key=-1;
            if(!s.Enabled) return -1;
            for(int i=0;i<s.Keys.Length;i++)
            {
                var k=s.Keys[i]; if(!k.Enabled || k.Bounds.Width<=0 || k.Bounds.Height<=0) continue;
                var bounds=CockpitPanelGuard.KeyBounds(s,k,margin);
                if(!probe.Intersects(bounds,padding)) continue;
                if(k.Round && SegmentDisk.DistanceSquared(probe.Start,probe.End,bounds)>(CockpitProbe.Radius+padding)*(CockpitProbe.Radius+padding)) continue;
                var point=k.Round ? SegmentDisk.Closest((Vector3)probe.Tip,bounds) : Vector3.Clamp((Vector3)probe.Tip,bounds.Min,bounds.Max);
                float candidate=Vector3.Distance((Vector3)probe.Tip,point);
                if(candidate>=distance) continue;
                distance=candidate; contact=point; key=i;
            }
            return key;
        }
        public static void BeginFrame()
        {
            long started=FeatureTiming.Start();
            try { BeginFrameCore(); }
            finally { FeatureTiming.End(FeatureTiming.Area.CockpitTouch,started); }
        }
        private static void BeginFrameCore()
        {
            CockpitButtons.Prepare();
            LeftPointing=RightPointing=false;
            var seat=SeatFit.Seat;
            bool eligible=SeatFit.Eligible(seat);
            if(!eligible)
            {
                for(int i=0;i<2;i++) if(hands[i].Input.Consumed)
                {
                    hands[i].Input.Sample(false,InteractionInput.Read(Controller(i),true),null,-1);
                    if(hands[i].Input.Consumed) Consume(i);
                }
                Reset();
                return;
            }
            bool changed=!ReferenceEquals(owner,seat) || origin!=Player.PlayerToAbsolute.matrix;
            owner=seat; origin=Player.PlayerToAbsolute.matrix;
            bool available=eligible && !FlightSettings.IsOpen && !ThirdPersonView.Active && !changed && InputRouter.CockpitInteraction && !Main.MenuOpen &&
                Player.Headset.pose.isTracked && Player.HandL.pose.isTracked && Player.HandR.pose.isTracked;
            var targets=new List<Target>(CockpitButtons.Targets);
            var panel=available && !WeaponHandling.ConsumesLeftGrip ? SeatPanel.View() : null;
            if(panel!=null) targets.Add(new Target { Surface=panel });
            double now=(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency;
            for(int i=0;i<2;i++)
            {
                var h=hands[i]; var hand=Controller(i); var c=Controls.Static;
                var input=InteractionInput.Read(hand,true);
                bool flying=i==0 ? c.ThrustRotate.RawPosition.LengthSquared()>.04f :
                    c.ThrustLRUD.RawPosition.LengthSquared()>.04f || c.ThrustLRFB.RawPosition.LengthSquared()>.04f;
                bool free=available && !ArthurLcdBridge.Owns(hand) && !CockpitControls.Held(hand) && !HelmetHud.Consumes(hand) &&
                    (i!=0 || !FloatingWindows.OwnsInput && !SpatialUi.OwnsRight && !TouchScreenBridge.OwnsInput);
                h.Change=-1;
                if(!free)
                {
                    h.Input.Sample(false,input,null,-1); h.Grace.Reset(); h.Target=h.Hover=null; h.HoverKey=-1;
                    if(h.Input.Consumed) Consume(i);
                    continue;
                }
                MatrixD rawWrist=TrackedArms.FreeWristWorld(hand);
                Matrix localWrist=(Matrix)(rawWrist*seat.PositionComp.WorldMatrixNormalizedInv);
                Vector3 raw=localWrist.Translation;
                var pointer=SpatialUi.DeviceWorld(hand.AimTracking);
                pointer.Translation+=pointer.Forward*.025;
                if(TrackedArms.TryFreePointPose(hand,out var finger)) pointer=finger;
                var probe=new CockpitProbe(pointer,hand==Player.HandL);
                Vector3D head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
                bool guarded=TouchScreenBridge.NearScreen(seat,probe);
                bool hasPalm=TrackedArms.TryFreeBarGrip(hand,out var palmRegion);
                Target chosen=null; int key=-1; float nearest=float.MaxValue; Vector3 chosenContact=Vector3.Zero;
                foreach(var target in targets)
                {
                    var s=target.Surface;
                    if((!target.Analog || target.Handle.Pinch) && Vector3D.Dot(s.Pose.Backward,head-s.Pose.Translation)<=.005) continue;
                    var localProbe=probe.Transform(MatrixD.Invert(s.Pose));
                    float distance; Vector3 contact;
                    if(target.Analog && !target.Handle.Pinch)
                        guarded|=NearBar(localProbe,hasPalm ? (CockpitProbe?)palmRegion.Transform(MatrixD.Invert(s.Pose)):null,
                            out _,out _,target.Handle.HalfWidth,target.Handle.Radius,CockpitPanelGuard.Margin);
                    else guarded|=NearKey(s,localProbe,out _,out _,target.Pull ? .004f : .001f,CockpitPanelGuard.Margin)>=0;
                    int candidate=target.Analog && !target.Handle.Pinch ? NearBar(localProbe,hasPalm ? (CockpitProbe?)palmRegion.Transform(MatrixD.Invert(s.Pose)):null,out distance,out contact,target.Handle.HalfWidth,target.Handle.Radius) ? 0:-1 :
                        NearKey(s,localProbe,out distance,out contact,target.Pull ? .004f : .001f);
                    if(candidate<0) continue;
                    guarded=true;
                    if(flying || hands[1-i].Input.Surface==s.Id) continue;
                    if(h.Hover?.Surface.Id==s.Id && h.HoverKey==candidate) distance-=.001f;
                    if(distance<nearest) { chosen=target; key=candidate; nearest=distance; chosenContact=contact; }
                }
                if(flying || h.Input.Surface!=null) h.Grace.Reset();
                else if(chosen!=null)
                    h.Grace.Remember(chosen.Surface.Id,key,Vector3D.Transform(probe.Tip,MatrixD.Invert(chosen.Surface.Pose)),chosenContact,now);
                else if(h.Grace.Surface!=null)
                {
                    var recent=targets.FirstOrDefault(t=>t.Surface.Id==h.Grace.Surface);
                    if(recent==null || hands[1-i].Input.Surface==recent.Surface.Id ||
                        Vector3D.Dot(recent.Surface.Pose.Backward,head-recent.Surface.Pose.Translation)<=.005)
                        h.Grace.Reset();
                    else if(h.Grace.TryGet(Vector3D.Transform(probe.Tip,MatrixD.Invert(recent.Surface.Pose)),now,out int recentKey,out var recentContact) &&
                        recentKey>=0 && recentKey<recent.Surface.Keys.Length && recent.Surface.Keys[recentKey].Enabled)
                    { chosen=recent; key=recentKey; chosenContact=recentContact; guarded=true; }
                }
                bool pointing=targets.Any(t=>Vector3D.Distance(rawWrist.Translation,t.Surface.Pose.Translation)<.4);
                if(i==0) RightPointing=pointing; else LeftPointing=pointing;
                if(chosen!=null && (h.Hover?.Surface.Id!=chosen.Surface.Id || h.HoverKey!=key) && !h.Input.Consumed)
                    CockpitFeedback.Hover(hand);
                h.Hover=chosen; h.HoverKey=key;
                var held=targets.FirstOrDefault(t=>t.Surface.Id==h.Input.Surface);
                Vector3 motion=Compensate(raw,SeatFit.Offset,h.FitAtGrab);
                bool reachable=h.Input.Surface==null || held!=null;
                h.Input.Sample(true,input,chosen?.Surface.Id,key,reachable,guarded,retainSqueeze:held?.Draggable==true,tapHold:Plugin.Common.Config.TapHoldLevers && (held ?? chosen)?.Analog==true);
                if(h.Input.Captured)
                {
                    held=chosen; h.StartHand=raw; h.FitAtGrab=SeatFit.Offset; h.Wrist=localWrist; h.Grabbed=DateTime.UtcNow;
                    var s=held.Surface; var b=s.Keys[h.Input.Held].Bounds;
                    Vector3D anchor=Vector3D.Transform(new Vector3D((b.Center.X-.5)*s.Width,(.5-b.Center.Y)*s.Height,0),s.Pose);
                    if(held.Pull || held.Analog)
                        anchor=Vector3D.Transform(chosenContact,s.Pose);
                    h.Capture(localWrist,(Vector3)Vector3D.Transform(anchor,seat.PositionComp.WorldMatrixNormalizedInv));
                    h.StartPosition=held.Position; h.GripOffset=chosenContact.X;
                    h.ValuePulse.Reset(held.Position);
                    if(held.Hinged) h.Drag.Begin(raw,held.Handle!=null ? Vector3.Transform(held.Handle.Center,held.Handle.Visual(held.Position)):h.Anchor,held.Pivot,held.Axis,held.Position,held.Travel);
                    else if(held.Pull || held.Analog || held.FingerSlide) h.Drag.BeginLinear(raw,held.Axis,held.Position,held.Travel);
                    CockpitFeedback.Engage(hand);
                }
                h.Target=h.Input.Surface!=null ? held : null;
                if(h.Target?.Draggable==true && !h.Input.Captured) h.Change=h.Drag.Move(motion,h.Input.Committed);
                if(h.Target?.Analog==true && h.Input.Committed && CockpitActions.ReadAnalog(h.Target.Slot,out _,out _) &&
                    h.ValuePulse.Sample(h.Drag.Value,DateTime.UtcNow)) CockpitFeedback.Activate(hand,.10f,.012f);
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
                result.Held=h.Input.Committed ? h.Input.Held : -1; result.Hover=h.Input.Held; result.Actor=Controller(i);
                result.Pressed=h.Input.Pressed;
                if(h.Target?.Draggable==true)
                {
                    result.Position=h.Drag.Value;
                    if(h.Change>=0) result.Requested=h.Change==1;
                }
            }
            return result;
        }
        internal static bool TryBarPalm(Controller hand,out MatrixD palm,out float blend)
        {
            palm=MatrixD.Identity; blend=0;
            if(!HoldingBar(hand) || !SeatFit.Eligible(SeatFit.Seat)) return false;
            var h=hands[hand==Player.HandL ? 1:0];
            var handle=CockpitRig.Find(SeatFit.Seat.BlockDefinition.Id.SubtypeName)?.HandleAt(h.Target.Slot);
            if(handle==null) return false;
            palm=(MatrixD)handle.Palm(hand==Player.HandL,CockpitButtons.SwitchPosition(h.Target.Slot),h.GripOffset)*SeatFit.Seat.WorldMatrix;
            blend=MathHelper.Clamp((float)(DateTime.UtcNow-h.Grabbed).TotalSeconds/.12f,0,1);
            return true;
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
            else if(h.Target.Pull || h.Target.Analog)
                anchor+=h.Target.Axis*((CockpitButtons.SwitchPosition(h.Target.Slot)-h.StartPosition)*h.Target.Travel);
            else if(CockpitRig.Find(SeatFit.Seat.BlockDefinition.Id.SubtypeName)?.ButtonAt(h.Target.Slot) is CockpitRig.Button button && h.Input.Committed)
                anchor-=button.Normal*button.Travel;
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
                if(h.Hover==null || h.Input.Consumed && !(h.Target?.Analog==true && CockpitActions.ReadAnalog(h.Target.Slot,out _,out _))) continue;
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
