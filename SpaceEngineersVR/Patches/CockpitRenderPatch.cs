using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRage.Utils;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class CockpitRenderPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Scene.Components.MyRenderableComponent"),"RebuildRenderProxies");
        private static void Postfix(object __instance) => CockpitRender.Verify(__instance);
    }
    [HarmonyPatch]
    internal static class CockpitRuntimeSectionsPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyMeshes"),"CreateRuntimeMesh");
        private static void Postfix(MyStringId nameKey,object __result)
        {
            if (nameKey.ToString().StartsWith("SEVR_Fighter_",System.StringComparison.Ordinal)) CockpitRender.InitializeRuntimeSections(__result);
        }
    }
}
