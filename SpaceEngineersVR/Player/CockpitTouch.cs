using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Independent state per hand and panel. A finger must retract; a ray needs a
    // fresh trigger, including after owner/origin/menu/tracking transitions.
    internal sealed class CockpitTouch
    {
        internal sealed class Hand
        {
            private readonly InputGate press=new InputGate();
            private readonly SurfaceTouch touch=new SurfaceTouch();
            private int capture=-1;
            private bool nearCapture;
            private Vector3 contactMotion;
            private readonly SwitchFlick flick=new SwitchFlick();
            public bool? FlickState { get; private set; }
            public int Hover { get; private set; }=-1;
            public int Held { get; private set; }=-1;
            public void Reset() { press.Block(); touch.Reset(); flick.Reset(); FlickState=null; nearCapture=false; capture=Hover=Held=-1; }
            internal static int NearKey(Vector3 point,int key) => point.Z>=-.018f && point.Z<.07f ? key : -1;
            internal static int SeatTarget(Vector3 point,int key,int rayKey)
            {
                int nearby=NearKey(point,key);
                return nearby>=0 ? nearby : rayKey;
            }
            public Vector3 ContactPoint(Vector3 point,Vector3 seatMotion) =>
                (touch.Held>=0 && touch.Held<7) || nearCapture ? point-(seatMotion-contactMotion) : point;
            public int Sample(bool available,bool raw,string surface,Vector3 point,int key,int rayKey,bool isSwitch=false,int hoverKey=-1,bool triggerOnly=false,Vector3 seatMotion=default(Vector3))
            {
                if(!available) { Reset(); return -1; }
                press.Update(true,raw);
                FlickState=null;
                int direction=isSwitch ? flick.Update(point) : -1;
                bool seatDirection=surface=="Seat" && key>=0 && key<7;
                if(triggerOnly) touch.Reset();
                int tap=triggerOnly ? -1 : isSwitch ? direction>=0 ? 0 : -1 : touch.Update(surface,point,key,seatDirection);
                if(surface=="Seat" && raw && capture>=0 && tap==capture) tap=-1;
                if(tap>=0 && seatDirection) contactMotion=seatMotion;
                if(direction>=0) FlickState=direction==1;
                Hover=NearKey(point,Math.Max(key,hoverKey));
                if(Hover<0) Hover=rayKey;
                if(!raw) { capture=-1; nearCapture=false; }
                int pressKey=rayKey>=0 ? rayKey : triggerOnly && point.Z>=-.035f && point.Z<.055f ? key : -1;
                if(pressKey>=0 && press.Pressed)
                {
                    bool alreadyTouched=surface=="Seat" && touch.Held==pressKey;
                    if(!alreadyTouched || tap>=0) tap=pressKey;
                    capture=pressKey; FlickState=null;
                    nearCapture=surface=="Seat" && pressKey<7 && pressKey==NearKey(point,key);
                    if(nearCapture && !alreadyTouched) contactMotion=seatMotion;
                }
                Held=touch.Held>=0 ? touch.Held : capture==Hover ? capture : -1;
                return tap;
            }
        }
        private readonly Hand[] hands={new Hand(),new Hand()};
        private object owner;
        private Matrix origin;
        public int Hover { get; private set; }=-1;
        public int Held { get; private set; }=-1;
        public Controller Actor { get; private set; }
        public bool? RequestedState { get; private set; }
        public static bool LeftPointing { get; private set; }
        public static bool RightPointing { get; private set; }
        private static readonly PointerIntent[] pointers={new PointerIntent(),new PointerIntent()};
        private static readonly Vector3D?[] rayHit=new Vector3D?[2];
        private static readonly bool[] showRay=new bool[2];
        private static object pointerOwner;
        private static Matrix pointerOrigin;
        public static bool OwnsRight => pointers[0].Owned;
        public static void BeginFrame()
        {
            LeftPointing=RightPointing=false;
            // The main menu has tracking, but no world camera or cockpit.
            if(!SeatFit.Eligible(SeatFit.Seat))
            {
                pointerOwner=null; pointerOrigin=Matrix.Identity;
                for(int i=0;i<2;i++)
                {
                    bool consumed=pointers[i].Owned;
                    pointers[i].Reset(); showRay[i]=false; rayHit[i]=null;
                    if(consumed) Consume(i);
                }
                return;
            }
            bool changed=!ReferenceEquals(pointerOwner,SeatFit.Seat) || pointerOrigin!=Player.PlayerToAbsolute.matrix;
            pointerOwner=SeatFit.Seat; pointerOrigin=Player.PlayerToAbsolute.matrix;
            bool available=!changed && SeatFit.Eligible(SeatFit.Seat) && InputRouter.Mode==InputMode.Piloting && !Main.MenuOpen && MenuPointer.GameFocused;
            var seat=available && !WeaponHandling.ConsumesLeftGrip ? SeatPanel.View() : null;
            for(int i=0;i<2;i++)
            {
                showRay[i]=false;
                rayHit[i]=null;
                var hand=i==0 ? Player.HandR : Player.HandL;
                bool consumed=pointers[i].Owned;
                float pressure=i==0 ? Controls.Static.PointerPressure.RawPosition.X : Controls.Static.ThrustUp.RawPosition.X;
                bool clicked=i==0 ? Controls.Static.Primary.RawPressed : pressure>.55f;
                var c=Controls.Static;
                bool flying=i==0 ? c.ThrustRotate.RawPosition.LengthSquared()>.04f :
                    c.ThrustLRUD.RawPosition.LengthSquared()>.04f || c.ThrustLRFB.RawPosition.LengthSquared()>.04f;
                bool free=available && hand.pose.isTracked && !CockpitControls.Held(hand) && !flying;
                bool pointing=false;
                if(free)
                {
                    var aim=SpatialUi.DeviceWorld(hand.AimTracking);
                    pointing=SpatialUi.Current.Any(s=>s.Style!=SurfaceStyle.Pointer && s.Keys.Length>0 &&
                        Vector3D.Distance(aim.Translation+aim.Forward*.025,s.Pose.Translation)<.4);
                }
                if(i==0) RightPointing=pointing; else LeftPointing=pointing;
                bool nearSeat=free && SpatialUi.SeatNearKey(seat,i)>=0;
                pointers[i].Begin(free,pressure,clicked,pointing || nearSeat,nearSeat ? "Seat" : null);
                if(consumed || pointers[i].Owned) Consume(i);
                if(pointers[i].Preview && !nearSeat)
                {
                    showRay[i]=true;
                }
            }
        }
        private static void Consume(int hand)
        {
            if(hand==0) Controls.Static.Primary.BlockUntilRelease();
            else Controls.Static.ThrustUp.BlockUntilRelease();
        }
        public void Reset()
        {
            foreach(var h in hands) h.Reset();
            Hover=Held=-1; Actor=null; RequestedState=null;
        }
        private Vector3 LocalPoint(SurfaceView surface,int index,MatrixD aim,out Vector3 seatMotion)
        {
            var hand=index==0 ? Player.HandR : Player.HandL;
            Vector3D tip=aim.Translation+aim.Forward*.025;
            if(Vector3D.Distance(tip,surface.Pose.Translation)<.4)
            {
                if(index==0) RightPointing=true; else LeftPointing=true;
                if(TrackedArms.TryFingertip(hand,out var finger)) tip=finger;
            }
            var local=PhysicalSurface.Point(surface,tip);
            seatMotion=Vector3.Zero;
            if(surface.Id=="Seat")
            {
                seatMotion=(Vector3)Vector3D.TransformNormal(SeatFit.Offset,SeatFit.Seat.WorldMatrix*MatrixD.Invert(surface.Pose));
                local=hands[index].ContactPoint(local,seatMotion);
            }
            return local;
        }
        internal int NearKey(SurfaceView surface,int hand)
        {
            if(surface==null) return -1;
            var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
            if(Vector3D.Dot(surface.Pose.Backward,head-surface.Pose.Translation)<=.015) return -1;
            var aim=SpatialUi.DeviceWorld((hand==0 ? Player.HandR : Player.HandL).AimTracking);
            var point=LocalPoint(surface,hand,aim,out _);
            return Hand.NearKey(point,surface.KeyAt(PhysicalSurface.UV(surface,point)));
        }
        public int Update(SurfaceView surface,bool available,bool rightBlocked=false,bool isSwitch=false,bool triggerOnly=false)
        {
            if(!ReferenceEquals(owner,SeatFit.Seat) || origin!=Player.PlayerToAbsolute.matrix)
            { Reset(); owner=SeatFit.Seat; origin=Player.PlayerToAbsolute.matrix; }
            Hover=Held=-1; Actor=null; RequestedState=null;
            if(!available || surface==null) { Reset(); return -1; }
            int clicked=-1;
            var head=SpatialUi.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation;
            bool front=Vector3D.Dot(surface.Pose.Backward,head-surface.Pose.Translation)>.015;
            for(int i=0;i<2;i++)
            {
                var state=hands[i]; var hand=i==0 ? Player.HandR : Player.HandL;
                bool raw=i==0 ? Controls.Static.Primary.RawPressed : Controls.Static.ThrustUp.RawPosition.X>.55f;
                if(!front || CockpitControls.Held(hand) || (i==0 && rightBlocked)) { state.Reset(); continue; }
                var aim=SpatialUi.DeviceWorld(hand.AimTracking);
                var local=LocalPoint(surface,i,aim,out var seatMotion);
                int key=surface.KeyAt(PhysicalSurface.UV(surface,local));
                var ray=(Matrix)(aim*MatrixD.Invert(surface.Pose));
                int hit=ray.Translation.Length()<1.25f && VrMath.PanelHit(ray,Matrix.Identity,surface.Width,surface.Height,out var uv) ? surface.KeyAt(uv) : -1;
                int target=surface.Id=="Seat" ? Hand.SeatTarget(local,key,hit) : hit;
                bool pointing=pointers[i].Capture(surface.Id,target>=0);
                var hoverUv=PhysicalSurface.UV(surface,local);
                int hoverKey=isSwitch && hoverUv.X>=0 && hoverUv.X<=1 && hoverUv.Y>=0 && hoverUv.Y<=1.8f ? 0 : -1;
                int tap=state.Sample(true,raw,surface.Id,local,key,pointing ? target : -1,isSwitch,hoverKey,triggerOnly,seatMotion);
                int hover=state.Hover,held=state.Held;
                if(pointing)
                {
                    Consume(i);
                    if(hit>=0 && pointers[i].Preview)
                    {
                        float distance=-ray.Translation.Z/ray.Forward.Z;
                        rayHit[i]=Vector3D.Transform(aim.Translation+aim.Forward*distance,SeatFit.Seat.PositionComp.WorldMatrixNormalizedInv);
                    }
                }
                if(hover>=0 && Hover<0) Hover=hover;
                if(held>=0 && Held<0) Held=held;
                if(tap>=0 && clicked<0) { clicked=tap; Actor=hand; RequestedState=state.FlickState; }
                if(tap>=0 || held>=0 || (surface.Id!="Seat" && hover>=0 && raw))
                {
                    if(i==0) Controls.Static.Primary.BlockUntilRelease();
                    else Controls.Static.ThrustUp.BlockUntilRelease();
                }
            }
            if(clicked>=0) Actor.Vibrate(0,.03f,100,.3f);
            return clicked;
        }
        public static IEnumerable<SurfaceView> RayViews()
        {
            if(Main.MenuOpen || !InputRouter.Gameplay || !SeatFit.Eligible(SeatFit.Seat)) yield break;
            for(int i=0;i<2;i++)
                if(showRay[i])
                {
                    var aim=SpatialUi.DeviceWorld((i==0 ? Player.HandR : Player.HandL).AimTracking);
                    Vector3D end=rayHit[i].HasValue ? Vector3D.Transform(rayHit[i].Value,SeatFit.Seat.WorldMatrix) : aim.Translation+aim.Forward*1.25;
                    Vector3D delta=end-aim.Translation;
                    if(delta.LengthSquared()<.000001) continue;
                    yield return new SurfaceView { Id="CockpitRay"+i,Style=SurfaceStyle.Pointer,
                        Pose=MatrixD.CreateWorld((aim.Translation+end)*.5,Vector3D.Normalize(delta),Math.Abs(Vector3D.Normalize(delta).Y)<.98 ? Vector3D.Up : Vector3D.Right),
                        Width=.0018f,Height=(float)delta.Length() };
                }
        }
    }
}
