using System;
using System.Collections;
using System.Collections.Generic;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // WeaponCore's client session, for what its public API does not expose: per-weapon state, the player's fake targets
    // (manual aim, paint) and client focus requests. Reads by name through ModReflection.
    internal static class WeaponCoreInternals
    {
        // Used when WeaponCore reports no targeting range for the tracked AI.
        private const double FallbackRange=5000;
        private static Func<object> session;
        private static bool resolved,failed;

        internal static bool Available => WeaponCoreBridge.ApiReady && Resolve() && Session!=null;
        internal static object Session => session?.Invoke();

        private static bool Resolve()
        {
            if(resolved) return true;
            if(failed) return false;
            var type=ModReflection.LiveType("CoreSystems.Session","I");
            session=type==null ? null : ModReflection.StaticReader(type,"I");
            if(session==null) { failed=true; Logger.Info("WeaponCore session not found; VR weapon status and aim disabled"); return false; }
            resolved=true;
            return true;
        }

        internal static void Reset() { session=null; resolved=failed=false; }

        internal static object TrackingAi
        {
            get
            {
                var session=Session;
                if(session==null || !ModReflection.Value(session,false,"InGridAiBlock")) return null;
                var ai=ModReflection.Get(session,"TrackingAi");
                return ai==null || ModReflection.Value(ai,false,"MarkedForClose") ? null : ai;
            }
        }

        internal static uint Tick => ModReflection.Value(Session,0u,"Tick");
        internal static long PlayerId => ModReflection.Value(Session,0L,"PlayerId");
        internal static double TargetingRange { get { double range=ModReflection.Value(TrackingAi,0d,"MaxTargetingRange"); return range>0 ? range:FallbackRange; } }

        // Client focus requests, the path WeaponCore's own target selection uses; its public focus API only works on the server.
        internal static bool RequestFocus(MyEntity target) => FocusCall("RequestAddFocus",target,true);
        internal static bool RequestRelease() => FocusCall("RequestReleaseActive",null,false);
        internal static bool RequestFocusLock() => FocusCall("RequestAddLock",null,false);
        private static bool FocusCall(string method,MyEntity target,bool withTarget)
        {
            var ai=TrackingAi;
            var focus=ModReflection.Path(ai,"Construct","Focus");
            if(focus==null) return false;
            var types=withTarget ? new[] {typeof(MyEntity),ai.GetType(),typeof(long)}:new[] {ai.GetType(),typeof(long)};
            var arguments=withTarget ? new object[] {target,ai,PlayerId}:new object[] {ai,PlayerId};
            return ModReflection.Call(focus,method,types,arguments);
        }

        internal static IEnumerable<object> WeaponComps()
        {
            if(!(ModReflection.Get(TrackingAi,"WeaponComps") is IList comps)) yield break;
            for(int i=0;i<comps.Count;i++) if(comps[i]!=null) yield return comps[i];
        }

        internal static IEnumerable<object> Weapons(object comp)
        {
            if(!(ModReflection.Get(comp,"Collection") is IList weapons)) yield break;
            for(int i=0;i<weapons.Count;i++) if(weapons[i]!=null) yield return weapons[i];
        }

        internal static MyEntity CompEntity(object comp) => ModReflection.Get(comp,"CoreEntity") as MyEntity;

        // The player's fake targets: "ManualTarget" (Manual-mode aim point) and "PaintedTarget" (painter mark).
        private static object FakeTarget(string which)
        {
            if(!(ModReflection.Get(Session,"PlayerDummyTargets") is IDictionary targets)) return null;
            long id=PlayerId;
            return targets.Contains(id) ? ModReflection.Get(targets[id],which) : null;
        }

        // WeaponCore syncs a fake target to the server when it was updated on the current tick, so call this from its input step.
        internal static bool UpdateFake(string which,Vector3D position,MyEntity hit) =>
            ModReflection.Call(FakeTarget(which),"Update",new[] {typeof(Vector3D),typeof(uint),typeof(MyEntity),typeof(long)},position,Tick,hit,0L);

        internal static long PaintedEntity => ModReflection.Value(FakeTarget("PaintedTarget"),0L,"EntityId");
        // The painted entity and the mark's offset in its frame, as WeaponCore keeps it; false when nothing is painted.
        internal static bool PaintMark(out MyEntity entity,out Vector3D local)
        {
            var target=FakeTarget("PaintedTarget");
            entity=ModReflection.Get(target,"TmpEntity") as MyEntity;
            local=ModReflection.Get(target,"LocalPosition") is Vector3D offset ? offset:Vector3D.Zero;
            if(entity==null || entity.Closed || entity.MarkedForClose || ModReflection.Value(target,0L,"EntityId")!=entity.EntityId) { entity=null; return false; }
            return true;
        }

        internal static object TargetUi => ModReflection.Get(Session,"TargetUi");
        // A WeaponCore hand weapon is the active weapon AI (the player is not seated).
        internal static bool HandheldActive => ModReflection.Get(TrackingAi,"AiType")?.ToString()=="Player";
        // A guided hand weapon that takes a selected target, as WeaponCore's middle-click selection does on desktop.
        internal static bool SmartHandheld => ModReflection.Value(TrackingAi,false,"SmartHandheld");
        internal static bool DroneAlert => ModReflection.Value(TrackingAi,false,"Construct","DroneAlert");
        // Servers can disable HUD painting; WeaponCore then ignores the mark.
        internal static bool PaintProhibited => ModReflection.Value(Session,false,"Settings","Enforcement","ProhibitHUDPainter");
        // WeaponCore's hand-weapon reload key (tap: reload, hold one second: cycle ammo).
        internal static VRage.Input.MyKeys ReloadKey => ModReflection.Value(Session,VRage.Input.MyKeys.None,"UiInput","ReloadKey");
        // Focus lock mode: when locked, the focus is kept even out of view. Null when unavailable.
        internal static bool? FocusLocked
        {
            get
            {
                var mode=ModReflection.Path(TrackingAi,"Construct","Data","Repo","FocusData","Locked");
                return mode==null ? (bool?)null : mode.ToString()!="None";
            }
        }
    }
}
