using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class RemoteScenePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Render.MyOffscreenRenderer"),"Render");
            yield return AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Culling.Occlusion.MyOcclusionTask"),"DoWork");
        }
        private static bool Prefix() => !RemoteScene.Active;
    }
    [HarmonyPatch]
    internal static class RemoteOcclusionPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render.Scene.MyActor"),"IsOccluded");
        private static bool Prefix(ref bool __result)
        {
            if(!RemoteScene.Active) return true;
            __result=false; return false;
        }
    }
}
