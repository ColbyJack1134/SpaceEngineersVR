using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using SpaceEngineersVR.Player.Components;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyCockpit),nameof(MyCockpit.RemovePilot))]
    internal static class CockpitMovementReleasePatch
    {
        private static void Prefix(MyCockpit __instance) => VRMovementComponent.StopOwner(__instance);
    }

    [HarmonyPatch(typeof(MyEntityController),nameof(MyEntityController.TakeControl))]
    internal static class MovementReleasePatch
    {
        private static void Prefix(MyEntityController __instance,IMyControllableEntity entity)
        {
            if(!ReferenceEquals(__instance.ControlledEntity,entity) && (entity==null || entity.ControllerInfo.Controller==null))
                VRMovementComponent.StopOwner(__instance.ControlledEntity);
        }
    }
}
