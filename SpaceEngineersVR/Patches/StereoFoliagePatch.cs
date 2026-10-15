using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class StereoFoliageBeginPatch
    {
        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(AccessTools.TypeByName("VRageRender.MyFoliageRenderingPass"),"Begin");
        private static void Postfix(object __instance) => StereoFoliage.Begin(__instance);
    }
    [HarmonyPatch]
    internal static class StereoFoliageEndPatch
    {
        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(AccessTools.TypeByName("VRageRender.MyFoliageRenderingPass"),"End");
        private static void Prefix(object __instance) => StereoFoliage.End(__instance);
    }
}
