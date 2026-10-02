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
        private static readonly CockpitTouch.SurfaceHold wristContact=new CockpitTouch.SurfaceHold();
        private static CockpitTouch.Hand wristTouch => wristContact.Input;
        private static volatile SurfaceView[] current=new SurfaceView[0];
        public static SurfaceView[] Current => current;
        private static SurfaceView wrist,wristMenu,seat;
        private static string wristHoverSurface,rayPressedSurface;
        private static SurfaceKey rayPressedKey;
        private static DateTime rayPressedUntil;
        private static bool expanded,failed,wristDirect,wristHoverFeedback;
        public static bool OwnsRight => wristTouch.Consumed;
        public static bool RayTargeted { get; private set; }
        public static bool Available => !failed;
        private static int wristHover=-1,wristPressed=-1,seatHover=-1,seatPressed=-1;
        private static float fold;
        private static object wristOwner;
        private static SurfaceKey heldKey;
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
        private static MatrixD SurfacePose(Matrix tracking) => ThirdPersonView.Active || !Main.WorldAvailable ? (MatrixD)tracking:DeviceWorld(tracking);
        public static void Expand() { expanded=true; }
        public static void Collapse() { expanded=false; wristTouch.Reset(); heldKey=null; wristHover=wristPressed=-1; WristPanel.StopEditing(); }
        public static bool CollapseIfOpen() { if(!expanded) return false; Collapse(); return true; }
        internal static SurfaceKey[] WristKeys() => WristPanel.Keys(Sandbox.Game.Screens.Helpers.MyToolbarComponent.CurrentToolbar,
            PlacementControls.OwnsTools,MySession.Static?.ControlledEntity is Sandbox.Game.Entities.MyShipController,ThirdPersonView.Active,
            MySession.Static?.LocalCharacter?.JetpackComp?.TurnedOn==true,EssentialHud.Current);
        public static void ReleaseInput()
        {
            wristTouch.Reset(); CockpitTouch.Reset(); Pointing=RayTargeted=wristHoverFeedback=false; rayPressedKey=null;
            FloatingKeyboard.ReleaseInput();
            wristHover=wristPressed=seatHover=seatPressed=-1;
            current=new SurfaceView[0];
        }
        public static void Reset()
        {
            WristPanel.Reset(); expanded=false; fold=0; wrist=wristMenu=seat=null; current=new SurfaceView[0];
            wristOwner=null;
            ReleaseInput();
        }
        public static void Update()
        {
            if(failed || !Player.Headset.pose.isTracked || !Player.HandR.pose.isTracked || !Player.HandL.pose.isTracked ||
                !MenuPointer.GameFocused || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu))
            { current=new SurfaceView[0]; wristTouch.Reset(); CockpitTouch.Reset(); wrist=wristMenu=seat=null; Pointing=RayTargeted=wristHoverFeedback=false; return; }
            var now=DateTime.UtcNow;
            float dt=(float)Math.Min(.05,Math.Max(0,(now-lastUpdate).TotalSeconds)); lastUpdate=now;
            fold=MathHelper.Clamp(fold+(expanded ? 1 : -1)*dt*4,0,1);
            Publish();
            var c=Controls.Static;
            bool trigger=c.Primary.HasPressed;
            Matrix aim=Player.HandR.AimTracking;
            MatrixD world=SurfacePose(aim);
            world.Translation+=world.Forward*.025;
            if(ThirdPersonView.Active && MenuHands.TryPointPose(Player.HandR.GripTracking,out var trackedPoint)) world=trackedPoint;
            else if(!ThirdPersonView.Active && TrackedArms.TryFreePointPose(Player.HandR,out var fingerPoint)) world=fingerPoint;
            Vector3D tip=world.Translation;
            Pointing=new[] { wrist,wristMenu,seat }.Any(s=>s!=null && Vector3D.Distance(s.Pose.Translation,tip)<.4);
            touchPoint=tip;
            RayTargeted=false;
            int previousHover=wristHover;
            string previousSurface=wristHoverSurface;
            wristHover=wristPressed=seatHover=seatPressed=-1;
            if(wrist!=null)
            {
                Matrix headTracking=Player.Headset.pose.deviceToAbsolute.matrix;
                Vector3D head=wrist.TrackingSpace ? (Vector3D)headTracking.Translation : DeviceWorld(headTracking).Translation;
                var target=WristTarget(wrist,wristMenu,world,head,wristTouch.Surface);
                var rayTarget=WristRayTarget(new[] {wrist,wristMenu},world,3,out _,out float rayDistance);
                if(rayTarget!=null && !wrist.TrackingSpace && HandInteraction.ObstacleDistance(world,rayDistance)+.005f<rayDistance) rayTarget=null;
                RayTargeted=rayTarget!=null;
                bool direct=DirectKey(target,world,head,out _)>=0;
                if(wristTouch.Surface==null && !direct && rayTarget!=null) target=rayTarget;
                wristHoverSurface=target.Id;
                int clicked=Interact(target,world,ref wristHover,ref wristPressed);
                bool feedback=wristHover>=0 && (target.Style!=SurfaceStyle.WristStatus || direct || rayTarget==target && rayDistance<=.12f);
                if(feedback && (!wristHoverFeedback || wristHover!=previousHover || wristHoverSurface!=previousSurface) && !wristTouch.Consumed) CockpitFeedback.Hover(Player.HandR);
                wristHoverFeedback=feedback;
                if(clicked>=0)
                {
                    InteractionInput.Read(Player.HandR,wristDirect).Consume(); CockpitFeedback.Click(Player.HandR);
                    if(!wristDirect)
                    {
                        rayPressedSurface=target.Id; rayPressedKey=target.Keys[clicked];
                        rayPressedUntil=now.AddSeconds(.12);
                    }
                    if(target==wrist)
                    {
                        expanded=!expanded;
                        if(!expanded) WristPanel.StopEditing();
                    }
                    else if(clicked<target.Keys.Length && target.Keys[clicked].Action!=null)
                    { var action=target.Keys[clicked].Action; GameActions.Execute(action); }
                }
            }
            if(seat!=null && !Main.MenuOpen)
            {
                bool holding=CockpitControls.Held(Player.HandR) || CockpitControls.Held(Player.HandL);
                var input=CockpitTouch.Read("Seat");
                int clicked=input.Pressed ? input.Held : -1;
                seatHover=input.Hover; seatPressed=input.Held;
                if(clicked>=0) CockpitFeedback.Click(input.Actor);
                if(clicked==7) CockpitControls.ToggleAdjustment();
                else if(clicked==8 && CockpitControls.Adjusting) CockpitControls.ResetPlacement();
                else if(clicked>=9) SeatPanel.Activate(clicked);
                int held=input.Held;
                if(!holding && held>=0 && held<seatDirections.Length) SeatFit.Move(seatDirections[held],held==4);
            }
            // A touch owns its input while the finger is on a surface, preventing tool use.
            if(wristTouch.Consumed) InteractionInput.Read(Player.HandR,wristDirect).Consume();
            if(seatPressed>=0 || (wristHover>=0 && trigger)) c.Primary.BlockUntilRelease();
            Publish();
        }
        private static int Interact(SurfaceView s,MatrixD pointer,ref int hover,ref int pressed)
        {
            var c=Controls.Static;
            if(wristTouch.Surface!=null && wristTouch.Surface!=s.Id) wristTouch.Reset();
            bool free=!CockpitControls.Held(Player.HandR) && !CockpitTouch.OwnsRight && !RemoteView.OwnsInput && !WeaponHandling.ConsumesLeftGrip;
            Matrix headTracking=Player.Headset.pose.deviceToAbsolute.matrix;
            Vector3D head=s.TrackingSpace ? (Vector3D)headTracking.Translation : DeviceWorld(headTracking).Translation;
            bool front=Vector3D.Dot(s.Pose.Backward,head-s.Pose.Translation)>.015;
            var probe=new CockpitProbe(pointer).Transform(MatrixD.Invert(s.Pose));
            int key=free && front ? CockpitTouch.NearKey(s,probe,out _,out _) : -1;
            bool direct=key>=0;
            if(key<0 && free && RayTargeted && WristRay(s,pointer,out _))
                WristRayTarget(new[] {s},pointer,3,out key,out _);
            hover=key;
            MatrixD rawWrist=s.TrackingSpace ? Alignment.Apply(Alignment.HandKey(Player.HandR),CockpitHandPose.GripWrist(Player.HandR.GripTracking)) : TrackedArms.FreeWristWorld(Player.HandR);
            Matrix local=(Matrix)(rawWrist*MatrixD.Invert(s.Pose));
            bool held=wristTouch.Surface==s.Id;
            if(!wristTouch.Consumed) wristDirect=direct;
            wristTouch.Sample(free,InteractionInput.Read(Player.HandR,wristDirect),key>=0 ? s.Id:null,key,
                reachable:!held || !wristDirect || wristContact.Reachable(local.Translation),guarded:key>=0,softCapture:false);
            if(wristTouch.Captured)
            {
                wristDirect=direct;
                heldKey=s.Keys[wristTouch.Held];
                var b=heldKey.Bounds;
                wristContact.Capture(local,new Vector3((b.Center.X-.5f)*s.Width,(.5f-b.Center.Y)*s.Height,.001f));
            }
            pressed=wristTouch.Committed ? wristTouch.Held:-1;
            if(wristTouch.Surface!=null) hover=wristTouch.Held;
            return wristTouch.Pressed ? pressed : -1;
        }
        internal static bool TryWristAttachment(out MatrixD pose,out Vector3D contact,out float blend,bool render=false)
        {
            pose=MatrixD.Identity; contact=Vector3D.Zero; blend=0;
            var panel=wristTouch.Surface==wristMenu?.Id ? wristMenu : wrist;
            if(panel==null || !wristDirect) return false;
            var parent=render && panel.TrackingSpace && panel.HandLocal.HasValue ? panel.HandLocal.Value*Player.HandL.RenderGripTracking : panel.Pose;
            if(!panel.TrackingSpace && TrackedArms.TryWristScreen(out var mount))
                parent=WristAttachmentParent(panel,mount);
            return wristContact.Attachment(parent,out pose,out contact,out blend);
        }
        internal static MatrixD WristAttachmentParent(SurfaceView panel,MatrixD mount) => WristPose(mount,panel.Style==SurfaceStyle.WristStatus ? 0:1,panel.Height,-1);
        public static void Publish()
        {
            if(!Main.WorldAvailable)
            { wrist=wristMenu=seat=null; current=new SurfaceView[0]; return; }
            if(failed || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu) || !Player.HandL.pose.isTracked || !Player.Headset.pose.isTracked)
            { current=new SurfaceView[0]; return; }
            var output=new List<SurfaceView>();
            var owner=MySession.Static?.ControlledEntity;
            if(!ReferenceEquals(wristOwner,owner))
            {
                WristPanel.Reset(); wristOwner=owner; fold=0; wristTouch.Reset();
            }
            MatrixD mount;
            if(!(ThirdPersonView.Active ? MenuHands.TryWristMount(Player.HandL.GripTracking,out mount):TrackedArms.TryWristScreen(out mount)))
            {
                Matrix tracking=Matrix.CreateRotationX(-MathHelper.PiOver2)*Matrix.CreateTranslation(0,.04f,.09f)*Player.HandL.GripTracking;
                mount=SurfacePose(tracking);
            }
            if(wristTouch.Surface==null) heldKey=null;
            var panels=WristViews(mount,fold,Alignment.Scale(Alignment.WristKey),EssentialHud.Current,fold>=.99f ? WristKeys() : new SurfaceKey[0]);
            wrist=panels[0]; wristMenu=panels.Length>1 ? panels[1] : null;
            foreach(var panel in panels)
            {
                panel.TrackingSpace=ThirdPersonView.Active;
                if(panel.TrackingSpace) panel.HandLocal=panel.Pose*MatrixD.Invert(Player.HandL.GripTracking);
                if(panel.Id==wristHoverSurface) { panel.Hover=wristHover; panel.Pressed=wristPressed; }
                if(wristTouch.Committed && wristTouch.Surface==panel.Id && heldKey!=null)
                {
                    panel.Keys=HoldKey(panel.Keys,heldKey,out int held);
                    panel.Hover=panel.Pressed=held;
                }
                if(panel.Pressed<0 && panel.Id==rayPressedSurface && rayPressedKey!=null && DateTime.UtcNow<rayPressedUntil)
                    panel.Pressed=Array.FindIndex(panel.Keys,k=>k.Label==rayPressedKey.Label && k.Bounds.Equals(rayPressedKey.Bounds));
                output.Add(panel);
            }
            seat=ThirdPersonView.Active ? null:SeatPanel.View();
            if(seat!=null)
            {
                seat.Hover=seatHover; seat.Pressed=seatPressed;
                output.Add(seat);
            }
            var point=touchPoint;
            if(!ThirdPersonView.Active) output.AddRange(CockpitButtons.Views);
            if(!ThirdPersonView.Active) output.AddRange(CockpitTouch.Labels());
            if(!ThirdPersonView.Active) output.AddRange(HandInteraction.Labels());
            if(BlockInspection.Current!=null) output.Add(BlockInspection.Current);
            foreach(var s in output)
            {
                Vector3 local=PhysicalSurface.Point(s,point);
                if(s.Style!=SurfaceStyle.WristStatus && s.Style!=SurfaceStyle.WristMenu && wristTouch.Surface==null && s.Pressed<0 && local.Z>-.035f && local.Z<.16f && s.KeyAt(PhysicalSurface.UV(s,local))>=0)
                    s.TouchPoint=new Vector3(local.X,local.Y,Math.Max(.014f,local.Z));
            }
            current=output.ToArray();
        }
        internal static SurfaceView[] WristViews(MatrixD mount,float fold,float scale,EssentialHud.View status,SurfaceKey[] keys)
        {
            var compact=new SurfaceView { Id="Wrist",Style=SurfaceStyle.WristStatus,
                Width=.133f*scale,Height=.070f*scale,Pose=WristPose(mount,0,.070f*scale,-1),
                Levels=status?.Levels,Status=status,Keys=new[] {new SurfaceKey("",.01f,.01f,.98f,.98f)} };
            if(fold<=0) return new[] {compact};
            float opening=MathHelper.SmoothStep(0,1,MathHelper.Clamp(fold,0,1));
            float height=.225f*scale*opening;
            var menu=new SurfaceView { Id="WristMenu",Style=SurfaceStyle.WristMenu,
                Width=MathHelper.Lerp(.133f,.40f,opening)*scale,Height=height,Pose=WristPose(mount,1,height,-1),
                Keys=fold>=.99f ? keys : new SurfaceKey[0] };
            return new[] {compact,menu};
        }
        private static int DirectKey(SurfaceView panel,MatrixD pointer,Vector3D head,out float distance)
        {
            distance=float.MaxValue;
            if(panel==null || Vector3D.Dot(panel.Pose.Backward,head-panel.Pose.Translation)<=.015) return -1;
            return CockpitTouch.NearKey(panel,new CockpitProbe(pointer).Transform(MatrixD.Invert(panel.Pose)),out distance,out _);
        }
        internal static SurfaceView WristRayTarget(IEnumerable<SurfaceView> panels,MatrixD pointer,float maximum,out int key,out float distance)
        {
            SurfaceView target=null; key=-1; distance=maximum;
            foreach(var panel in panels)
            {
                if(panel==null || !WristRay(panel,pointer,out float hit) || hit>distance) continue;
                var local=PhysicalSurface.Point(panel,pointer.Translation+pointer.Forward*hit);
                int index=panel.KeyAt(PhysicalSurface.UV(panel,local));
                target=panel; key=index>=0 && panel.Keys[index].Enabled ? index : -1; distance=hit;
            }
            return target;
        }
        internal static SurfaceView WristTarget(SurfaceView compact,SurfaceView menu,MatrixD pointer,Vector3D head,string held)
        {
            if(held==compact.Id) return compact;
            if(menu!=null && held==menu.Id) return menu;
            var target=compact;
            float nearest=float.MaxValue;
            foreach(var panel in new[] {compact,menu})
            {
                if(DirectKey(panel,pointer,head,out float distance)>=0 && distance<nearest)
                { target=panel; nearest=distance; }
            }
            return target;
        }
        internal static SurfaceKey[] HoldKey(SurfaceKey[] keys,SurfaceKey held,out int index)
        {
            // Keep the original physical button until release, even if its action changed the page.
            index=Array.FindIndex(keys,k=>k.Bounds==held.Bounds);
            if(index<0) { index=keys.Length; keys=keys.Concat(new[] {held}).ToArray(); }
            else keys[index]=held;
            return keys;
        }
        internal static bool WristRay(SurfaceView panel,MatrixD aim,out float distance)
        {
            distance=0;
            var local=aim*MatrixD.Invert(panel.Pose);
            double z=PhysicalSurface.KeyHeight(panel);
            if(local.Translation.Z<=z || local.Forward.Z>=-.00001) return false;
            double d=(z-local.Translation.Z)/local.Forward.Z;
            if(d<=0 || d>3) return false;
            var hit=local.Translation+local.Forward*d;
            if(Math.Abs(hit.X)>panel.Width/2 || Math.Abs(hit.Y)>panel.Height/2) return false;
            distance=(float)d; return true;
        }
        internal static MatrixD WristPose(MatrixD mount,float fold,float height,int side)
        {
            // Landscape baseline follows the forearm (native display's long axis).
            // The long-edge hinge stays attached; its top lifts outward from the arm.
            MatrixD hinge=MatrixD.CreateRotationX(Math.PI*.5*fold)*MatrixD.CreateRotationZ(side*Math.PI*.5);
            hinge.Translation=new Vector3D(side*.035,0,0)+hinge.Up*(height*.5);
            mount.Translation-=mount.Backward*(.0035*(1-fold));
            return MatrixD.CreateRotationZ(Math.PI*fold)*MatrixD.CreateRotationY(Math.PI*fold)*MatrixD.CreateRotationZ(Math.PI)*hinge*mount;
        }
        public static void Draw(Texture2D target,MatrixD view,MatrixD projection,SurfaceView[] frame=null,bool tracking=false,MatrixD? trackingToWorld=null)
        {
            if(failed || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu) || !Player.HandL.renderPose.isTracked || !Player.HandR.renderPose.isTracked) return;
            try
            {
                var surfaces=(frame ?? current).Where(s=>s.TrackingSpace==tracking).ToArray();
                if(surfaces.Length==0) return;
                if(tracking) surfaces=surfaces.Select(s=>s.At((s.HandLocal.HasValue ? s.HandLocal.Value*Player.HandL.RenderGripTracking:s.Pose)*(trackingToWorld ?? MatrixD.Identity))).ToArray();
                MatrixD aim;
                bool pointing=tracking ? MenuHands.TryPointPose(Player.HandR.RenderGripTracking,out aim) : TrackedArms.TryFreePointPose(Player.HandR,out aim);
                var controls=Controls.Static;
                if(pointing && RayTargeted && (!OwnsRight || !wristDirect) && !CockpitTouch.OwnsRight && !HandInteraction.HoldingRight &&
                    (HandInteraction.ShowRay(controls.PointerPressure.RawPosition.X,false) || controls.Primary.RawPressed))
                {
                    if(tracking) aim*=trackingToWorld ?? MatrixD.Identity;
                    float distance=float.MaxValue;
                    foreach(var panel in surfaces.Where(s=>s.Style==SurfaceStyle.WristStatus || s.Style==SurfaceStyle.WristMenu))
                        if(WristRay(panel,aim,out float hit)) distance=Math.Min(distance,hit);
                    if(distance<float.MaxValue)
                    {
                        var ray=new SurfaceView { Id="WristRay",Style=SurfaceStyle.Pointer,Width=.002f,Height=distance,
                            Pose=MatrixD.CreateWorld(aim.Translation+aim.Forward*distance*.5,aim.Forward,aim.Up) };
                        surfaces=surfaces.Concat(new[] {ray}).ToArray();
                    }
                }
                PhysicalSurface.Draw(target,surfaces,view,projection,tracking && !trackingToWorld.HasValue ? MenuHands.Depth : PhysicalSurface.SceneDepth());
            }
            catch(Exception ex) { failed=true; current=new SurfaceView[0]; Logger.Warning(ex,"Physical UI disabled; native menus remain available"); }
        }
    }
}
