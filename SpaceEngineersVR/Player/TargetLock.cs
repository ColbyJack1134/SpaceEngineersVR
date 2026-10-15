using System;
using System.Collections.Generic;
using System.Linq;
using VRage;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // WeaponCore target locking from VR:
    //  - Tap the right grip while pointing (at the world, or at the camera feed image): lock / unlock.
    //    This reuses the plugin's existing cockpit grip tap (ship secondary / vanilla lock); WC grids are routed here instead.
    //  - "Lock target ahead" action: lock along the feed centre (where WC draws its camera reticle) or the cockpit nose.
    internal static class TargetLock
    {
        private const double Range=20000;
        private const double ConeDegrees=2.0;
        private static readonly double coneCos=Math.Cos(MathHelper.ToRadians(ConeDegrees));

        // Pointing at a camera feed claims the hand's grip, so the cockpit grip tap never completes; this tap covers that case.
        private static readonly GripTap feedTap=new GripTap();
        private static bool feedGripWas;

        internal static readonly ActionChoice LockAheadAction=new ActionChoice("Lock target ahead",LockAhead,
            enabled:()=>ShooterGrid()!=null,searchTerms:"weaponcore wc target lock focus ahead reticle");
        internal static readonly ActionChoice NextTargetAction=new ActionChoice("Next target",()=>Cycle(1),
            enabled:()=>ShooterGrid()!=null,searchTerms:"weaponcore wc target cycle next focus");
        internal static readonly ActionChoice PreviousTargetAction=new ActionChoice("Previous target",()=>Cycle(-1),
            enabled:()=>ShooterGrid()!=null,searchTerms:"weaponcore wc target cycle previous focus");
        internal static readonly ActionChoice ReleaseTargetAction=new ActionChoice("Release target",Release,
            enabled:()=>ShooterGrid()!=null,searchTerms:"weaponcore wc target release unlock focus clear");
        internal static readonly ActionChoice FocusLockAction=new ActionChoice("Toggle focus lock",ToggleFocusLock,
            enabled:()=>ShooterGrid()!=null,searchTerms:"weaponcore wc focus lock hold keep target");
        internal static readonly ActionChoice PaintAction=new ActionChoice("Paint target",WeaponCoreAim.RequestPaint,
            enabled:()=>ShooterGrid()!=null,searchTerms:"weaponcore wc painter paint mark guided");
        internal static readonly ActionChoice ClearPaintAction=new ActionChoice("Clear paint",WeaponCoreAim.RequestClearPaint,
            enabled:()=>ShooterGrid()!=null,searchTerms:"weaponcore wc painter paint clear");
        internal static readonly ActionChoice[] Actions={LockAheadAction,NextTargetAction,PreviousTargetAction,ReleaseTargetAction,FocusLockAction,PaintAction,ClearPaintAction};

        internal static void Reset()
        {
            feedTap.Update(false,false,true,DateTime.UtcNow);
            feedGripWas=false;
        }

        internal static void Update()
        {
            if(Main.WorldAvailable) WeaponCoreBridge.Load();
            if(!WeaponCoreBridge.ApiReady) { feedGripWas=false; return; }
            TargetLockToolbar.Register();
            WeaponCoreAlerts.Update(ShooterGrid());
            WeaponCoreAim.UpdatePaintArm();
            var hand=Player.HandR;
            bool grip=hand.pose.isTracked && Controls.Static.GripClick(hand).RawPressed;
            bool released=feedGripWas && !grip;
            feedGripWas=grip;
            // Mirrors RemoteView's claim rule: camera feeds claim the pointing hand, turret and remote-grid feeds do not.
            bool claimedFeed=RemoteView.Current!=null && RemoteView.ImageUV.HasValue && RemoteView.PointerHand==hand &&
                !RemoteView.Turret && !RemoteView.RemoteGrid && InputRouter.Gameplay && !Main.MenuOpen && ShooterGrid()!=null;
            if(feedTap.Update(claimedFeed && grip,released,!claimedFeed,DateTime.UtcNow)) PointAndToggle(hand);
        }

        // Called from the cockpit's existing right-grip tap. Returns true when WeaponCore handled it.
        internal static bool OnGripTap()
        {
            if(ShooterGrid()==null) return false;
            PointAndToggle(Player.HandR);
            return true;
        }

        private static void PointAndToggle(Controller hand)
        {
            var grid=ShooterGrid();
            if(grid==null) return;
            MyEntity picked=null;
            if(TryHandRay(hand,out var origin,out var direction)) picked=Pick(grid,origin,direction);
            Toggle(grid,picked,true);
            hand.Vibrate(0,.025f,90,picked!=null ? .25f:.12f);
        }

        // Feed image first (when this hand is pointing at it), otherwise the free world pointer.
        private static bool TryHandRay(Controller hand,out Vector3D origin,out Vector3D direction)
        {
            origin=direction=Vector3D.Zero;
            if(!hand.pose.isTracked) return false;
            var remote=RemoteView.Current;
            if(remote!=null && RemoteView.ImageUV.HasValue && RemoteView.PointerHand==hand &&
                FeedCamera.TryRay(remote.Source,RemoteView.ImageUV.Value,out origin,out direction)) return true;
            if(!GridSelection.TryPointer(hand,out var pose)) return false;
            origin=pose.Translation; direction=pose.Forward;
            return direction.IsValid() && direction.LengthSquared()>.5;
        }

        // Steps the focus through WeaponCore's tracked threats in the order the markers rank them, wrapping at either end.
        internal static void Cycle(int step)
        {
            var grid=ShooterGrid();
            if(grid==null) return;
            var threats=RankedThreats(grid).Select(t=>t.Item1).ToList();
            if(threats.Count==0) { EssentialHud.Notify("No targets tracked"); return; }
            var current=WeaponCoreBridge.Focus(grid);
            int index=current==null ? -1 : threats.FindIndex(t=>t.GetTopMostParent()==current.GetTopMostParent());
            int next=index<0 ? (step>0 ? 0:threats.Count-1) : ((index+step)%threats.Count+threats.Count)%threats.Count;
            EssentialHud.Notify(WeaponCoreBridge.SetFocus(grid,threats[next]) ? "Target locked: "+Name(threats[next])+"  ("+(next+1)+"/"+threats.Count+")" : "Lock refused");
        }

        internal static void Release()
        {
            var grid=ShooterGrid();
            if(grid!=null && WeaponCoreBridge.Focus(grid)!=null && WeaponCoreBridge.ReleaseFocus(grid)) EssentialHud.Notify("Target released");
        }

        internal static void ToggleFocusLock()
        {
            if(ShooterGrid()==null) return;
            bool? locked=WeaponCoreBridge.FocusLocked();
            if(WeaponCoreBridge.ToggleFocusLock()) EssentialHud.Notify(locked==true ? "Focus lock off" : "Focus lock on");
        }

        internal static void LockAhead()
        {
            var grid=ShooterGrid();
            if(grid==null) { EssentialHud.Notify("No WeaponCore grid controlled"); return; }
            if(!TryAheadRay(out var origin,out var direction)) { EssentialHud.Notify("No aim direction"); return; }
            var picked=Pick(grid,origin,direction);
            if(picked==null) { EssentialHud.Notify("No target ahead"); return; }
            Toggle(grid,picked,false);
        }

        // Matches WeaponCore: camera feeds aim along the camera centre; a seated first-person view aims along the cockpit.
        internal static bool TryAheadRay(out Vector3D origin,out Vector3D direction)
        {
            origin=direction=Vector3D.Zero;
            var remote=RemoteView.Current;
            if(remote!=null && FeedCamera.TryCentre(remote.Source,out origin,out direction)) return true;
            var controlled=MySession.Static?.ControlledEntity?.Entity as MyEntity;
            if(controlled==null) return false;
            origin=controlled.PositionComp.WorldAABB.Center; direction=controlled.PositionComp.WorldMatrixRef.Forward;
            return true;
        }

        // WeaponCore's targeting range for the controlled grid; Range when unknown.
        private static double TargetRange { get { double range=WeaponCoreInternals.MaxTargetingRange; return range>0 ? range:Range; } }

        // Tracked threats other than the own construct, nearest first; equal distances put the higher threat first.
        internal static List<MyTuple<MyEntity,float>> RankedThreats(MyEntity grid)
        {
            var own=grid.GetTopMostParent();
            var centre=grid.PositionComp.WorldAABB.Center;
            return WeaponCoreBridge.Threats(grid)
                .Where(t=>t.Item1!=null && !t.Item1.Closed && !t.Item1.MarkedForClose && t.Item1.GetTopMostParent()!=own)
                .OrderBy(t=>Vector3D.DistanceSquared(t.Item1.PositionComp.WorldAABB.Center,centre)).ThenByDescending(t=>t.Item2).ToList();
        }

        internal static MyEntity ShooterGrid()
        {
            if(!WeaponCoreBridge.ApiReady) return null;
            var controlled=MySession.Static?.ControlledEntity;
            MyEntity grid=controlled is MyShipController ship ? ship.CubeGrid :
                controlled is MyLargeTurretBase turret ? turret.CubeGrid :
                controlled is SpaceEngineers.Game.Entities.Blocks.MyTurretControlBlock control ? control.CubeGrid : null;
            return WeaponCoreBridge.HasAi(grid) ? grid : null;
        }

        // Only targets WeaponCore's own AI already tracks are eligible, so VR can never focus something WC's UI could not
        // (the server does not re-check range or detection on focus requests). An exact ray hit on one of them wins;
        // otherwise the tracked threat nearest the ray inside a small cone.
        private static MyEntity Pick(MyEntity grid,Vector3D origin,Vector3D direction)
        {
            var own=grid.GetTopMostParent();
            var threats=WeaponCoreBridge.Threats(grid);
            double range=TargetRange;
            var hitEntity=GridSelection.Cast(new LineD(origin,origin+direction*range),out _)?.GetTopMostParent() as MyEntity;
            MyEntity best=null; double bestCos=coneCos;
            foreach(var threat in threats)
            {
                var target=threat.Item1;
                if(target==null || target.Closed || target.MarkedForClose || target.GetTopMostParent()==own) continue;
                if(hitEntity!=null && target.GetTopMostParent()==hitEntity) return target;
                var toTarget=target.PositionComp.WorldAABB.Center-origin;
                double distance=toTarget.Length();
                if(distance<1 || distance>range) continue;
                double cos=Vector3D.Dot(toTarget/distance,direction);
                if(cos>bestCos) { bestCos=cos; best=target; }
            }
            return best;
        }

        private static void Toggle(MyEntity grid,MyEntity picked,bool releaseOnEmpty)
        {
            var current=WeaponCoreBridge.Focus(grid);
            if(picked==null)
            {
                if(releaseOnEmpty && current!=null && WeaponCoreBridge.ReleaseFocus(grid)) EssentialHud.Notify("Target released");
                else if(current==null) EssentialHud.Notify("No target under pointer");
                return;
            }
            if(current!=null && current.GetTopMostParent()==picked.GetTopMostParent())
            {
                if(WeaponCoreBridge.ReleaseFocus(grid)) EssentialHud.Notify("Target released");
                return;
            }
            EssentialHud.Notify(WeaponCoreBridge.SetFocus(grid,picked) ? "Target locked: "+Name(picked) : "Lock refused");
        }

        private static string Name(MyEntity entity) => string.IsNullOrWhiteSpace(entity.DisplayName) ? entity.GetType().Name : entity.DisplayName;
    }
}
