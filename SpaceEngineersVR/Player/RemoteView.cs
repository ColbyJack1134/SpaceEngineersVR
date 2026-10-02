using System;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
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
            public MatrixD Pose;
            public float Width,Height;
            public int Hover;
            public long Source;
            public Vector3D? RayStart,RayEnd;
        }
        private static readonly MenuWindow window=new MenuWindow { BarOffset=.035f };
        private static readonly InteractionPress press=new InteractionPress();
        private static readonly Action<MyLargeTurretBase,float> turretZoom=AccessTools.MethodDelegate<Action<MyLargeTurretBase,float>>(AccessTools.Method(typeof(MyLargeTurretBase),"ChangeZoomPrecise"));
        private static object source;
        private static MyCockpit seat;
        private static MatrixD stationary;
        private static DateTime last;
        private static int epoch=100000;
        private static string key;
        private static string world;
        private static bool placementDirty;
        private static bool recenter,directHeld;
        private static int zoomHeld;
        private static Matrix heldWrist;
        private static Vector3 heldPoint;
        private static DateTime grabbed;
        private static Vector3D? rayStart,rayEnd;
        private static int lastHover;
        public static bool Pointing => Current?.Hover>0 || directHeld;
        internal static bool TryAttachment(out MatrixD wrist,out Vector3D point,out float blend)
        {
            MatrixD parent=(MatrixD)window.Pose*Parent;
            wrist=(MatrixD)heldWrist*parent; point=Vector3D.Transform(heldPoint,parent);
            blend=MathHelper.Clamp((float)(DateTime.UtcNow-grabbed).TotalSeconds/.09f,0,1);
            return Active && directHeld;
        }
        public static View Current { get; private set; }
        public static CameraRig.Frame SeatedRig { get; private set; }
        public static bool Active => source!=null;
        public static bool OwnsInput { get; private set; }
        public static bool Turret => IsTurret(MySession.Static?.ControlledEntity);
        internal static bool IsTurret(object entity) => entity is MyLargeTurretBase || entity is MyTurretControlBlock;
        public static MyCockpit HomeSeat => Active ? seat : null;
        private static MatrixD Parent => seat?.WorldMatrix ?? stationary;
        internal static Sandbox.Game.Entities.IMyControllableEntity Previous =>
            MySession.Static?.ControlledEntity is MyLargeTurretBase turret ? turret.PreviousControlledEntity :
            MySession.Static?.ControlledEntity is MyTurretControlBlock custom ? custom.PreviousControlledEntity : MySession.Static?.ControlledEntity;
        public static bool CharacterAnchor => Active && seat==null && MySession.Static?.LocalCharacter==Previous;
        // Native control can change during a switch action, before UpdateContext runs again.
        public static bool UsesSeat(MyCockpit candidate) => candidate!=null &&
            (Active && candidate==seat || (Turret || MySession.Static?.CameraController is MyCameraBlock) && Previous==candidate);
        public static void Reset()
        {
            if(placementDirty) Save();
            source=null; seat=null; Current=null; SeatedRig=null; OwnsInput=false;
            window.Cancel(); press.Block(); recenter=directHeld=false; rayStart=rayEnd=null; lastHover=zoomHeld=0; epoch++;
        }
        public static void Recenter() { recenter=true; }
        public static void Exit()
        {
            if(Turret) MySession.Static.ControlledEntity.Use();
            else if(MySession.Static?.CameraController is MyCameraBlock camera) camera.CubeGrid.GridSystems.CameraSystem.ResetCamera();
            Controls.Static.BlockUntilRelease();
        }
        public static void UpdateContext()
        {
            object next=MySession.Static?.CameraController is MyCameraBlock camera ? (object)camera :
                MySession.Static?.ControlledEntity is MyLargeTurretBase turret ? turret : null;
            var character=MySession.Static?.LocalCharacter;
            if(character==null || character.IsDead || !Main.WorldAvailable) next=null;
            if(!ReferenceEquals(source,next))
            {
                Reset(); source=next;
                if(next==null) return;
                seat=character.Parent as MyCockpit ?? Previous as MyCockpit;
                stationary=MatrixD.CreateTranslation(character.WorldMatrix.Translation);
                key="Remote/"+(seat?.BlockDefinition.Id.SubtypeName ?? "Character");
                world=MySession.Static.CurrentPath;
                if(seat==null) { CameraRig.Begin(character); CameraRig.End(character); }
                Place(); Restore();
            }
            if(!Active) return;
            if(seat!=null && (seat.Closed || seat.Pilot!=character)) { Reset(); return; }
            Refresh();
        }
        public static void Refresh()
        {
            if(!Active) return;
            if(seat!=null) SeatedRig=new CameraRig.Frame(seat.GetHeadMatrix(true,true),Player.PlayerToAbsolute.inverted,epoch);
            Publish();
        }
        private static void Place()
        {
            MatrixD anchor=seat!=null ? seat.GetHeadMatrix(true,true) : CameraRig.Anchor;
            MatrixD head=(MatrixD)(Player.Headset.pose.deviceToAbsolute.matrix*Player.PlayerToAbsolute.inverted)*anchor;
            window.Place((Matrix)(head*MatrixD.Invert(Parent)));
            window.Width=1.2f;
            window.Aspect=9f/16;
            window.Pose.Translation+=(window.Pose.Backward*.7f);
            recenter=false;
        }
        public static void Update()
        {
            OwnsInput=false; rayStart=rayEnd=null;
            if(!Active) { press.Block(); return; }
            if(recenter) { Place(); Save(); }
            var now=DateTime.UtcNow; float seconds=(float)Math.Min(.05,Math.Max(0,(now-last).TotalSeconds)); last=now;
            var controls=Controls.Static;
            if(!Turret && InputRouter.Gameplay && !Main.MenuOpen && !CockpitControls.Adjusting && CockpitControls.Held(Player.HandL))
            {
                Zoom(-VrMath.Deadzone(controls.ThrustLRFB.RawPosition.Y)*seconds*.6f);
                controls.ThrustLRFB.BlockUntilRelease();
            }
            bool available=InputRouter.Gameplay && !Main.MenuOpen && MenuPointer.GameFocused && Player.HandR.pose.isTracked &&
                !SpatialUi.OwnsRight && !CockpitTouch.OwnsRight && !CockpitControls.Held(Player.HandR);
            if(!available) { press.Block(); if(placementDirty) Save(); window.Cancel(); directHeld=false; lastHover=zoomHeld=0; Publish(); return; }
            MatrixD aim=SpatialUi.DeviceWorld(Player.HandR.AimTracking);
            if(TrackedArms.TryFreePointPose(Player.HandR,out var finger)) aim=finger;
            Matrix local=(Matrix)(aim*MatrixD.Invert(Parent));
            int hover=0;
            var onPanel=local*Matrix.Invert(window.Pose);
            bool near=onPanel.Translation.Z>=-.025f && onPanel.Translation.Z<=.05f;
            var input=press.Read(Player.HandR,window.Drag!=0 || zoomHeld!=0 ? directHeld:near);
            press.Update(true,input);
            Vector3 point=onPanel.Translation; point.Z=0;
            bool hitPanel=near || window.Pointer(local,out point);
            if(!input.Down && window.Drag==0) { directHeld=false; zoomHeld=0; }
            if(window.Drag!=0)
            {
                OwnsInput=true;
                if(!input.Down) { window.Stop(); directHeld=false; Save(); }
                else if(window.Pointer(local,out point,true) || window.Drag==1)
                { window.Move(local,point,controls.ThrustRotate.RawPosition,seconds); placementDirty=true; }
                input.Consume(); controls.ThrustRotate.BlockUntilRelease(); controls.WalkRotate.BlockUntilRelease();
            }
            else if(hitPanel)
            {
                hover=window.Handle(point);
                if(hover==0 && Math.Abs(point.Y+window.Height/2+window.BarOffset)<.035f)
                {
                    if(Math.Abs(point.X-ZoomX(window.Width,false))<.027f) hover=3;
                    else if(Math.Abs(point.X-ZoomX(window.Width,true))<.027f) hover=4;
                }
                var hit=Vector3D.Transform(point,(MatrixD)window.Pose*Parent);
                if(HandInteraction.ObstacleDistance(aim,(float)Vector3D.Distance(aim.Translation,hit)+.01f)<Vector3D.Distance(aim.Translation,hit)-.02) hover=0;
                if(!near && Math.Abs(point.X)<window.Width/2+.065f && point.Y<window.Height/2+.03f && point.Y> -window.Height/2-.10f &&
                    HandInteraction.ObstacleDistance(aim,(float)Vector3D.Distance(aim.Translation,hit)+.01f)>=Vector3D.Distance(aim.Translation,hit)-.02)
                { rayStart=Vector3D.Transform(aim.Translation,MatrixD.Invert(Parent)); rayEnd=Vector3D.Transform(point,(MatrixD)window.Pose); }
                if(near && hover==0 && Math.Abs(point.X)<window.Width/2 && Math.Abs(point.Y)<window.Height/2) hover=1;
                if(hover!=0)
                {
                    if(hover!=lastHover) CockpitFeedback.Hover(Player.HandR);
                }
                if(press.Pressed && hover!=0)
                {
                    input.Consume(); OwnsInput=true;
                    if(near)
                    {
                        directHeld=true; grabbed=now; heldPoint=point;
                        heldWrist=(Matrix)(TrackedArms.FreeWristWorld(Player.HandR)*MatrixD.Invert((MatrixD)window.Pose*Parent));
                    }
                    if(hover<=2) { window.Begin(hover,local,point); }
                    else { zoomHeld=hover; Zoom(hover==3 ? .12f : -.12f); }
                    CockpitFeedback.Click(Player.HandR);
                }
                else if(press.Held && hover>=3 && zoomHeld==hover) { OwnsInput=true; input.Consume(); Zoom((hover==3 ? 1:-1)*seconds*.6f); }
            }
            lastHover=hover;
            Publish(window.Drag!=0 ? window.Drag:hover);
        }
        internal static float ZoomX(float width,bool plus) => -width/2+(plus ? .115f:.05f);
        private static void Publish(int hover=-1)
        {
            if(!Active) return;
            Current=new View { Pose=(MatrixD)window.Pose*Parent,Width=window.Width,Height=window.Height,Hover=hover<0 ? window.Drag!=0 ? window.Drag:lastHover : hover,
                RayStart=rayStart.HasValue ? Vector3D.Transform(rayStart.Value,Parent):(Vector3D?)null,RayEnd=rayEnd.HasValue ? Vector3D.Transform(rayEnd.Value,Parent):(Vector3D?)null,Source=(source as VRage.Game.Entity.MyEntity)?.EntityId ?? 0 };
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
        public static void Stop(Sandbox.Game.Entities.IMyControllableEntity owner)
        { if(IsTurret(owner)) owner.MoveAndRotate(Vector3.Zero,Vector2.Zero,0); }
        public static void Zoom(float delta)
        {
            if(delta==0 || !Active) return;
            delta*=Common.Config.CameraZoomSensitivity;
            if(source is MyLargeTurretBase turret) turretZoom(turret,delta);
            else if(source is MyCameraBlock camera) camera.ChangeZoomPrecise(delta);
        }
        private static void Save()
        {
            placementDirty=false;
            if(seat==null) return;
            var p=window.Pose.Translation; var q=Quaternion.CreateFromRotationMatrix(window.Pose);
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
            q.Normalize(); window.Pose=Matrix.CreateFromQuaternion(q); window.Pose.Translation=p; window.Width=saved.Width;
        }
    }
}
