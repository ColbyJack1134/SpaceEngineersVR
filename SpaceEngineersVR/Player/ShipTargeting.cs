using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;

namespace SpaceEngineersVR.Player
{
    internal static class ShipTargeting
    {
        internal static bool CanLock(Sandbox.Game.Entities.IMyControllableEntity controlled) =>
            controlled is IMyTargetingCapableBlock target && target.IsTargetLockingEnabled() &&
            (!(controlled is MyCubeBlock block) || block.IsWorking && block.CubeGrid.IsPowerSwitchOn);
        internal static void SecondaryPressed(Sandbox.Game.Entities.IMyControllableEntity controlled)
        {
            if(!CanLock(controlled)) return;
            MySession.Static?.LocalCharacter?.Components.Get<MyTargetFocusComponent>()?.OnLockRequest();
        }
    }
}
