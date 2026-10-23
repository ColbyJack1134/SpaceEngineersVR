using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class ShipTargeting
    {
        internal static bool CanLock(Sandbox.Game.Entities.IMyControllableEntity controlled) =>
            controlled is IMyTargetingCapableBlock target && target.IsTargetLockingEnabled() &&
            (!(controlled is MyCubeBlock block) || block.IsWorking && block.CubeGrid.IsPowerSwitchOn);
        // ahead: the press came from a hand on a physical stick.
        internal static void SecondaryPressed(Sandbox.Game.Entities.IMyControllableEntity controlled,bool ahead=false)
        {
            if(TargetLock.OnLockPress(ahead)) return;
            if(!CanLock(controlled)) return;
            MySession.Static?.LocalCharacter?.Components.Get<MyTargetFocusComponent>()?.OnLockRequest();
        }
        // Vanilla takes the lock cone from the barrel in a turret, which lags the head ray while it turns.
        internal static bool TryAim(out MatrixD aim)
        {
            aim=MatrixD.Identity;
            if(!Player.Headset.pose.isTracked || !TurretHeadAim.Active) return false;
            aim=TurretHeadAim.Head;
            return aim.IsValid();
        }
    }
}
