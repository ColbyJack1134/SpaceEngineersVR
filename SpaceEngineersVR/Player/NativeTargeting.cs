using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.ModAPI;

namespace SpaceEngineersVR.Player
{
    internal static class NativeTargeting
    {
        internal static bool Enabled(Sandbox.Game.Entities.IMyControllableEntity controlled)
        {
            if(controlled is MyCharacter character) return character.IsHandWeaponTargetLockingCapable && !character.IsElectricityDisrupted;
            return controlled is IMyTargetingCapableBlock capable && capable.IsTargetLockingEnabled() && !capable.IsShipToolSelected() &&
                (!(controlled is MyCubeBlock block) || block.IsWorking && block.CubeGrid.IsPowerSwitchOn);
        }
        internal static IMyShootOrigin Origin(Sandbox.Game.Entities.IMyControllableEntity controlled) =>
            controlled is MyCharacter character ? character.CurrentWeapon as IMyShootOrigin :
            controlled is IMyTargetingCapableBlock capable && capable.CanActiveToolShoot() ? controlled.Entity as IMyShootOrigin:null;
    }
}
