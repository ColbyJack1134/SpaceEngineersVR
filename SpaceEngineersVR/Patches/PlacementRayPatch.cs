using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyDefaultPlacementProvider))]
    internal static class PlacementRayPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("get_RayStart")]
        private static void Start(ref Vector3D __result)
        {
            if (PlacementControls.TryPose(out MatrixD pose))
                __result = pose.Translation;
        }
        [HarmonyPostfix]
        [HarmonyPatch("get_RayDirection")]
        private static void Direction(ref Vector3D __result)
        {
            if (PlacementControls.TryPose(out MatrixD pose))
                __result = pose.Forward;
        }
    }

    [HarmonyPatch(typeof(MyGridClipboard), "GetPasteMatrix")]
    internal static class ClipboardPosePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref MatrixD __result)
        {
            if (PlacementControls.ClipboardActive && PlacementControls.TryPose(out MatrixD pose)) __result = pose;
        }
    }
}
