using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.ModAPI;
using SpaceEngineersVR.Plugin;
using VRage;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Player
{
    // Talks to WeaponCore (CoreSystems). Reads go through the public mod API.
    // Focus changes go through WC's own client request path (the same one its right-click UI uses),
    // because the public SetAiFocus/ReleaseAiFocus API only works on the server.
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

        // Reflection into the CoreSystems session for client-side focus requests.
        private static bool reflected,reflectionFailed;
        private static FieldInfo trackingAi,playerId,inGridAiBlock,construct,focus;
        private static MethodInfo requestAddFocus,requestReleaseActive,requestAddLock;
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
            reflected=false; reflectionFailed=false;
            trackingAi=playerId=inGridAiBlock=construct=focus=null;
            requestAddFocus=requestReleaseActive=requestAddLock=null;
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

        internal static bool SetFocus(MyEntity grid,MyEntity target)
        {
            if(grid==null || target==null) return false;
            if(TryClientRequest(target,true)) return true;
            return setAiFocus!=null && setAiFocus(grid,target,0);
        }

        internal static bool ReleaseFocus(MyEntity grid)
        {
            if(grid==null) return false;
            if(TryClientRequest(null,false)) return true;
            long id=MyAPIGateway.Session?.Player?.IdentityId ?? 0;
            return releaseAiFocus!=null && releaseAiFocus(grid,id);
        }

        // Number of WeaponCore projectiles currently homing on the grid; 0 when none or unknown.
        internal static int IncomingProjectiles(MyEntity grid)
        {
            if(!ApiReady || grid==null || projectilesLockedOn==null) return 0;
            var result=projectilesLockedOn(grid);
            return result.Item1 ? result.Item2 : 0;
        }

        // WeaponCore focus lock mode: when locked, the focus is kept even out of view. Null when unavailable.
        internal static bool? FocusLocked()
        {
            var mode=WeaponCoreInternals.Path(WeaponCoreInternals.TrackingAi,"Construct","Data","Repo","FocusData","Locked");
            return mode==null ? (bool?)null : mode.ToString()!="None";
        }

        internal static bool ToggleFocusLock()
        {
            if(!Reflect() || requestAddLock==null) return false;
            try
            {
                object session=WeaponCoreInternals.Session;
                if(session==null || !(bool)inGridAiBlock.GetValue(session)) return false;
                object ai=trackingAi.GetValue(session);
                object focusObject=ai==null ? null:focus.GetValue(construct.GetValue(ai));
                if(focusObject==null) return false;
                requestAddLock.Invoke(focusObject,new[] {ai,playerId.GetValue(session)});
                return true;
            }
            catch(Exception ex) { Logger.Warning(ex,"WeaponCore focus lock request failed"); return false; }
        }

        private static bool TryClientRequest(MyEntity target,bool add)
        {
            if(!Reflect()) return false;
            try
            {
                object session=WeaponCoreInternals.Session;
                if(session==null || !(bool)inGridAiBlock.GetValue(session)) return false;
                object ai=trackingAi.GetValue(session);
                if(ai==null) return false;
                object focusObject=focus.GetValue(construct.GetValue(ai));
                if(focusObject==null) return false;
                long player=(long)playerId.GetValue(session);
                if(add) requestAddFocus.Invoke(focusObject,new[] {target,ai,(object)player});
                else requestReleaseActive.Invoke(focusObject,new[] {ai,(object)player});
                return true;
            }
            catch(Exception ex)
            {
                reflectionFailed=true;
                Logger.Warning(ex,"WeaponCore focus request failed; falling back to server-only API");
                return false;
            }
        }

        private static bool Reflect()
        {
            if(reflected) return true;
            if(reflectionFailed) return false;
            try
            {
                // Not latched as a failure: the session can still be loading.
                if(!WeaponCoreInternals.Available) return false;
                // Resolve within the live session's assembly; older worlds' copies of the mod stay loaded.
                var sessionType=WeaponCoreInternals.SessionType;
                var aiType=sessionType.Assembly.GetType("CoreSystems.Support.Ai",false);
                var focusType=sessionType.Assembly.GetType("CoreSystems.Support.Focus",false);
                if(aiType==null || focusType==null) { reflectionFailed=true; return false; }
                trackingAi=AccessTools.Field(sessionType,"TrackingAi");
                playerId=AccessTools.Field(sessionType,"PlayerId");
                inGridAiBlock=AccessTools.Field(sessionType,"InGridAiBlock");
                construct=AccessTools.Field(aiType,"Construct");
                focus=construct==null ? null : AccessTools.Field(construct.FieldType,"Focus");
                requestAddFocus=AccessTools.Method(focusType,"RequestAddFocus",new[] {typeof(MyEntity),aiType,typeof(long)});
                requestReleaseActive=AccessTools.Method(focusType,"RequestReleaseActive",new[] {aiType,typeof(long)});
                requestAddLock=AccessTools.Method(focusType,"RequestAddLock",new[] {aiType,typeof(long)});
                reflected=trackingAi!=null && playerId!=null && inGridAiBlock!=null &&
                    construct!=null && focus!=null && requestAddFocus!=null && requestReleaseActive!=null;
                if(!reflected) { reflectionFailed=true; Logger.Info("WeaponCore internals changed; client focus requests unavailable"); }
                return reflected;
            }
            catch(Exception ex)
            {
                reflectionFailed=true;
                Logger.Warning(ex,"WeaponCore reflection failed");
                return false;
            }
        }
    }
}
