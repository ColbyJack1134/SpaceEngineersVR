using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.World;
using Sandbox.Game.Weapons;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(Sandbox.Game.Components.MyRenderComponentSkinnedEntity),"Draw")]
    internal static class HeldItemRenderPosePatch
    {
        private static void Prefix(Sandbox.Game.Components.MyRenderComponentSkinnedEntity __instance)
        {
            var character=__instance.Container.Entity as MyCharacter;
            if(!MotionToolPatch.Eligible(character) || !(character.CurrentWeapon is MyEntity item) ||
                !WeaponHandling.TryPose(character,out var pose,out _)) return;
            // Publish the item and final arm bones together before native skinning is queued.
            item.WorldMatrix=pose; TrackedArms.Update(character);
            SpatialUi.Publish();
        }
    }
    [HarmonyPatch(typeof(MyCharacterWeaponPositionComponent), nameof(MyCharacterWeaponPositionComponent.Update))]
    public static class MotionToolPatch
    {
        private static readonly PropertyInfo LogicalWorld = AccessTools.Property(typeof(MyCharacterWeaponPositionComponent),"LogicalPositionWorld");
        private static readonly PropertyInfo LogicalLocal = AccessTools.Property(typeof(MyCharacterWeaponPositionComponent),"LogicalPositionLocalSpace");
        private static readonly PropertyInfo Direction = AccessTools.Property(typeof(MyCharacterWeaponPositionComponent),"LogicalOrientationWorld");
        private static readonly PropertyInfo Crosshair = AccessTools.Property(typeof(MyCharacterWeaponPositionComponent),"LogicalCrosshairPoint");
        private static readonly PropertyInfo Graphical = AccessTools.Property(typeof(MyCharacterWeaponPositionComponent),"GraphicalPositionWorld");
        private static bool disabled;

        public static bool Eligible(MyCharacter character) => Main.VrActive && InputRouter.TrackedItems && character!=null && !character.IsDead && !character.IsSitting && !character.IsOnLadder &&
            character == MySession.Static?.LocalCharacter && MySession.Static.ControlledEntity == character;

        internal static void Refresh(MyCharacter character) { if (character?.WeaponPosition!=null) Postfix(character.WeaponPosition); }

        [HarmonyPostfix]
        private static void Postfix(MyCharacterWeaponPositionComponent __instance)
        {
            if (disabled || !Eligible(__instance.Character)) return;
            try
            {
                MatrixD hand;
                Vector3D muzzle;
                if (!WeaponHandling.TryPose(__instance.Character,out hand,out muzzle))
                {
                    if (!HandInteraction.TryWorldPose(Player.Player.HandR,out hand)) return;
                    hand.Translation += hand.Forward * 0.08;
                    muzzle=hand.Translation;
                }
                var profile=Multiplayer.HeldItemPose.Profile(__instance.Character);
                var direction=profile==null ? hand.Forward:Vector3D.TransformNormal(profile.Direction,hand);
                if(profile?.Tool==true && Multiplayer.HeldItemPose.TryToolRay(__instance.Character,out var ray))
                { muzzle=ray.Translation; direction=ray.Forward; }
                LogicalWorld.SetValue(__instance,muzzle,null);
                LogicalLocal.SetValue(__instance,Vector3D.Transform(muzzle,__instance.Character.PositionComp.WorldMatrixInvScaled),null);
                Direction.SetValue(__instance,direction,null);
                Crosshair.SetValue(__instance,muzzle + direction * 2000,null);
                Graphical.SetValue(__instance,hand.Translation,null);
                __instance.Character.ShootDirection = (Vector3)direction;
                if (__instance.Character.CurrentWeapon is MyEntity weapon) weapon.WorldMatrix = hand;
                if (__instance.Character.CurrentWeapon is MyEngineerToolBase tool) tool.UpdateSensorPosition();
                else if(__instance.Character.CurrentWeapon is MyHandDrill drill) Multiplayer.DrillContact.Refresh(drill);
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Motion tool override disabled; default aiming retained"); }
        }
    }
}
