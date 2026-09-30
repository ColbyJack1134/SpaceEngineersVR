using HarmonyLib;
using Sandbox.Game.Entities;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // Native view/render packets, controller conversion and interaction share one anchor.
    [HarmonyPatch(typeof(MyCockpit),nameof(MyCockpit.GetHeadMatrix))]
    internal static class SeatFitPatch
    {
        private static void Postfix(MyCockpit __instance,ref MatrixD __result)
        {
            if (__instance==SeatFit.Seat && SeatFit.Eligible(__instance))
                __result.Translation+=Vector3D.TransformNormal(SeatFit.Offset,__instance.WorldMatrix);
        }
    }
}
