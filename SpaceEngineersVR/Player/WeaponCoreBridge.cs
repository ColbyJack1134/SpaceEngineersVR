using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRage;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Player
{
    // WeaponCore's public mod API. Focus changes try WeaponCore's client request path first (WeaponCoreInternals), since
    // the public focus API only works on the server.
    internal static class WeaponCoreBridge
    {
        private static readonly ModApiClient api=new ModApiClient("WeaponCore",67549756549,Attach,Detach);
        private static Func<MyEntity,int,MyEntity> getAiFocus;
        private static Func<MyEntity,MyEntity,int,bool> setAiFocus;
        private static Func<MyEntity,long,bool> releaseAiFocus;
        private static Action<MyEntity,ICollection<MyTuple<MyEntity,float>>> getSortedThreats;
        private static Func<MyEntity,bool> hasGridAi;
        private static Func<MyEntity,MyEntity,int,VRageMath.Vector3D?> predictedPosition;
        private static Func<MyEntity,MyTuple<bool,int,int>> projectilesLockedOn;
        private static readonly List<MyTuple<MyEntity,float>> threatBuffer=new List<MyTuple<MyEntity,float>>();

        internal static bool ApiReady => api.Ready;
        internal static void Load() => api.Load();
        internal static void Unload() => api.Unload();

        private static bool Attach(IReadOnlyDictionary<string,Delegate> methods)
        {
            getAiFocus=ModApiClient.Get<Func<MyEntity,int,MyEntity>>(methods,"GetAiFocusBase");
            setAiFocus=ModApiClient.Get<Func<MyEntity,MyEntity,int,bool>>(methods,"SetAiFocusBase");
            releaseAiFocus=ModApiClient.Get<Func<MyEntity,long,bool>>(methods,"ReleaseAiFocusBase");
            getSortedThreats=ModApiClient.Get<Action<MyEntity,ICollection<MyTuple<MyEntity,float>>>>(methods,"GetSortedThreatsBase");
            hasGridAi=ModApiClient.Get<Func<MyEntity,bool>>(methods,"HasGridAiBase");
            predictedPosition=ModApiClient.Get<Func<MyEntity,MyEntity,int,VRageMath.Vector3D?>>(methods,"GetPredictedTargetPositionBase");
            projectilesLockedOn=ModApiClient.Get<Func<MyEntity,MyTuple<bool,int,int>>>(methods,"GetProjectilesLockedOnBase");
            return getAiFocus!=null && getSortedThreats!=null;
        }

        private static void Detach()
        {
            getAiFocus=null; setAiFocus=null; releaseAiFocus=null; getSortedThreats=null; hasGridAi=null; predictedPosition=null; projectilesLockedOn=null;
            threatBuffer.Clear();
        }

        internal static bool HasAi(MyEntity grid) => ApiReady && grid!=null && (hasGridAi?.Invoke(grid) ?? true);

        internal static MyEntity Focus(MyEntity grid) => ApiReady && grid!=null ? getAiFocus(grid,0) : null;

        // Null when out of the weapon's trajectory range or the weapon is not ready.
        internal static VRageMath.Vector3D? PredictedPosition(MyEntity weapon,MyEntity target) =>
            ApiReady && weapon!=null && target!=null && predictedPosition!=null ? predictedPosition(weapon,target,0) : null;

        // The grid's tracked threats, sorted by WeaponCore. A shared buffer: valid until the next call.
        internal static IReadOnlyList<MyTuple<MyEntity,float>> Threats(MyEntity grid)
        {
            threatBuffer.Clear();
            if(ApiReady && grid!=null) getSortedThreats(grid,threatBuffer);
            return threatBuffer;
        }

        internal static bool SetFocus(MyEntity grid,MyEntity target) =>
            grid!=null && target!=null && (WeaponCoreInternals.RequestFocus(target) || setAiFocus!=null && setAiFocus(grid,target,0));

        internal static bool ReleaseFocus(MyEntity grid) =>
            grid!=null && (WeaponCoreInternals.RequestRelease() || releaseAiFocus!=null && releaseAiFocus(grid,MyAPIGateway.Session?.Player?.IdentityId ?? 0));

        // Number of WeaponCore projectiles currently homing on the grid; 0 when none or unknown.
        internal static int IncomingProjectiles(MyEntity grid)
        {
            if(!ApiReady || grid==null || projectilesLockedOn==null) return 0;
            var result=projectilesLockedOn(grid);
            return result.Item1 ? result.Item2 : 0;
        }
    }
}
