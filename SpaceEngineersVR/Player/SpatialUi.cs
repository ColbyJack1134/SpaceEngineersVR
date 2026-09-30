using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SpatialUi
    {
        private static readonly SurfaceTouch wristTouch=new SurfaceTouch();
        private static readonly CockpitTouch seatInput=new CockpitTouch();
        internal static int SeatNearKey(SurfaceView surface,int hand) => seatInput.NearKey(surface,hand);
        private static volatile SurfaceView[] current=new SurfaceView[0];
        public static SurfaceView[] Current => current;
        private static SurfaceView wrist,seat;
        private static bool expanded,failed;
        public static bool Available => !failed;
        private static int wristHover=-1,wristPressed=-1,seatHover=-1,seatPressed=-1;
        private static float fold;
        private static int wristSide=1;
        private static MatrixD wristMount;
        private static object wristOwner;
        private static bool refolding;
        private static DateTime awaySince;
        private static DateTime lastUpdate;
        public static bool Pointing { get; private set; }
        private static Vector3D touchPoint;
        private static readonly Vector3[] seatDirections={ Vector3.Up,Vector3.Forward,Vector3.Down,Vector3.Left,Vector3.Zero,Vector3.Right,Vector3.Backward };
        public static MatrixD DeviceWorld(Matrix tracking)
        {
            if(SeatFit.Eligible(SeatFit.Seat))
                return (MatrixD)VrMath.Affine(tracking*Player.PlayerToAbsolute.inverted)*SeatFit.Seat.GetHeadMatrix(true,true);
            return CameraRig.DeviceWorld(tracking);
        }
        public static void Expand() { expanded=true; }
        public static void ReleaseInput()
        {
            wristTouch.Reset(); seatInput.Reset(); Pointing=false;
            FloatingKeyboard.ReleaseInput();
            wristHover=wristPressed=seatHover=seatPressed=-1;
            current=new SurfaceView[0];
        }
        public static void Reset()
        {
            expanded=false; fold=0; wrist=seat=null; current=new SurfaceView[0];
            wristOwner=null; refolding=false; awaySince=DateTime.MinValue;
            ReleaseInput();
        }
        public static void Update()
        {
            if(failed || !Player.Headset.pose.isTracked || !Player.HandR.pose.isTracked || !Player.HandL.pose.isTracked ||
                !MenuPointer.GameFocused || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu))
            { current=new SurfaceView[0]; wristTouch.Reset(); seatInput.Reset(); wrist=seat=null; Pointing=false; return; }
            var now=DateTime.UtcNow;
            float dt=(float)Math.Min(.05,Math.Max(0,(now-lastUpdate).TotalSeconds)); lastUpdate=now;
            fold=MathHelper.Clamp(fold+(expanded && !refolding ? 1 : -1)*dt*4,0,1);
            Publish();
            if(expanded && fold>=.99f && !CockpitControls.Held(Player.HandL) &&
                WristNeedsRefold(wristMount,DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation,wristSide))
            {
                if(awaySince==DateTime.MinValue) awaySince=now;
                else if((now-awaySince).TotalSeconds>.25) { refolding=true; wristTouch.Reset(); }
            }
            else awaySince=DateTime.MinValue;
            var c=Controls.Static;
            bool trigger=c.Primary.HasPressed;
            bool raw=c.Primary.RawPressed;
            Matrix aim=Player.HandR.AimTracking;
            MatrixD world=Main.WorldAvailable ? DeviceWorld(aim) : (MatrixD)aim;
            Vector3D tip=world.Translation+world.Forward*.025;
            Pointing=new[] { wrist,seat }.Any(s=>s!=null && Vector3D.Distance(s.Pose.Translation,tip)<.4);
            if(Main.WorldAvailable && Pointing && TrackedArms.TryFingertip(out var finger)) tip=finger;
            touchPoint=tip;
            wristHover=wristPressed=seatHover=seatPressed=-1;
            if(wrist!=null)
            {
                int clicked=Interact(wrist,wristTouch,world,tip,trigger,ref wristHover,ref wristPressed);
                if(clicked>=0)
                {
                    c.Primary.BlockUntilRelease(); Player.HandR.Vibrate(0,.022f,125,.28f);
                    if(fold<.1f) expanded=true;
                    else switch(clicked)
                    {
                        case 0: GameActions.Execute(GameActions.InventoryAction); break;
                        case 1: GameActions.Execute(GameActions.TerminalAction); break;
                        case 2: GameActions.Execute(GameActions.ConfigureToolbarAction); break;
                        case 3: MenuKeyboard.Open(); break;
                        case 4: GameActions.Execute(GameActions.HelmetAction); break;
                        case 5: MySession.Static?.ControlledEntity?.SwitchDamping(); break;
                        case 6: expanded=false; break;
                    }
                }
            }
            if(seat!=null && !Main.MenuOpen)
            {
                bool holding=CockpitControls.Held(Player.HandR) || CockpitControls.Held(Player.HandL);
                bool available=!WeaponHandling.ConsumesLeftGrip;
                int clicked=seatInput.Update(seat,available,wristHover>=0);
                seatHover=seatInput.Hover; seatPressed=seatInput.Held;
                if(clicked==7) CockpitControls.ToggleAdjustment();
                else if(clicked==8 && CockpitControls.Adjusting) CockpitControls.ResetPlacement();
                else if(clicked>=9) SeatPanel.Activate(clicked);
                int held=seatInput.Held;
                if(!holding && held>=0 && held<seatDirections.Length) SeatFit.Move(seatDirections[held],held==4);
            }
            else seatInput.Reset();
            // A touch owns its input while the finger is on a surface, preventing tool use.
            if(wristPressed>=0 || seatPressed>=0 || (wristHover>=0 && trigger)) c.Primary.BlockUntilRelease();
            Publish();
        }
        private static int Interact(SurfaceView s,SurfaceTouch touch,MatrixD aim,Vector3D tip,bool trigger,ref int hover,ref int pressed)
        {
            if(CockpitControls.Held(Player.HandR) || CockpitTouch.OwnsRight || WeaponHandling.ConsumesLeftGrip)
            { touch.Reset(); return -1; }
            Matrix headTracking=Player.Headset.pose.deviceToAbsolute.matrix;
            Vector3D head=s.TrackingSpace ? (Vector3D)headTracking.Translation : DeviceWorld(headTracking).Translation;
            if(Vector3D.Dot(s.Pose.Backward,head-s.Pose.Translation)<=.015)
            { touch.Reset(); return -1; }
            Vector3 local=PhysicalSurface.Point(s,tip);
            int key=s.KeyAt(PhysicalSurface.UV(s,local));
            int clicked=touch.Update(s.Id,local,key);
            if(local.Z>= -.018f && local.Z<.07f) hover=key;
            pressed=touch.Held;
            Matrix ray=(Matrix)(aim*MatrixD.Invert(s.Pose));
            if(ray.Translation.Length()<.85f && VrMath.PanelHit(ray,Matrix.Identity,s.Width,s.Height,out var uv))
            {
                int hit=s.KeyAt(uv);
                if(hover<0) hover=hit;
                if(trigger && hit>=0) { clicked=hit; pressed=hit; }
            }
            return clicked;
        }
        public static void Publish()
        {
            if(!Main.WorldAvailable)
            { wrist=seat=null; current=new SurfaceView[0]; return; }
            if(failed || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu) || !Player.HandL.pose.isTracked || !Player.Headset.pose.isTracked)
            { current=new SurfaceView[0]; return; }
            var output=new List<SurfaceView>();
            var owner=MySession.Static?.ControlledEntity;
            if(!ReferenceEquals(wristOwner,owner))
            {
                // A cockpit changes the wrist's pose frame. Reopen on the new
                // viewer-facing side rather than retaining the old seated hinge.
                wristOwner=owner; fold=0; refolding=false; awaySince=DateTime.MinValue; wristTouch.Reset();
            }
            MatrixD mount;
            if(!TrackedArms.TryWristScreen(out mount))
            {
                Matrix tracking=Matrix.CreateRotationX(-MathHelper.PiOver2)*Matrix.CreateTranslation(0,.04f,.09f)*Player.HandL.GripTracking;
                mount=Main.WorldAvailable ? DeviceWorld(tracking) : (MatrixD)tracking;
            }
            wristMount=mount;
            if(fold==0)
            {
                Vector3D head=Main.WorldAvailable ? DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation : (Vector3D)Player.Headset.pose.deviceToAbsolute.matrix.Translation;
                wristSide=Vector3D.Dot(head-mount.Translation,mount.Right)>=0 ? 1 : -1;
                refolding=false; awaySince=DateTime.MinValue;
            }
            float openWidth=.40f;
            float width=MathHelper.Lerp(.133f,openWidth,fold)*Alignment.Scale(Alignment.WristKey);
            float height=MathHelper.Lerp(.070f,openWidth*9/16,fold)*Alignment.Scale(Alignment.WristKey);
            wrist=new SurfaceView { Id="Wrist",Pose=WristPose(mount,fold,height,wristSide),Width=width,Height=height,TrackingSpace=!Main.WorldAvailable,
                Style=fold<.1f ? SurfaceStyle.WristStatus : SurfaceStyle.WristMenu,Hover=wristHover,Pressed=wristPressed };
            var status=EssentialHud.Current;
            if(fold<.1f)
            {
                wrist.Levels=status?.Levels;
                wrist.Keys=new[] { new SurfaceKey("",.01f,.01f,.98f,.98f) };
            }
            else
            {
                wrist.Keys=Enumerable.Range(0,7).Select(i=>new SurfaceKey("",.04f+(i%3)*.315f,.08f+(i/3)*.29f,.29f,.25f)).ToArray();
            }
            // Do not accept a new touch on controls while their geometry is unfolding.
            if(refolding || (fold>.01f && fold<.99f)) wrist.Keys=new SurfaceKey[0];
            output.Add(wrist);
            seat=SeatPanel.View();
            if(seat!=null)
            {
                seat.Hover=seatHover; seat.Pressed=seatPressed;
                output.Add(seat);
            }
            var point=touchPoint;
            output.AddRange(CockpitButtons.Views);
            output.AddRange(CockpitTouch.RayViews());
            if(Main.WorldAvailable && Pointing && TrackedArms.TryFingertip(out var latest)) point=latest;
            foreach(var s in output)
            {
                Vector3 local=PhysicalSurface.Point(s,point);
                if(local.Z>-.035f && local.Z<.16f && s.KeyAt(PhysicalSurface.UV(s,local))>=0)
                    s.TouchPoint=new Vector3(local.X,local.Y,Math.Max(.014f,local.Z));
            }
            current=output.ToArray();
        }
        internal static MatrixD WristPose(MatrixD mount,float fold,float height,int side)
        {
            // Landscape baseline follows the forearm (native display's long axis).
            // The long-edge hinge stays attached; its top lifts outward from the arm.
            MatrixD hinge=MatrixD.CreateRotationX(Math.PI*.5*fold)*MatrixD.CreateRotationZ(side*Math.PI*.5);
            hinge.Translation=new Vector3D(side*.035,0,0)+hinge.Up*(height*.5);
            return hinge*mount;
        }
        internal static bool WristNeedsRefold(MatrixD mount,Vector3D head,int side) =>
            Vector3D.Dot(head-mount.Translation,mount.Right)*side<-.06;
        public static void Draw(Texture2D target,MatrixD view,MatrixD projection,SurfaceView[] frame=null,bool tracking=false)
        {
            if(failed || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu) || !Player.HandL.renderPose.isTracked || !Player.HandR.renderPose.isTracked) return;
            try
            {
                var surfaces=(frame ?? current).Where(s=>s.TrackingSpace==tracking).ToArray();
                if(surfaces.Length==0) return;
                PhysicalSurface.Draw(target,surfaces,view,projection,tracking ? null : PhysicalSurface.SceneDepth());
            }
            catch(Exception ex) { failed=true; current=new SurfaceView[0]; Logger.Warning(ex,"Physical UI disabled; native menus remain available"); }
        }
    }
}
