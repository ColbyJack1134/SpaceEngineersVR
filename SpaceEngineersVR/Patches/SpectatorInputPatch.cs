using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRage.Input;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyInputExtensions),nameof(MyInputExtensions.GetPositionDelta),new[] {typeof(IMyInput)})]
    internal static class SpectatorTranslationPatch
    {
        private static bool Prefix(ref Vector3 __result)
        {
            if(!SpectatorView.Active) return true;
            __result=SpectatorView.Movement;
            return false;
        }
    }
    [HarmonyPatch(typeof(MyInputExtensions),nameof(MyInputExtensions.GetRotation),new[] {typeof(IMyInput)})]
    internal static class SpectatorRotationPatch
    {
        private static bool Prefix(ref Vector2 __result)
        {
            if(!SpectatorView.Active) return true;
            __result=SpectatorView.Rotation;
            return false;
        }
    }
    [HarmonyPatch]
    internal static class SpectatorRollPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(string name in new[] {"GetRoll","GetDeveloperRoll"})
                yield return AccessTools.Method(typeof(MyInputExtensions),name,new[] {typeof(IMyInput)});
        }
        private static bool Prefix(ref float __result)
        {
            if(!SpectatorView.Active) return true;
            __result=SpectatorView.Roll;
            return false;
        }
    }
}
