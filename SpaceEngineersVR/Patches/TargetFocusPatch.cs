using HarmonyLib;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    // The lock cone is cast along m_aimMatrix; in a head-aimed turret it follows the head ray.
    [HarmonyPatch(typeof(MyTargetFocusComponent),nameof(MyTargetFocusComponent.UpdateAimVector))]
    internal static class TargetFocusPatch
    {
        private static readonly System.Reflection.FieldInfo aim=AccessTools.Field(typeof(MyTargetFocusComponent),"m_aimMatrix");
        private static void Postfix(MyTargetFocusComponent __instance)
        {
            if(aim==null || !Main.VrActive || !ReferenceEquals(__instance.Entity,MySession.Static?.LocalCharacter)) return;
            if(ShipTargeting.TryAim(out var matrix)) aim.SetValue(__instance,matrix);
        }
    }
}
