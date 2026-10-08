using System;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.GameSystems;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SpaceEngineers.Game.Entities.Blocks;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class RemoteView
    {
        internal sealed class View
        {
            // Raw tracking space in metres, shared by drawing, contact and window manipulation.
            public MatrixD Pose;
            public float Width,Height;
            public int Hover;
            public int SignalMode;
            public int Ammo=-1,Capacity;
            public long Source;
            public Vector3D? RayStart,RayEnd;
            public bool LeftHand;
        }
        private static readonly MenuWindow window=new MenuWindow { BarOffset=.035f,TopMargin=.08f };
        private static readonly WindowInteraction interaction=new WindowInteraction(window);
        private static readonly Action<MyLargeTurretBase,float> turretZoom=AccessTools.MethodDelegate<Action<MyLargeTurretBase,float>>(AccessTools.Method(typeof(MyLargeTurretBase),"ChangeZoomPrecise"));
        private static object owner,source;
        private static MyCockpit seat;
        private static DateTime last;
        private static int epoch=100000;
        private static string key;
        private static string world;
        private static bool placementDirty;
        private static bool recenter;
        private static Vector3D? rayStart,rayEnd;
        private static int lastHover;
        public static bool Pointing => Current?.Hover>0 || interaction.DirectHeld;
        internal static bool PointingFor(Controller hand) => hand==Hand && Pointing;
        private static bool left;
        private static MyCameraBlock pendingCamera;
        private static readonly CameraSwitch cameraSwitch=new CameraSwitch();
        internal sealed class CameraSwitch
        {
            private object turret,previous;
            private DateTime until;
            private Action activate;
            internal bool Pending => activate!=null;
            internal void Begin(object from,object to,DateTime now,Action action)
            { turret=from; previous=to; until=now.AddSeconds(5); activate=action; }
            internal void Cancel() { turret=previous=null; activate=null; }
            internal void Update(object controlled,bool available,DateTime now)
            {
                if(activate==null) return;
                if(!available || now>until || controlled!=turret && controlled!=previous) { Cancel(); return; }
                if(controlled!=previous) return;
                var action=activate; Cancel(); action();
            }
        }
        private static Controller Hand => left ? Player.HandL:Player.HandR;
        internal static bool TryAttachment(Controller hand,out MatrixD wrist,out Vector3D point,out float blend,bool tracking=false) =>
            interaction.TryAttachment(hand,PhysicalTrackingToWorld,tracking,out wrist,out point,out blend) & Current!=null;
        private static MatrixD Aim(Controller hand) => WindowInteraction.Aim(hand,PhysicalTrackingToWorld);
        private static bool Free(Controller hand) => hand.pose.isTracked && !CockpitControls.Held(hand) && !DesktopWindow.OwnsInput &&
            (hand==Player.HandL ? !CockpitTouch.Owns(hand) : !SpatialUi.OwnsRight && !CockpitTouch.OwnsRight);

        public static View Current { get; private set; }
        public static CameraRig.Frame SeatedRig { get; private set; }
        public static bool Active => owner!=null;
        public static bool RemoteGrid => MySession.Static?.ControlledEntity is MyRemoteControl;
        public static bool OwnsInput { get; private set; }
        public static bool Turret => IsTurret(MySession.Static?.ControlledEntity);
        internal static bool IsTurret(object entity) => entity is MyLargeTurretBase || entity is MyTurretControlBlock;
        public static MyCockpit HomeSeat => Active ? seat : null;
        internal static MatrixD PhysicalTrackingToWorld => (MatrixD)Player.PlayerToAbsolute.inverted*PhysicalAnchor;
        private static MatrixD PhysicalAnchor => seat!=null ? seat.GetHeadMatrix(true,true):CameraRig.Anchor;
        internal static bool Live(object entity) => entity!=null && (!(entity is VRage.Game.Entity.MyEntity native) || !native.Closed && !native.MarkedForClose);
        internal static bool ExitControlled(object controlled) => controlled is MyRemoteControl || IsTurret(controlled);
        internal static object Owner(object controlled,object camera) => !Live(controlled) ? null : ExitControlled(controlled) ? controlled : camera is MyCameraBlock && Live(camera) ? camera:null;
        internal static object Feed(object controlled,object camera) => camera is MyCameraBlock block ? Usable(block) ? (object)block:null : controlled is MyLargeTurretBase turret && Usable(turret) ? turret:null;
        internal static bool Usable(Sandbox.Game.Entities.Cube.MyFunctionalBlock block) => block!=null && !block.Closed && !block.MarkedForClose && block.IsWorking;
        internal static Sandbox.Game.Entities.IMyControllableEntity PreviousOwner(Sandbox.Game.Entities.IMyControllableEntity controlled) =>
            controlled is MyLargeTurretBase turret ? turret.PreviousControlledEntity :
            controlled is MyTurretControlBlock custom ? custom.PreviousControlledEntity :
            controlled is MyRemoteControl remote ? remote.PreviousControlledEntity : controlled;
        internal static Sandbox.Game.Entities.IMyControllableEntity Previous => PreviousOwner(MySession.Static?.ControlledEntity);
        internal static MyCockpit SeatFor(Sandbox.Game.Entities.IMyControllableEntity controlled,MyCharacter character) =>
            character?.Parent as MyCockpit ?? PreviousOwner(controlled) as MyCockpit;
        public static bool CharacterAnchor => Active && seat==null && MySession.Static?.LocalCharacter==Previous;
        // Native control can change during a switch action, before UpdateContext runs again.
        public static bool UsesSeat(MyCockpit candidate) => candidate!=null &&
            (Active && candidate==seat || (Turret || RemoteGrid || MySession.Static?.CameraController is MyCameraBlock) && Previous==candidate);
        public static void Reset()
        {
            CancelCameraSwitch();
            if(placementDirty) Save();
            owner=source=null; seat=null; Current=null; SeatedRig=null; OwnsInput=false;
            interaction.Reset(true); recenter=false; rayStart=rayEnd=null; lastHover=0; epoch++;
        }
        public static void Recenter() { recenter=true; }
        public static void ReleaseInput()
        {
            if(placementDirty) Save();
            interaction.Reset(); OwnsInput=false; lastHover=0;
            Controls.Static.BlockUntilRelease(); Components.VRMovementComponent.StopActive();
        }
        public static void Exit()
        {
            CancelCameraSwitch();
            ReleaseInput();
            var controlled=MySession.Static?.ControlledEntity;
            if(RemoteGrid && MySession.Static.CameraController is MyCameraBlock remoteCamera)
                remoteCamera.CubeGrid.GridSystems.CameraSystem.ResetCamera();
            if(ExitControlled(controlled)) controlled.Use();
            else if(MySession.Static?.CameraController is MyCameraBlock camera) camera.CubeGrid.GridSystems.CameraSystem.ResetCamera();
        }
        private static void CancelCameraSwitch()
        { pendingCamera=null; cameraSwitch.Cancel(); }
        internal static bool SwitchToCamera(MyCameraBlock camera)
        {
            var controlled=MySession.Static?.ControlledEntity;
            if(!IsTurret(controlled)) return false;
            if(!AvailableCamera(camera)) return false;
            var previous=PreviousOwner(controlled);
            if(!Live(previous)) return false;
            Exit();
            pendingCamera=camera;
            cameraSwitch.Begin(controlled,previous,DateTime.UtcNow,()=> {
                CancelCameraSwitch(); ReleaseInput(); camera.RequestSetView();
            });
            return true;
        }
        private static void UpdateCameraSwitch()
        {
            if(pendingCamera==null) return;
            var session=MySession.Static;
            bool available=Main.WorldAvailable && session?.LocalCharacter?.IsDead==false && !Main.MenuOpen &&
                Player.Headset.pose.isTracked && AvailableCamera(pendingCamera);
            cameraSwitch.Update(session?.ControlledEntity,available,DateTime.UtcNow);
            if(!cameraSwitch.Pending) pendingCamera=null;
        }
        public static void UpdateContext()
        {
            UpdateCameraSwitch();
            var session=MySession.Static;
            var character=session?.LocalCharacter;
            object next=Owner(session?.ControlledEntity,session?.CameraController);
            if(character==null || character.IsDead || !Main.WorldAvailable || next is VRage.Game.Entity.MyEntity entity && (entity.Closed || entity.MarkedForClose)) next=null;
            var home=next==null ? null:SeatFor(session.ControlledEntity,character);
            if(home!=null && (home.Closed || home.MarkedForClose || home.Pilot!=character)) next=null;
            if(next==null) { if(Active) { ReleaseInput(); Reset(); } return; }
            bool changed=!ReferenceEquals(owner,next);
            if(!Active || seat!=home)
            {
                ReleaseInput(); Reset(); owner=next; seat=home;
                key="Remote/"+(seat?.BlockDefinition.Id.SubtypeName ?? "Character");
                world=session.CurrentPath;
                if(seat==null) { CameraRig.Begin(character); CameraRig.End(character); }
                Place(); Restore();
            }
            else if(changed) { ReleaseInput(); owner=next; }
            if(changed && next is MyRemoteControl remote && !(session.CameraController is MyCameraBlock))
            {
                // Native AssignControl opens this camera only for character-origin control.
                long id=((Sandbox.ModAPI.Ingame.IMyTerminalBlock)remote).GetValue<long>("CameraList");
                MyEntities.TryGetEntityById(id,out var assigned);
                var camera=assigned as MyCameraBlock;
                if(!AvailableCamera(camera)) camera=remote.CubeGrid.GetFatBlocks<MyCameraBlock>().FirstOrDefault(AvailableCamera);
                camera?.RequestSetView();
            }
            var feed=Feed(session.ControlledEntity,session.CameraController);
            if(!ReferenceEquals(source,feed)) { ReleaseInput(); source=feed; }
            Refresh();
        }
        private static bool AvailableCamera(MyCameraBlock camera) => Usable(camera) && MyGridCameraSystem.CameraIsInRangeAndPlayerHasAccessLocal(camera);
        public static void Refresh()
        {
            if(!Active) return;
            if(seat!=null) SeatedRig=new CameraRig.Frame(seat.GetHeadMatrix(true,true),Player.PlayerToAbsolute.inverted,epoch);
            Publish();
        }
        private static void Place()
        {
            window.Place(Player.Headset.pose.deviceToAbsolute.matrix,1.2f,new Vector3(0,0,-1.5f));
            window.Aspect=9f/16; interaction.Reset();
            recenter=false;
        }
        public static void Update()
        {
            OwnsInput=false; rayStart=rayEnd=null;
            if(Current==null) { interaction.Reset(); return; }
            if(recenter) { Place(); Save(); }
            var now=DateTime.UtcNow; float seconds=(float)Math.Min(.05,Math.Max(0,(now-last).TotalSeconds)); last=now;
            var controls=Controls.Static;
            if(!Turret && !RemoteGrid && InputRouter.Gameplay && !Main.MenuOpen && CockpitControls.OwnsRightThumb)
                Zoom(-controls.ThrustRotate.Position.Y*seconds*.6f);
            if(InputRouter.Gameplay && !Main.MenuOpen) left=interaction.PickHand(Hand,Free,Aim)==Player.HandL;
            var hand=Hand;
            bool available=InputRouter.Gameplay && !Main.MenuOpen && !ThirdPersonView.Manipulating && Free(hand);
            if(!available) { interaction.Reset(); if(placementDirty) Save(); window.Stop(); lastHover=0; Publish(); return; }
            MatrixD aim=Aim(hand);
            var Reachable=WindowInteraction.Reachable(window,aim,PhysicalTrackingToWorld);
            int ExtraHit(Vector3 point,bool near)
            {
                int control=WindowFrame.Control(point,window.Width,window.Height,window.BarOffset,true,false,false,true);
                if(control!=0) return control;
                return near && Math.Abs(point.X)<window.Width/2 && Math.Abs(point.Y)<window.Height/2 ? 1:0;
            }
            interaction.Update(hand,(Matrix)aim,seconds,ExtraHit,Reachable);
            int hover=interaction.Hover;
            OwnsInput=interaction.Active || interaction.Released;
            if(interaction.Changed) placementDirty=true;
            if(interaction.Released) Save();
            if(interaction.Ray(aim,Reachable,out var start,out var end)) {rayStart=start; rayEnd=end;}
            if(interaction.Captured)
            {
                interaction.Grab(hand,PhysicalTrackingToWorld);
                if(interaction.HeldAction==WindowFrame.SignalsHover) CycleSignals();
                if(interaction.HeldAction==3 || interaction.HeldAction==4) Zoom(interaction.HeldAction==3 ? .12f:-.12f);
            }
            else if(interaction.Active && (hover==3 || hover==4) && interaction.HeldAction==hover) Zoom((hover==3 ? 1:-1)*seconds*.6f);
            // Turret and remote-grid feeds keep firing through the image; only their buttons and handles claim input.
            if(hover!=0 || interaction.Active || rayStart.HasValue && !Turret && !RemoteGrid) WindowInteraction.Claim(hand);
            lastHover=hover;
            Publish(window.Drag!=0 ? window.Drag:hover);
        }
        internal static int NextSignalMode(int mode) => (mode+1)%4;
        internal static float ZoomX(float width,bool plus) => WindowFrame.ZoomX(width,plus);
        private static int ReadSignalMode()
        {
            long id=(source as VRage.Game.Entity.MyEntity)?.EntityId ?? 0;
            return Math.Max(0,Math.Min(3,Common.Config?.MenuWindows?.FirstOrDefault(s=>s.Screen=="CameraSignals" && s.World==world && s.Cockpit==id)?.SignalMode ?? 0));
        }
        private static void CycleSignals()
        {
            long id=(source as VRage.Game.Entity.MyEntity)?.EntityId ?? 0;
            var saved=new MenuWindowSetting {Screen="CameraSignals",World=world,Cockpit=id,SignalMode=NextSignalMode(ReadSignalMode())};
            Common.Config.MenuWindows=Common.Config.MenuWindows.Where(s=>s.Screen!=saved.Screen || s.World!=world || s.Cockpit!=id).Concat(new[] {saved}).ToArray();
        }
        private static void Publish(int hover=-1)
        {
            if(!Active || source==null) { Current=null; return; }
            RemoteCombat.ReadAmmo(source as MyLargeTurretBase,out int ammo,out int capacity);
            Current=new View { Pose=(MatrixD)window.Pose,Width=window.Width,Height=window.Height,Hover=hover<0 ? window.Drag!=0 ? window.Drag:lastHover : hover,
                SignalMode=ReadSignalMode(),Ammo=ammo,Capacity=capacity,
                RayStart=rayStart,RayEnd=rayEnd,LeftHand=left,Source=(source as VRage.Game.Entity.MyEntity)?.EntityId ?? 0 };
        }
        internal static void Axes(Vector2 right,Vector2 left,float speed,out Vector2 aim,out float zoom)
        { aim=new Vector2(-VrMath.Deadzone(right.Y),VrMath.Deadzone(right.X))*speed; zoom=-VrMath.Deadzone(left.Y); }
        public static void ControlTurret(float speed)
        {
            var controls=Controls.Static;
            Axes(controls.ThrustRotate.Position,controls.ThrustLRFB.Position,speed,out var aim,out float zoom);
            CockpitControls.ApplyTurret(speed,ref aim,ref zoom);
            aim*=Common.Config.TurretAimSensitivity;
            if(OwnsInput || CockpitControls.Adjusting) { aim=Vector2.Zero; zoom=0; }
            MySession.Static.ControlledEntity.MoveAndRotate(Vector3.Zero,aim,0);
            Zoom(zoom/60*.6f);
        }
        public static void Zoom(float delta)
        {
            if(delta==0 || source==null) return;
            delta*=Common.Config.CameraZoomSensitivity;
            if(source is MyLargeTurretBase turret) turretZoom(turret,delta);
            else if(source is MyCameraBlock camera) camera.ChangeZoomPrecise(delta);
        }
        private static MatrixD TrackingToSeat => (MatrixD)Player.PlayerToAbsolute.inverted*(PhysicalAnchor*seat.PositionComp.WorldMatrixNormalizedInv);
        private static void Save()
        {
            placementDirty=false;
            if(seat==null) return;
            var local=(Matrix)((MatrixD)window.Pose*TrackingToSeat);
            var p=local.Translation; var q=Quaternion.CreateFromRotationMatrix(local);
            var saved=new MenuWindowSetting { Screen=key,World=world,Cockpit=seat.EntityId,Width=window.Width,X=p.X,Y=p.Y,Z=p.Z,QX=q.X,QY=q.Y,QZ=q.Z,QW=q.W };
            Common.Config.MenuWindows=Common.Config.MenuWindows.Where(s=>s.Screen!=key || s.World!=world || s.Cockpit!=seat.EntityId).Concat(new[] {saved}).ToArray();
        }
        private static void Restore()
        {
            if(seat==null) return;
            var saved=Common.Config.MenuWindows.FirstOrDefault(s=>s.Screen==key && s.World==world && s.Cockpit==seat.EntityId) ??
                Common.Config.MenuWindows.FirstOrDefault(s=>s.Screen==key && s.World==null && s.Cockpit==0);
            if(saved==null) return;
            var p=new Vector3(saved.X,saved.Y,saved.Z); var q=new Quaternion(saved.QX,saved.QY,saved.QZ,saved.QW);
            if(!p.IsValid() || p.Length()>10 || !q.LengthSquared().IsValid() || q.LengthSquared()<.9f || q.LengthSquared()>1.1f ||
                !saved.Width.IsValid() || saved.Width<MenuWindow.MinWidth || saved.Width>MenuWindow.MaxWidth) return;
            q.Normalize(); var local=MatrixD.CreateFromQuaternion(q); local.Translation=p;
            window.Pose=(Matrix)(local*MatrixD.Invert(TrackingToSeat)); window.Width=saved.Width;
        }
    }
}
