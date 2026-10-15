using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Read-only views into WeaponCore's client session for data its public API does not expose
    // (per-weapon ammo, reload and heat, the player's manual aim target). Every lookup is by name and
    // cached; a missing member disables only the feature that needs it.
    internal static class WeaponCoreInternals
    {
        private static readonly Dictionary<(Type,string),Func<object,object>> readers=new Dictionary<(Type,string),Func<object,object>>();
        private static Func<object> session;
        private static bool resolved,failed;

        internal static bool Available => WeaponCoreBridge.ApiReady && Resolve() && Session!=null;
        internal static object Session => session?.Invoke();
        internal static Type SessionType { get; private set; }

        private static bool Resolve()
        {
            if(resolved) return true;
            if(failed) return false;
            SessionType=LiveType("CoreSystems.Session","I");
            session=SessionType==null ? null : StaticReader(SessionType,"I");
            if(session==null) { failed=true; Logger.Info("WeaponCore session not found; VR weapon status and aim disabled"); return false; }
            resolved=true;
            return true;
        }

        internal static void Reset()
        {
            readers.Clear(); session=null; SessionType=null; fakeUpdate=null; fakeUpdateType=null; resolved=failed=false;
        }

        // Mods are recompiled into a new assembly on every world load and the old assemblies stay loaded, so a lookup by
        // name alone can return a previous world's dead copy. Returns the newest copy whose static instance is set.
        internal static Type LiveType(string fullName,string instance)
        {
            foreach(var assembly in Util.Util.NewestAssemblies())
            {
                var type=assembly.GetType(fullName,false);
                if(type!=null && StaticReader(type,instance)?.Invoke()!=null) return type;
            }
            return null;
        }

        // Getter for a static field or property; null when neither exists.
        internal static Func<object> StaticReader(Type type,string name)
        {
            var field=AccessTools.Field(type,name);
            if(field!=null && field.IsStatic) return ()=>field.GetValue(null);
            var getter=AccessTools.Property(type,name)?.GetGetMethod(true);
            return getter!=null && getter.IsStatic ? ()=>getter.Invoke(null,null) : (Func<object>)null;
        }

        // Reads a field or property by name on the runtime type of obj. Returns null when missing.
        internal static object Get(object obj,string name)
        {
            if(obj==null) return null;
            var type=obj.GetType();
            var key=(type,name);
            if(!readers.TryGetValue(key,out var reader))
            {
                var field=AccessTools.Field(type,name);
                if(field!=null) reader=o=>field.GetValue(o);
                else
                {
                    var property=AccessTools.Property(type,name);
                    var getter=property?.GetGetMethod(true);
                    reader=getter!=null ? (Func<object,object>)(o=>getter.Invoke(o,null)) : (o=>null);
                }
                readers[key]=reader;
            }
            try { return reader(obj); }
            catch(Exception) { return null; }
        }

        internal static object Path(object obj,params string[] names)
        {
            foreach(string name in names) { obj=Get(obj,name); if(obj==null) return null; }
            return obj;
        }

        internal static T Value<T>(object obj,T fallback,params string[] names) => Path(obj,names) is T value ? value : fallback;

        internal static object TrackingAi
        {
            get
            {
                var session=Session;
                if(session==null || !Value(session,false,"InGridAiBlock")) return null;
                var ai=Get(session,"TrackingAi");
                return ai==null || Value(ai,false,"MarkedForClose") ? null : ai;
            }
        }

        internal static uint Tick => Value(Session,0u,"Tick");
        internal static long PlayerId => Value(Session,0L,"PlayerId");
        internal static double MaxTargetingRange => Value(TrackingAi,0d,"MaxTargetingRange");

        internal static IEnumerable<object> WeaponComps()
        {
            if(!(Get(TrackingAi,"WeaponComps") is IList comps)) yield break;
            for(int i=0;i<comps.Count;i++) if(comps[i]!=null) yield return comps[i];
        }

        internal static IEnumerable<object> Weapons(object comp)
        {
            if(!(Get(comp,"Collection") is IList weapons)) yield break;
            for(int i=0;i<weapons.Count;i++) if(weapons[i]!=null) yield return weapons[i];
        }

        internal static MyEntity CompEntity(object comp) => Get(comp,"CoreEntity") as MyEntity;

        // The player's fake targets: "ManualTarget" (Manual-mode aim point) and "PaintedTarget" (painter mark).
        internal static object FakeTarget(string which)
        {
            var session=Session;
            if(!(Get(session,"PlayerDummyTargets") is IDictionary targets)) return null;
            long id=PlayerId;
            return targets.Contains(id) ? Get(targets[id],which) : null;
        }

        private static MethodInfo fakeUpdate;
        private static Type fakeUpdateType;
        // WeaponCore syncs a fake target to the server when it was updated on the current tick, so call this from its input step.
        internal static bool UpdateFake(string which,Vector3D position,MyEntity hit)
        {
            var target=FakeTarget(which);
            if(target==null) return false;
            if(fakeUpdateType!=target.GetType())
            {
                fakeUpdateType=target.GetType();
                fakeUpdate=AccessTools.Method(fakeUpdateType,"Update",new[] {typeof(Vector3D),typeof(uint),typeof(MyEntity),typeof(long)});
            }
            if(fakeUpdate==null) return false;
            fakeUpdate.Invoke(target,new object[] {position,Tick,hit,0L});
            return true;
        }

        internal static bool UpdateManualTarget(Vector3D position,MyEntity hit) => UpdateFake("ManualTarget",position,hit);
        internal static long PaintedEntity => Value(FakeTarget("PaintedTarget"),0L,"EntityId");
        // World position of the paint mark, which WeaponCore keeps relative to the painted entity.
        internal static Vector3D? PaintedPosition
        {
            get
            {
                var target=FakeTarget("PaintedTarget");
                if(!(Get(target,"TmpEntity") is MyEntity entity) || entity.Closed || entity.MarkedForClose || Value(target,0L,"EntityId")!=entity.EntityId) return null;
                return Get(target,"LocalPosition") is Vector3D local ? Vector3D.Transform(local,entity.PositionComp.WorldMatrixRef) : (Vector3D?)null;
            }
        }

        // A WeaponCore hand weapon is the active weapon AI (the player is not seated).
        internal static bool HandheldActive => Get(TrackingAi,"AiType")?.ToString()=="Player";
        internal static bool DroneAlert => Value(TrackingAi,false,"Construct","DroneAlert");
        // WeaponCore's hand-weapon reload key (tap: reload, hold one second: cycle ammo).
        internal static VRage.Input.MyKeys ReloadKey => Value(Session,VRage.Input.MyKeys.None,"UiInput","ReloadKey");
    }
}
