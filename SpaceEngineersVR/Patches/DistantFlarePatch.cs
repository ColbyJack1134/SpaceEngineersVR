using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Plugin;
using VRageRender;
using VRageRender.Messages;
using VRageRender.Lights;

namespace SpaceEngineersVR.Patches
{
    // Keep a live comparison switch while testing stereo occlusion changes.
    [HarmonyPatch]
    internal static class DistantFlarePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(
            AccessTools.TypeByName("VRageRender.MyFlareRenderer"),"Draw",new[] {
                AccessTools.TypeByName("VRage.Render11.Scene.Components.MyLightComponent"),typeof(float),typeof(bool) });
        private static bool Prefix(VRage.Render.Scene.Components.MyLightComponent light)
        {
            return !Main.VrActive || Common.Config.DistantFlares || light.Data.Glare.Type!=MyGlareTypeEnum.Distant;
        }
    }
}
