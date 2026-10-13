using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Weapons;
using Sandbox.Game.World;
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
            return model.IsValid() && WithinReach(character,Vector3D.Transform(profile.Primary,model));
        }
        // Stretched arm plus a leaning head.
        internal const double HandReach=1.5;
        internal static bool WithinReach(MyCharacter character,Vector3D point) =>
            Vector3D.DistanceSquared(point,character.GetHeadMatrix(true,true).Translation)<=HandReach*HandReach;
        internal static bool TryToolRay(MyCharacter character,out MatrixD ray)
        {
            ray=MatrixD.Identity;
            var local=LocalToolRay?.Invoke(character);
            if(local.HasValue) ray=local.Value;
            else if(!MultiplayerRuntime.ToolRay(character,out ray)) return false;
            return ray.IsValid() && WithinReach(character,ray.Translation);
        }
        private static readonly FieldInfo startingVertAngle=AccessTools.Field(typeof(MyAutomaticRifleGun),"m_startingVertAngle"),
            horizontalAngle=AccessTools.Field(typeof(MyAutomaticRifleGun),"m_horizontalAngle");
        // Native recoil moves head angles the VR camera ignores; degrees up and left.
        internal static Vector2 Recoil(MyCharacter character)
        {
            if(!(character?.CurrentWeapon is MyAutomaticRifleGun gun) || MySession.Static?.Settings.EnableRecoil!=true) return Vector2.Zero;
            var kick=new Vector2(gun.IsRecoiling ? character.HeadLocalXAngle-(float)startingVertAngle.GetValue(gun):0,(float)horizontalAngle.GetValue(gun));
            return kick.IsValid() ? kick:Vector2.Zero;
        }
        internal static MatrixD Kick(MatrixD model,WeaponProfile profile,Vector2 recoil)
        {
            if(recoil==Vector2.Zero) return model;
            var grip=Vector3D.Transform(profile.Primary,model);
            var turn=MatrixD.CreateFromAxisAngle(model.Right,MathHelper.ToRadians(recoil.X))*MatrixD.CreateFromAxisAngle(model.Up,MathHelper.ToRadians(recoil.Y));
            model.Translation-=grip; model*=turn; model.Translation+=grip;
            return model;
        }
        // Native tool sensors start on the body axis at neck height.
        internal static Vector3D ReachOrigin(MyCharacter character)
        {
            var definition=character.Definition;
            float height=(character.IsCrouching ? definition.CharacterCollisionCrouchHeight:definition.CharacterCollisionHeight)-definition.CharacterHeadHeight*.5f;
            return Vector3D.Transform(new Vector3D(0,height,0),character.PositionComp.WorldMatrixRef);
        }
        internal static double ReachAlong(Vector3D center,Vector3D start,Vector3D direction,double radius)
        {
            var offset=start-center;
            double b=Vector3D.Dot(offset,direction),c=offset.LengthSquared()-radius*radius;
            return c>0 ? 0:Math.Max(0,Math.Sqrt(Math.Max(0,b*b-c))-b);
        }
        internal static MatrixD Working(MatrixD model,WeaponProfile profile) => MatrixD.CreateWorld(Vector3D.Transform(profile.Muzzle,model),
            Vector3D.TransformNormal(profile.Direction,model),model.Up);
        internal static MatrixD ToolAim(MyCharacter owner,MatrixD model,WeaponProfile profile)
            => ToolAim(owner,model,profile,Supported(owner));
        internal static MatrixD ToolAim(MyCharacter owner,MatrixD model,WeaponProfile profile,bool supported)
        {
            if(supported) return Working(model,profile);
            return TryToolRay(owner,out var finger) ? finger:profile.Kind==ItemKind.Drill ? model:Working(model,profile);
        }
        internal static double ToolReach(MyCharacter owner,MatrixD model,WeaponProfile profile,MatrixD ray,double nativeReach)
        {
            double reach=owner==null ? nativeReach:ReachAlong(ReachOrigin(owner),ray.Translation,ray.Forward,nativeReach);
            if(!Supported(owner)) return reach;
            var grip=Vector3D.Transform(profile.Primary,model);
            return Math.Max(0,Math.Min(reach,nativeReach-Math.Max(0,Vector3D.Dot(ray.Translation-grip,ray.Forward))));
        }
        // Players never block a held item from reaching its grip, muzzle or tool ray.
        internal static bool Clear(MyCharacter character,Vector3D from,Vector3D to)
        {
            if(Vector3D.DistanceSquared(from,to)<.000001) return true;
            var line=new LineD(from,to);
            return !MyEntities.GetIntersectionWithLine(ref line,character,character.CurrentWeapon as MyEntity,ignoreChildren:false,ignoreFloatingObjects:false,ignoreCharacters:true).HasValue;
        }
        internal static bool MuzzleClear(MyCharacter character,MatrixD model,WeaponProfile profile)
        {
            var grip=Vector3D.Transform(profile.Primary,model); var muzzle=Vector3D.Transform(profile.Muzzle,model);
            // Close targets may overlap the held weapon without preventing a shot.
            return Clear(character,character.GetHeadMatrix(true,true).Translation,grip) && Clear(character,grip,muzzle);
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
                __result=(Vector3)(profile.Tool ? HeldItemPose.ToolAim(owner,model,profile).Forward:HeldItemPose.Working(model,profile).Forward);
        }
    }
    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.UpdateShootDirection))]
    internal static class HeldStraightAimPatch
    {
        private static void Prefix(MyCharacter __instance,ref bool shootStraight)
        { if(HeldItemPose.TryGet(__instance,out _,out _)) shootStraight=false; }
    }
}
