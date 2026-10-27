using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // Native paste velocity is the preview's last movement, which the hand ray turns into a throw.
    [HarmonyPatch]
    internal static class PasteVelocityPatch
    {
        private static readonly AccessTools.FieldRef<MyGridClipboard,Vector3> velocity=AccessTools.FieldRefAccess<MyGridClipboard,Vector3>("m_objectVelocity");
        private static readonly AccessTools.FieldRef<MyGridClipboard,bool> calculate=AccessTools.FieldRefAccess<MyGridClipboard,bool>("m_calculateVelocity");
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(MyGridClipboard),nameof(MyGridClipboard.Update));
            yield return AccessTools.DeclaredMethod(typeof(MyGridClipboardAdvanced),nameof(MyGridClipboardAdvanced.Update));
        }
        private static void Postfix(MyGridClipboard __instance)
        {
            if(!Main.VrActive || !calculate(__instance)) return;
            velocity(__instance)=MySession.Static?.ControlledEntity?.Entity?.GetTopMostParent()?.Physics?.LinearVelocity ?? Vector3.Zero;
        }
    }
}
