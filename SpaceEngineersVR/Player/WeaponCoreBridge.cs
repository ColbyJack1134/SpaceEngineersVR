using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using VRage;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Player
{
    // WeaponCore's public mod API. Focus changes try WeaponCore's client request path first (WeaponCoreInternals), since
    // the public focus API only works on the server.
    internal static class WeaponCoreBridge
    {
        private const long Channel=67549756549;
        private static bool registered;
        private static Func<MyEntity,int,MyEntity> getAiFocus;
        private static Func<MyEntity,MyEntity,int,bool> setAiFocus;
        private static Func<MyEntity,long,bool> releaseAiFocus;
        private static Action<MyEntity,ICollection<MyTuple<MyEntity,float>>> getSortedThreats;
        private static Func<MyEntity,bool> hasGridAi;
        private static Func<MyEntity,MyEntity,int,VRageMath.Vector3D?> predictedPosition;

        private static Func<MyEntity,MyTuple<bool,int,int>> projectilesLockedOn;

        private static readonly List<MyTuple<MyEntity,float>> threatBuffer=new List<MyTuple<MyEntity,float>>();

        internal static bool ApiReady { get; private set; }

        internal static void Load()
        {
            if(registered || MyAPIGateway.Utilities==null) return;
            registered=true;
            MyAPIGateway.Utilities.RegisterMessageHandler(Channel,HandleMessage);
            MyAPIGateway.Utilities.SendModMessage(Channel,"ApiEndpointRequest");
        }

        internal static void Unload()
        {
            if(registered && MyAPIGateway.Utilities!=null) MyAPIGateway.Utilities.UnregisterMessageHandler(Channel,HandleMessage);
            registered=false; ApiReady=false;
            getAiFocus=null; setAiFocus=null; releaseAiFocus=null; getSortedThreats=null; hasGridAi=null; predictedPosition=null; projectilesLockedOn=null;
            threatBuffer.Clear();
        }

        private static void HandleMessage(object message)
        {
            // Our own "ApiEndpointRequest" string echoes back on this channel; only dictionaries are endpoints.
            if(!(message is IReadOnlyDictionary<string,Delegate> methods)) return;
            if(methods.Count==0) { ApiReady=false; return; }
            try
            {
                getAiFocus=Get<Func<MyEntity,int,MyEntity>>(methods,"GetAiFocusBase");
                setAiFocus=Get<Func<MyEntity,MyEntity,int,bool>>(methods,"SetAiFocusBase");
                releaseAiFocus=Get<Func<MyEntity,long,bool>>(methods,"ReleaseAiFocusBase");
                getSortedThreats=Get<Action<MyEntity,ICollection<MyTuple<MyEntity,float>>>>(methods,"GetSortedThreatsBase");
                hasGridAi=Get<Func<MyEntity,bool>>(methods,"HasGridAiBase");
                predictedPosition=Get<Func<MyEntity,MyEntity,int,VRageMath.Vector3D?>>(methods,"GetPredictedTargetPositionBase");
                projectilesLockedOn=Get<Func<MyEntity,MyTuple<bool,int,int>>>(methods,"GetProjectilesLockedOnBase");
                ApiReady=getAiFocus!=null && getSortedThreats!=null;
                Logger.Info("WeaponCore API attached; ready="+ApiReady);
            }
            catch(Exception ex) { ApiReady=false; Logger.Warning(ex,"WeaponCore API attach failed; VR target lock disabled"); }
        }

        private static T Get<T>(IReadOnlyDictionary<string,Delegate> methods,string name) where T:class =>
            methods.TryGetValue(name,out var method) ? method as T : null;

        internal static bool HasAi(MyEntity grid) => ApiReady && grid!=null && (hasGridAi?.Invoke(grid) ?? true);

        internal static MyEntity Focus(MyEntity grid) => ApiReady && grid!=null ? getAiFocus(grid,0) : null;

        // Null when out of the weapon's trajectory range or the weapon is not ready.
        internal static VRageMath.Vector3D? PredictedPosition(MyEntity weapon,MyEntity target) =>
            ApiReady && weapon!=null && target!=null && predictedPosition!=null ? predictedPosition(weapon,target,0) : null;

        internal static List<MyTuple<MyEntity,float>> Threats(MyEntity grid)
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
