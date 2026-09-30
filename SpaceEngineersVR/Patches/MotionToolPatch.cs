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
                if (__instance.Character.CurrentWeapon is MyEntity weapon) weapon.WorldMatrix = hand;
                LogicalWorld.SetValue(__instance,muzzle,null);
                LogicalLocal.SetValue(__instance,Vector3D.Transform(muzzle,__instance.Character.PositionComp.WorldMatrixInvScaled),null);
                Direction.SetValue(__instance,hand.Forward,null);
                Crosshair.SetValue(__instance,muzzle + hand.Forward * 2000,null);
                Graphical.SetValue(__instance,hand.Translation,null);
                __instance.Character.ShootDirection = (Vector3)hand.Forward;
                if (__instance.Character.CurrentWeapon is MyEngineerToolBase tool) tool.UpdateSensorPosition();
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Motion tool override disabled; default aiming retained"); }
        }
    }
    [HarmonyPatch(typeof(MyAutomaticRifleGun),nameof(MyAutomaticRifleGun.Shoot),
        new[] { typeof(VRage.Game.ModAPI.MyShootActionEnum),typeof(Vector3),typeof(Vector3D?),typeof(string) })]
    internal static class MotionWeaponShotPatch
    {
        private static void Prefix(MyAutomaticRifleGun __instance, ref Vector3 direction, ref Vector3D? overrideWeaponPos)
        {
            var character=MySession.Static?.LocalCharacter;
            if (!InputRouter.Gameplay || character?.CurrentWeapon!=__instance || !MotionToolPatch.Eligible(character) ||
                !WeaponHandling.TryPose(character,out MatrixD model,out Vector3D muzzle)) return;
            __instance.WorldMatrix=model;
            direction=(Vector3)model.Forward;
            // The native rifle/launcher subtracts 25 cm from its supplied shot origin.
            overrideWeaponPos=muzzle+model.Forward*0.25;
        }
    }
    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.UpdateShootDirection))]
    public static class MotionAimPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MyCharacter __instance)
        {
            if (!MotionToolPatch.Eligible(__instance)) return;
            if (WeaponHandling.TryPose(__instance,out MatrixD model,out _)) __instance.ShootDirection=(Vector3)model.Forward;
            else if (HandInteraction.TryWorldPose(Player.Player.HandR,out MatrixD hand)) __instance.ShootDirection=(Vector3)hand.Forward;
        }
    }
}
