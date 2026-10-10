using System;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.SessionComponents.Clipboard;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class GridSelection
    {
        private static readonly Func<MyCubeBuilder,bool> building=AccessTools.MethodDelegate<Func<MyCubeBuilder,bool>>(AccessTools.Method(typeof(MyCubeBuilder),"IsBuilding"));
        private static readonly SelectionInput input=new SelectionInput();
        private static object owner,camera;
        private static Controller hand;
        private static string action;
        private static bool detached,magLocks;
        private static int epoch;
        private static TargetFeedback.View feedback;
        internal static bool Active => input.Active;
        internal static Controller Hand => Active ? hand:null;
        internal static bool Executing { get; private set; }
        internal static MyEntity Confirmed { get; private set; }
        internal static bool Owns(Controller candidate) => Active && candidate==hand;
        internal static TargetFeedback.View Feedback => feedback;
        internal static bool Arm(string name,bool single=false,bool magnetic=false,Controller pointer=null)
        {
            if(!InputRouter.Gameplay || MySession.Static?.LocalCharacter?.IsDead!=false) return false;
            if(PlacementControls.ClipboardActive || MyCubeBuilder.Static!=null && building(MyCubeBuilder.Static))
            { EssentialHud.Notify("Finish or cancel the current preview"); return false; }
            DampenerTargeting.Cancel();
            Clear();
            hand=pointer ?? GameActions.InvocationHand ?? Player.HandR;
            if(hand==null || !hand.pose.isTracked) return false;
            action=name; detached=single; magLocks=magnetic;
            owner=MySession.Static.ControlledEntity; camera=MySession.Static.CameraController;
            epoch=CameraRig.Current?.Epoch ?? 0;
            input.Arm(); CloseMenu();
            Controls.Static.Click(hand).Consume(true,true);
            hand.Vibrate(0,.025f,90,.18f);
            return true;
        }
        internal static void CloseMenu()
        {
            SpatialUi.Collapse();
            if(InputRouter.RadialOpen) ToolbarWheel.Close();
            if(MenuKeyboard.IsOpen) MenuKeyboard.Close();
        }
        internal static void CancelDampeners() { if(action=="Dampeners") Clear(); }
        internal static void Clear()
        {
            input.Clear(); feedback=null; owner=camera=null; action=null; hand=null;
        }
        internal static bool Eligible(string name,MyEntity entity) => entity!=null && !entity.Closed && !entity.MarkedForClose &&
            (name=="Dampeners" ? entity is MyCubeGrid && entity.Physics!=null :
             name=="Spectator lock" ? (entity is MyCubeGrid || entity is Sandbox.Game.Entities.Character.MyCharacter) && entity.Physics!=null :
             name=="CreateBlueprint" ? entity is MyCubeGrid :
             entity is MyCubeGrid || entity is MyFloatingObject || (name=="Cut" || name=="Delete") && entity is MyVoxelMap);
        internal static MyEntity Cast(LineD ray,out Vector3D hit,bool includeControlled=false)
        {
            hit=ray.To;
            var controlled=includeControlled ? null:MySession.Static?.ControlledEntity?.Entity;
            var result=MyEntities.GetIntersectionWithLine(ref ray,controlled,controlled?.GetTopMostParent(),ignoreChildren:false,ignoreFloatingObjects:false);
            if(!result.HasValue || result.Value.Entity==null) return null;
            hit=result.Value.IntersectionPointInWorldSpace;
            return result.Value.Entity.GetTopMostParent() as MyEntity;
        }
        internal static bool TryPointer(Controller pointer,out MatrixD pose)
        {
            pose=MatrixD.Identity;
            if(pointer==null || !pointer.pose.isTracked || !HandInteraction.TryWorldPose(pointer,out pose)) return false;
            if(ThirdPersonView.Active || SpectatorView.Active)
            {
                if(MenuHands.TryPointPose(pointer.GripTracking,out var tracking,pointer)) pose=tracking*CameraRig.Current.TrackingToWorld;
            }
            else if(TrackedArms.TryFreePointPose(pointer,out var finger)) pose=finger;
            pose=VrMath.Rigid(pose);
            return pose.IsValid();
        }
        private static bool UiOwns(Controller pointer) => Main.MenuOpen || FloatingWindows.OwnsInput || HelmetHud.Consumes(pointer) ||
            CockpitTouch.Owns(pointer) || HandInteraction.Owns(pointer) || TouchScreenBridge.PointingFor(pointer) || ArthurLcdBridge.PointingFor(pointer) ||
            (pointer==Player.HandR && (SpatialUi.OwnsRight || SpatialUi.Pointing || SpatialUi.RayTargeted || CockpitControls.Held(pointer)));
        internal static void Update()
        {
            if(!Active) return;
            var c=Controls.Static;
            bool cancel=c.Unequip.HasPressed;
            bool valid=InputRouter.Gameplay && !Main.MenuOpen && !ThirdPersonView.Manipulating &&
                ReferenceEquals(owner,MySession.Static?.ControlledEntity) && ReferenceEquals(camera,MySession.Static?.CameraController) &&
                epoch==(CameraRig.Current?.Epoch ?? 0) && MySession.Static?.LocalCharacter?.IsDead==false && hand.pose.isTracked;
            var pointer=hand;
            bool ui=UiOwns(pointer) || SpectatorView.Manipulating,down=c.Click(pointer).Down;
            bool pressed=input.Update(valid,down,ui,cancel);
            if(cancel) c.Unequip.BlockUntilRelease();
            if(!input.Active) { Clear(); return; }
            // Sample raw trigger before consuming its gameplay aliases.
            c.Click(pointer).Consume(true,down);
            if(pointer==Player.HandL) { c.ThrustUp.BlockUntilRelease(); c.ThrustForward.BlockUntilRelease(); }
            if(!TryPointer(pointer,out var pose)) { feedback=null; return; }
            double range=action=="Dampeners" || action=="Spectator lock" ? 1000:10000;
            if(action=="Spectator lock") range*=Math.Max(1,CameraRig.Current?.UnitsPerMeter ?? 1);
            var ray=new LineD(pose.Translation,pose.Translation+pose.Forward*range);
            var target=Cast(ray,out var point,action=="Spectator lock");
            bool eligible=!ui && Eligible(action,target) && (action!="Dampeners" || DampenerTargeting.InRange(target));
            feedback=new TargetFeedback.View(pose,point,eligible ? target:null,CameraRig.Current?.UnitsPerMeter ?? 1);
            if(!pressed || !eligible) return;
            var name=action; bool single=detached,magnetic=magLocks;
            Clear();
            if(name=="Dampeners") { DampenerTargeting.Select(target,ray,pointer); return; }
            if(name=="Spectator lock") { SpectatorView.Lock(target); return; }
            try
            {
                Executing=true; Confirmed=target;
                var method=AccessTools.Method(typeof(MyClipboardComponent),name,new[] {typeof(bool),typeof(bool)});
                bool result=(bool)method.Invoke(MyClipboardComponent.Static,new object[] {single,magnetic});
                if(!result) EssentialHud.Notify("Action unavailable for this target");
            }
            finally { Executing=false; Confirmed=null; c.Click(pointer).Consume(true,down); InputRouter.Update(); }
        }
    }
}
