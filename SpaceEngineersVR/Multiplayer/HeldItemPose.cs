using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using SpaceEngineersVR.Player;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class HeldItemPose
    {
        internal static Func<MyCharacter,MatrixD?> Local { get; set; }
        internal static Func<MyCharacter,MatrixD?> LocalToolRay { get; set; }
        internal static Func<MyCharacter,bool?> LocalSupported { get; set; }
        internal static bool Supported(MyCharacter character) => LocalSupported?.Invoke(character) ?? MultiplayerRuntime.ItemSupported(character);
        internal static Action<MyCharacter,ItemKind> Feedback { get; set; }
        private static readonly FieldInfo toolOwner=AccessTools.Field(typeof(MyEngineerToolBase),"Owner");
        internal static MyCharacter Owner(object item) => (item as MyAutomaticRifleGun)?.Owner ?? (item as MyHandDrill)?.Owner ?? (item is MyEngineerToolBase ? toolOwner.GetValue(item) as MyCharacter:null);
        internal static WeaponProfile Profile(MyCharacter character) => WeaponProfile.Find(character?.CurrentWeapon?.PhysicalObject?.SubtypeName,(character?.CurrentWeapon as MyEntity)?.Model?.AssetName);
        internal static bool TryGet(MyCharacter character,out MatrixD model,out WeaponProfile profile)
        {
            model=MatrixD.Identity; profile=Profile(character);
            if(profile==null || character==null || character.IsDead || character.IsSitting || character.IsOnLadder) return false;
            var local=Local?.Invoke(character);
            if(local.HasValue) model=local.Value;
            else if(!MultiplayerRuntime.ItemPose(character,out model)) return false;
            return model.IsValid() && Vector3D.DistanceSquared(model.Translation,character.PositionComp.GetPosition())<9;
        }
        internal static bool TryToolRay(MyCharacter character,out MatrixD ray)
        {
            ray=MatrixD.Identity;
            var local=LocalToolRay?.Invoke(character);
            if(local.HasValue) ray=local.Value;
            else if(!MultiplayerRuntime.ToolRay(character,out ray)) return false;
            return ray.IsValid() && Vector3D.DistanceSquared(ray.Translation,character.PositionComp.GetPosition())<9;
        }
        internal static MatrixD Working(MatrixD model,WeaponProfile profile) => MatrixD.CreateWorld(Vector3D.Transform(profile.Muzzle,model),
            Vector3D.TransformNormal(profile.Direction,model),model.Up);
        internal static bool Clear(MyCharacter character,Vector3D from,Vector3D to,bool ignoreCharacters=false)
        {
            if(Vector3D.DistanceSquared(from,to)<.000001) return true;
            var line=new LineD(from,to);
            return !MyEntities.GetIntersectionWithLine(ref line,character,character.CurrentWeapon as MyEntity,ignoreChildren:false,ignoreFloatingObjects:false,ignoreCharacters:ignoreCharacters).HasValue;
        }
        internal static bool MuzzleClear(MyCharacter character,MatrixD model,WeaponProfile profile)
        {
            var grip=Vector3D.Transform(profile.Primary,model); var muzzle=Vector3D.Transform(profile.Muzzle,model);
            // Close targets may overlap the held weapon without preventing a shot.
            return Clear(character,character.GetHeadMatrix(true,true).Translation,grip,ignoreCharacters:true) && Clear(character,grip,muzzle,ignoreCharacters:true);
        }
    }
    [HarmonyPatch(typeof(MyAutomaticRifleGun),nameof(MyAutomaticRifleGun.Shoot),new[] {typeof(MyShootActionEnum),typeof(Vector3),typeof(Vector3D?),typeof(string)})]
    internal static class HeldWeaponShotPatch
    {
        private static bool Prefix(MyAutomaticRifleGun __instance,MyShootActionEnum action,ref Vector3 direction,ref Vector3D? overrideWeaponPos,out bool __state)
        {
            __state=false;
            if(action!=MyShootActionEnum.PrimaryAction || !HeldItemPose.TryGet(__instance.Owner,out var model,out var profile)) return true;
            if(!HeldItemPose.MuzzleClear(__instance.Owner,model,profile)) return false;
            __instance.WorldMatrix=model; direction=(Vector3)model.Forward;
            // Native handheld shooting subtracts 25 cm before creating the projectile or missile.
            overrideWeaponPos=Vector3D.Transform(profile.Muzzle,model)+direction*.25f;
            __state=true; return true;
        }
        private static void Postfix(MyAutomaticRifleGun __instance,bool __state)
        { if(__state) HeldItemPose.Feedback?.Invoke(__instance.Owner,HeldItemPose.Profile(__instance.Owner).Kind); }
    }
    [HarmonyPatch(typeof(MyProjectile),nameof(MyProjectile.Start))]
    internal static class HeldProjectileStartPatch
    {
        private static void Prefix(MyEntity weapon,MyEntity ownerEntity,ref Vector3D origin,Vector3 directionNormalized)
        {
            var character=ownerEntity as MyCharacter;
            if(character?.CurrentWeapon is MyAutomaticRifleGun gun && weapon==gun && HeldItemPose.TryGet(character,out _,out _))
                origin-=.1*(Vector3D)directionNormalized;
        }
    }
    [HarmonyPatch]
    internal static class HeldDirectionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(var type in new[] {typeof(MyAutomaticRifleGun),typeof(MyEngineerToolBase),typeof(MyHandDrill)})
                yield return AccessTools.Method(type,"DirectionToTarget");
        }
        private static void Postfix(object __instance,ref Vector3 __result)
        {
            var owner=HeldItemPose.Owner(__instance);
            if(HeldItemPose.TryGet(owner,out var model,out var profile))
                __result=(Vector3)(profile.Tool && HeldItemPose.TryToolRay(owner,out var ray) ? ray.Forward:HeldItemPose.Working(model,profile).Forward);
        }
    }
    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.UpdateShootDirection))]
    internal static class HeldStraightAimPatch
    {
        private static void Prefix(MyCharacter __instance,ref bool shootStraight)
        { if(HeldItemPose.TryGet(__instance,out _,out _)) shootStraight=false; }
    }
}
