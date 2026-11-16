using HarmonyLib;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatchCategory("Targeting")]
    [HarmonyPatch(typeof(MyTargetFocusComponent),nameof(MyTargetFocusComponent.UpdateAimVector))]
    internal static class TargetFocusPatch
    {
        private static readonly System.Reflection.FieldInfo aim=AccessTools.Field(typeof(MyTargetFocusComponent),"m_aimMatrix");
        private static void Postfix(MyTargetFocusComponent __instance)
        {
            if(aim==null || !Main.VrActive || !ReferenceEquals(__instance.Entity,MySession.Static?.LocalCharacter)) return;
            if(Multiplayer.HeldTargeting.TryAim(MySession.Static.LocalCharacter,out var matrix) || ShipTargeting.TryAim(out matrix))
                aim.SetValue(__instance,matrix);
        }
    }
}
