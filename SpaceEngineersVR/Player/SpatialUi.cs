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
        private static readonly WristKnob.Turn knobTurn=new WristKnob.Turn();
        internal static bool PinchingKnob => wristTouch.Committed && heldKey?.Knob.HasValue==true && wristDirect;
        private static CockpitTouch.Hand wristTouch => wristContact.Input;
        private static volatile SurfaceView[] current=new SurfaceView[0];
        public static SurfaceView[] Current => current;
        private static SurfaceView wrist,wristMenu,seat,flightView;
        private static readonly CockpitFeedback.ValuePulse sliderPulse=new CockpitFeedback.ValuePulse();
        private static string wristHoverSurface,rayPressedSurface;
        private static SurfaceKey rayPressedKey;
        private static DateTime rayPressedUntil;
        private static bool expanded,wristDirect,wristHoverFeedback;
        private static readonly RenderRecovery recovery=new RenderRecovery("Physical UI");
        private static bool failed => recovery.Failed;
        public static bool OwnsRight => wristTouch.Consumed || seatRayPress.Held || FlightSettings.WindowCaptured;
        internal static bool ContentCaptured => wristTouch.Committed;
        internal static bool WristInUse => fold>0 || wristTouch.Surface!=null;
        public static bool RayTargeted { get; private set; }
        public static bool Available => !failed;
        private static int wristHover=-1,wristPressed=-1,seatHover=-1,seatPressed=-1;
        private static float fold;
        private static readonly InteractionPress seatRayPress=new InteractionPress();
        private static int seatRayKey=-1;
        private static float seatRayDistance;
        private static bool seatRayTargeted;
        private static object wristOwner;
        private static SurfaceKey heldKey;
        private static DateTime lastUpdate;
        public static bool Pointing { get; private set; }

        public static MatrixD DeviceWorld(Matrix tracking)
        {
            if(SeatFit.Eligible(SeatFit.Seat))
                return (MatrixD)VrMath.Affine(tracking*Player.PlayerToAbsolute.inverted)*SeatFit.Seat.GetHeadMatrix(true,true);
            return CameraRig.DeviceWorld(tracking);
        }
        private static MatrixD SurfacePose(Matrix tracking) => CameraRig.Detached || !Main.WorldAvailable ? (MatrixD)tracking:DeviceWorld(tracking);
        public static void Expand() { expanded=true; }
        public static void Collapse() { if(FlightSettings.IsOpen && FlightSettings.OnWrist) FlightSettings.Close(); expanded=false; wristTouch.Reset(); heldKey=null; wristHover=wristPressed=-1; WristPanel.StopEditing(); }
        public static bool CollapseIfOpen() { if(!expanded || WristPanel.DesktopOpen && !DesktopPointed) return false; Collapse(); return true; }
        private static DateTime desktopSeen;
        internal static bool DesktopRevealed => WristPanel.DesktopOpen && (DateTime.UtcNow-desktopSeen).TotalSeconds<1;
        private static bool DesktopPointed => (DateTime.UtcNow-desktopSeen).TotalSeconds<.15;
        internal static SurfaceKey[] WristKeys() => FlightSettings.IsOpen && FlightSettings.OnWrist ? FlightSettings.Keys():WristPanel.Keys(Sandbox.Game.Screens.Helpers.MyToolbarComponent.CurrentToolbar,
            PlacementControls.OwnsTools,MySession.Static?.ControlledEntity is Sandbox.Game.Entities.MyShipController,CameraRig.Detached,
            MySession.Static?.LocalCharacter?.JetpackComp?.TurnedOn==true,EssentialHud.Current);
        public static void ReleaseInput()
        {
            wristTouch.Reset(); seatRayPress.Block(); seatRayTargeted=false; CockpitTouch.Reset(); Pointing=RayTargeted=wristHoverFeedback=false; rayPressedKey=null;
            FloatingKeyboard.ReleaseInput();
            wristHover=wristPressed=seatHover=seatPressed=-1;
            current=new SurfaceView[0];
        }
        public static void Reset()
        {
            if(FlightSettings.IsOpen) FlightSettings.Close(); WristPanel.Reset(); expanded=false; fold=0; wrist=wristMenu=seat=null; current=new SurfaceView[0];
            wristOwner=null;
            ReleaseInput();
        }
        public static void Update()
        {
            FlightSettings.Check();
            if(failed || !Player.Headset.pose.isTracked || !Player.HandR.pose.isTracked || !Player.HandL.pose.isTracked ||
                (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu))
            { current=new SurfaceView[0]; wristTouch.Reset(); seatRayPress.Block(); seatRayTargeted=false; CockpitTouch.Reset(); wrist=wristMenu=seat=null; Pointing=RayTargeted=wristHoverFeedback=false; return; }
            var now=DateTime.UtcNow;
            float dt=(float)Math.Min(.05,Math.Max(0,(now-lastUpdate).TotalSeconds)); lastUpdate=now;
            fold=MathHelper.Clamp(fold+(expanded ? 1 : -1)*dt*4,0,1);
            Publish();
            var c=Controls.Static;
            bool trigger=c.Primary.HasPressed;
            Matrix aim=Player.HandR.AimTracking;
            MatrixD world=SurfacePose(aim);
            world.Translation+=world.Forward*.025;
            if(CameraRig.Detached && MenuHands.TryPointPose(Player.HandR.GripTracking,out var trackedPoint)) world=trackedPoint;
            else if(!CameraRig.Detached && TrackedArms.TryFreePointPose(Player.HandR,out var fingerPoint)) world=fingerPoint;
            Vector3D tip=world.Translation;
            Pointing=new[] { wrist,wristMenu,seat,flightView }.Any(s=>s!=null && Vector3D.Distance(s.Pose.Translation,tip)<.4);
            RayTargeted=false;
            int previousHover=wristHover;
            string previousSurface=wristHoverSurface;
            wristHover=wristPressed=seatHover=seatPressed=-1;
            if(wrist!=null && !FlightSettings.UpdateWindow(world))
            {
                Matrix headTracking=Player.Headset.pose.deviceToAbsolute.matrix;
                Vector3D head=wrist.TrackingSpace ? (Vector3D)headTracking.Translation : DeviceWorld(headTracking).Translation;
                var target=flightView ?? WristTarget(wrist,wristMenu,world,head,wristTouch.Surface);
                var rayTarget=WristRayTarget(flightView!=null ? new[] {flightView}:new[] {wrist,wristMenu},world,3,out _,out float rayDistance);
                if(rayTarget!=null && !rayTarget.WindowPose.HasValue && !wrist.TrackingSpace && HandInteraction.ObstacleDistance(world,rayDistance)+.005f<rayDistance) rayTarget=null;
                RayTargeted=rayTarget!=null;
                if(wristMenu?.Desktop==true)
                {
                    var local=PhysicalSurface.Point(wristMenu,tip);
                    if(rayTarget==wristMenu || Math.Abs(local.X)<wristMenu.Width/2+.02f && Math.Abs(local.Y)<wristMenu.Height/2+.02f && local.Z>-.01f && local.Z<.08f) desktopSeen=now;
                }
                bool direct=DirectKey(target,world,head,out _)>=0;
                if(wristTouch.Surface==null && !direct && rayTarget!=null) target=rayTarget;
                wristHoverSurface=target.Id;
                int clicked=Interact(target,world,ref wristHover,ref wristPressed);
                bool feedback=wristHover>=0 && (target.Style!=SurfaceStyle.WristStatus || direct || rayTarget==target && rayDistance<=.12f);
                if(feedback && (!wristHoverFeedback || wristHover!=previousHover || wristHoverSurface!=previousSurface) && !wristTouch.Consumed) CockpitFeedback.Hover(Player.HandR);
                wristHoverFeedback=feedback;
                if(target==wristMenu && WristPanel.SeatOpen && fold>=.99f)
                    SeatPanel.UpdateInput(clicked>=0 ? target.Keys[clicked].SeatControl : -1,
                        wristTouch.Committed && wristTouch.Surface==target.Id ? heldKey?.SeatControl ?? -1 : -1,true);
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
                    { var key=target.Keys[clicked]; GameActions.Execute(key.Action,Player.HandR,key.SearchResult); }
                }
            }
            if(seat!=null && !Main.MenuOpen)
            {
                var input=CockpitTouch.Read("Seat");
                int clicked=input.Pressed ? input.Held : -1;
                var actor=input.Actor;
                seatHover=input.Hover; seatPressed=input.Held;
                bool firing=c.Primary.IsPressed && !c.Primary.HasPressed && !seatRayPress.Held;
                bool rayFree=!firing && !RayTargeted && !CockpitControls.Held(Player.HandR) && !CockpitTouch.OwnsRight && !FloatingWindows.OwnsInput &&
                    !wristTouch.Consumed && !TouchScreenBridge.OwnsInput && !ArthurLcdBridge.OwnsInput &&
                    !WeaponHandling.ConsumesLeftGrip && !Main.MenuOpen;
                int rayKey=-1;
                seatRayTargeted=rayFree && Vector3D.Dot(seat.Pose.Backward,DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix).Translation-seat.Pose.Translation)>.015 &&
                    WristRayTarget(new[] {seat},world,3,out rayKey,out seatRayDistance)!=null &&
                    HandInteraction.ObstacleDistance(world,seatRayDistance)+.005f>=seatRayDistance;
                if(input.Hover>=0) seatRayTargeted=false;
                if(!seatRayTargeted || rayKey!=seatRayKey) seatRayPress.Block();
                seatRayKey=rayKey;
                var rayInput=InteractionInput.Read(Player.HandR,false);
                seatRayPress.Update(seatRayTargeted && rayKey>=0,rayInput);
                if(seatRayTargeted)
                {
                    seatHover=rayKey;
                    if(rayInput.Down) rayInput.Consume();
                    if(seatRayPress.Held) seatPressed=rayKey;
                    if(seatRayPress.Pressed) { clicked=rayKey; actor=Player.HandR; }
                }
                if(clicked>=0) CockpitFeedback.Click(actor);
                if(!(wristTouch.Committed && heldKey?.SeatControl>=0)) SeatPanel.UpdateInput(clicked>=0 ? seat.Keys[clicked].SeatControl:-1,seatPressed>=0 ? seat.Keys[seatPressed].SeatControl:-1);
            }
            else { seatRayPress.Block(); seatRayTargeted=false; }
            // A touch owns its input while the finger is on a surface, preventing tool use.
            if(wristTouch.Consumed) InteractionInput.Read(Player.HandR,wristDirect).Consume();
            if(seatPressed>=0 || (wristHover>=0 && trigger)) c.Primary.BlockUntilRelease();
            Publish();
        }
        private static int Interact(SurfaceView s,MatrixD pointer,ref int hover,ref int pressed)
        {
            var c=Controls.Static;
            if(wristTouch.Surface!=null && wristTouch.Surface!=s.Id) wristTouch.Reset();
            bool free=!CockpitControls.Held(Player.HandR) && !CockpitTouch.OwnsRight && !FloatingWindows.OwnsInput && !WeaponHandling.ConsumesLeftGrip;
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
                reachable:!held || heldKey?.Slider.HasValue==true || !wristDirect || wristContact.Reachable(local.Translation),guarded:key>=0,softCapture:false);
            if(wristTouch.Captured)
            {
                wristDirect=direct;
                heldKey=s.Keys[wristTouch.Held];
                var b=heldKey.Bounds;
                wristContact.Capture(local,heldKey.Knob.HasValue ? WristKnob.Center(s):new Vector3((b.Center.X-.5f)*s.Width,(.5f-b.Center.Y)*s.Height,.001f));
                if(heldKey.Slider.HasValue) sliderPulse.Reset(heldKey.Slider.Value);
                if(heldKey.Knob.HasValue) knobTurn.Begin(local,Common.Config.WristSignalTint/.75f);
            }
            pressed=wristTouch.Committed ? wristTouch.Held:-1;
            if(pressed>=0 && heldKey?.Knob.HasValue==true && wristDirect)
            {
                knobTurn.Move(local);
                Common.Config.WristSignalTint=knobTurn.Value*.75f;
                heldKey.Knob=knobTurn.Value;
                wristContact.Wrist=knobTurn.Wrist;
            }
            if(pressed>=0 && heldKey?.Slider.HasValue==true)
            {
                Vector3 point=PhysicalSurface.Point(s,pointer.Translation);
                if(!wristDirect && WristRay(s,pointer,out float distance)) point=PhysicalSurface.Point(s,pointer.Translation+pointer.Forward*distance);
                var uv=PhysicalSurface.UV(s,point); var bounds=heldKey.Bounds;
                float inset=10f/1024,amount=MathHelper.Clamp((uv.X-bounds.X-inset)/(bounds.Width-2*inset),0,1);
                heldKey.Change?.Invoke(amount);
                wristContact.Anchor=new Vector3(((bounds.X+inset+(bounds.Width-2*inset)*amount)-.5f)*s.Width,(.5f-(bounds.Y+bounds.Height*.76f))*s.Height,.001f);
                if(sliderPulse.Sample(amount,DateTime.UtcNow)) CockpitFeedback.Activate(Player.HandR,.09f,.009f);
            }
            if(wristTouch.Surface!=null) hover=wristTouch.Held;
            return wristTouch.Pressed ? pressed : -1;
        }
        internal static bool TryWristAttachment(out MatrixD pose,out Vector3D contact,out float blend,bool render=false)
        {
            pose=MatrixD.Identity; contact=Vector3D.Zero; blend=0;
            var panel=wristTouch.Surface==flightView?.Id ? flightView:wristTouch.Surface==wristMenu?.Id ? wristMenu : wrist;
            if(panel==null || !wristDirect) return false;
            var parent=render && panel.TrackingSpace && panel.HandLocal.HasValue ? panel.HandLocal.Value*Player.HandL.RenderGripTracking : panel.Pose;
            if(panel!=flightView && !panel.TrackingSpace && TrackedArms.TryWristScreen(out var mount))
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
            if(!(CameraRig.Detached ? MenuHands.TryWristMount(Player.HandL.GripTracking,out mount):TrackedArms.TryWristScreen(out mount)))
            {
                Matrix tracking=Matrix.CreateRotationX(-MathHelper.PiOver2)*Matrix.CreateTranslation(0,.04f,.09f)*Player.HandL.GripTracking;
                mount=SurfacePose(tracking);
            }
            if(wristTouch.Surface==null) heldKey=null;
            var panels=WristViews(mount,fold,Alignment.Scale(Alignment.WristKey),EssentialHud.Current,fold>=.99f ? WristKeys() : new SurfaceKey[0]);
            wrist=panels[0]; wristMenu=panels.Length>1 ? panels[1] : null;
            foreach(var panel in panels)
            {
                panel.TrackingSpace=CameraRig.Detached;
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
            flightView=FlightSettings.Floating();
            if(flightView!=null)
            {
                if(flightView.Id==wristHoverSurface) {flightView.Hover=wristHover; flightView.Pressed=wristPressed;}
                if(wristTouch.Committed && wristTouch.Surface==flightView.Id && heldKey!=null)
                {flightView.Keys=HoldKey(flightView.Keys,heldKey,out int held); flightView.Hover=flightView.Pressed=held;}
                output.Add(flightView);
            }
            seat=CameraRig.Detached || FlightSettings.IsOpen ? null:SeatPanel.View();
            if(seat!=null)
            {
                seat.Hover=seatHover; seat.Pressed=seatPressed;
                output.Add(seat);
            }
            if(!CameraRig.Detached) output.AddRange(CockpitButtons.Views);
            if(!CameraRig.Detached) output.AddRange(CockpitTouch.Labels());
            if(!CameraRig.Detached) output.AddRange(HandInteraction.Labels());
            if(BlockInspection.Current!=null) output.Add(BlockInspection.Current);
            var ammo=WeaponAmmo.View(); if(ammo!=null) output.Add(ammo);
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
                Keys=fold>=.99f ? keys : new SurfaceKey[0],SignalWindow=fold>=.99f && WristPanel.Inspecting,
                SeatSettings=fold>=.99f && WristPanel.SeatOpen && !FlightSettings.IsOpen,
                FlightPage=fold>=.99f && FlightSettings.IsOpen && FlightSettings.OnWrist,Title=FlightSettings.IsOpen ? FlightSettings.Title:null,
                Levels=WristPanel.SeatOpen ? SeatPanel.States():null,Handle=CockpitControls.Adjusting ? 1:0,
                HudSettings=fold>=.99f && WristPanel.HudOpen ? WristHud.Current:null,Desktop=fold>=.99f && WristPanel.DesktopOpen };
            if(menu.Desktop) DesktopCapture.Request();
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
                target=panel; key=index>=0 && panel.Keys[index].Enabled && !panel.Keys[index].DirectOnly ? index : -1; distance=hit;
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
        internal static SurfaceView RenderSurface(SurfaceView s,MatrixD? trackingToWorld)
        {
            if(!s.TrackingSpace) return s;
            return s.At((s.HandLocal.HasValue ? s.HandLocal.Value*Player.HandL.RenderGripTracking:s.Pose)*(trackingToWorld ?? MatrixD.Identity));
        }
        internal static SurfaceView RaySurface(MatrixD aim,float distance) => new SurfaceView {
            Id="WristRay",Style=SurfaceStyle.Pointer,Width=.002f,Height=distance,
            Pose=MatrixD.CreateWorld(aim.Translation+aim.Forward*distance*.5,aim.Forward,aim.Up) };
        internal static void DrawFloating(Texture2D target,MatrixD view,MatrixD projection,SurfaceView[] frame)
        {
            foreach(var surface in (frame ?? new SurfaceView[0]).Where(s=>s.WindowPose.HasValue))
            {
                var panel=surface.At(surface.WindowPose.Value);
                var panels=new[] {panel};
                if((RayTargeted || panel.WindowHover>0) && (!OwnsRight || !wristDirect) && TrackedArms.TryFreePointPose(Player.HandR,out var worldAim))
                {
                    var aim=worldAim*MatrixD.Invert(SpatialUi.DeviceWorld(Matrix.Identity));
                    if(WristRay(panel,aim,out float distance)) panels=panels.Concat(new[] {RaySurface(aim,distance)}).ToArray();
                }
                FloatingSurface.Draw(target,panels,view,projection);
                WindowFrame.Draw(target,"FlightSettings",new WindowFrame.Snapshot {Pose=(Matrix)panel.Pose,Width=panel.Width,Height=panel.Height,BarOffset=.035f,Hover=panel.WindowHover},view,projection,NativeHandLayer.Depth);
            }
        }
        public static void Draw(Texture2D target,MatrixD view,MatrixD projection,SurfaceView[] frame=null,bool tracking=false,MatrixD? trackingToWorld=null)
        {
            if(failed || (!InputRouter.Gameplay && InputRouter.Mode!=InputMode.Menu) || !Player.HandL.renderPose.isTracked || !Player.HandR.renderPose.isTracked) return;
            try
            {
                var surfaces=(frame ?? current).Where(s=>!s.WindowPose.HasValue && s.TrackingSpace==tracking).ToArray();
                if(surfaces.Length==0) return;
                if(tracking) surfaces=surfaces.Select(s=>RenderSurface(s,trackingToWorld)).ToArray();
                surfaces=surfaces.Select(s=>s.SignalWindow ? WristSignals.Apply(s,WorldMarkers.RenderSnapshot,WorldMarkers.RenderHead,WorldMarkers.WristOptions,DateTime.UtcNow):s).ToArray();
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
                        var ray=RaySurface(aim,distance);
                        surfaces=surfaces.Concat(new[] {ray}).ToArray();
                    }
                }
                if(!tracking && pointing && seatRayTargeted && !CockpitTouch.OwnsRight &&
                    (HandInteraction.ShowRay(controls.PointerPressure.RawPosition.X,false) || controls.Primary.RawPressed))
                    surfaces=surfaces.Concat(new[] {RaySurface(aim,seatRayDistance)}).ToArray();
                PhysicalSurface.Draw(target,surfaces,view,projection,tracking && !trackingToWorld.HasValue ? MenuHands.Depth : PhysicalSurface.SceneDepth());
                recovery.Succeeded();
            }
            catch(Exception ex) { current=new SurfaceView[0]; recovery.Fail(ex,"Physical UI disabled; native menus remain available"); }
        }
    }
}
