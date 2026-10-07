using HarmonyLib;
using SpaceEngineersVR.Player;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.UpdateModelProperties))]
    internal static class CockpitScreenVisibilityPatch
    {
        private static void Prefix(uint id,string materialName,ref RenderFlags addFlags,ref RenderFlags removeFlags) =>
            CockpitRender.PreserveScreenVisibility(id,materialName,ref addFlags,ref removeFlags);
    }
}
