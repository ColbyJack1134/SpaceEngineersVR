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

        public static bool Eligible(MyCharacter character) => !disabled && Main.VrActive && InputRouter.TrackedItems && character!=null && !character.IsDead && !character.IsSitting && !character.IsOnLadder &&
            character == MySession.Static?.LocalCharacter && MySession.Static.ControlledEntity == character;

        internal static void Refresh(MyCharacter character) { if (character?.WeaponPosition!=null) Apply(character.WeaponPosition,false); }

        // Direction a host without SEVR should fire along: from its own weapon origin to what the held item points at.
        // The native weapon update stops without a held item, so a value older than a couple of frames is discarded.
        private static Vector3D? hostAim;
        private static int hostAimFrame;
        internal static Vector3D? HostAim => MySession.Static!=null && MySession.Static.GameplayFrameCounter-hostAimFrame<=2 ? hostAim:null;
        private const double ConvergeRange=300;

        internal static bool TryAim(MyCharacter character,out MatrixD hand,out Vector3D muzzle,out Vector3D direction)
        {
            direction=Vector3D.Zero;
            if (!WeaponHandling.TryPose(character,out hand,out muzzle))
            {
                if (!HandInteraction.TryWorldPose(Player.Handedness.ActionHand,out hand)) return false;
                hand.Translation += hand.Forward * 0.08;
                muzzle=hand.Translation;
            }
            var profile=Multiplayer.HeldItemPose.Profile(character);
            direction=profile==null ? hand.Forward:Vector3D.TransformNormal(profile.Direction,hand);
            if(profile?.Sight!=null && character.ZoomMode==MyZoomModeEnum.IronSight)
            {
                var sight=Vector3D.Transform(profile.Sight.Value,hand); var barrel=direction;
                direction=SightAim(muzzle,barrel,sight,start=>HandInteraction.ObstacleDistance(MatrixD.CreateWorld(start,barrel,Vector3D.CalculatePerpendicularVector(barrel)),(float)ConvergeRange));
            }
            if(profile?.Tool==true && Multiplayer.HeldItemPose.TryToolRay(character,out var ray))
            { muzzle=ray.Translation; direction=ray.Forward; }
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(MyCharacterWeaponPositionComponent __instance) => Apply(__instance,true);

        private static void Apply(MyCharacterWeaponPositionComponent component,bool nativeOrigin)
        {
            if (!Eligible(component.Character)) { if (nativeOrigin) hostAim=null; return; }
            try
            {
                // Read before the override below replaces the native logical position.
                var origin=(Vector3D)LogicalWorld.GetValue(component,null);
                if (!TryAim(component.Character,out var hand,out var muzzle,out var direction)) { if (nativeOrigin) hostAim=null; return; }
                if (nativeOrigin) { hostAim=Converge(component.Character,origin,muzzle,direction); hostAimFrame=MySession.Static.GameplayFrameCounter; }
                LogicalWorld.SetValue(component,muzzle,null);
                LogicalLocal.SetValue(component,Vector3D.Transform(muzzle,component.Character.PositionComp.WorldMatrixInvScaled),null);
                Direction.SetValue(component,direction,null);
                Crosshair.SetValue(component,muzzle + direction * 2000,null);
                Graphical.SetValue(component,hand.Translation,null);
                component.Character.ShootDirection = (Vector3)direction;
                if (component.Character.CurrentWeapon is MyEntity weapon) weapon.WorldMatrix = hand;
                if (component.Character.CurrentWeapon is MyEngineerToolBase tool) tool.UpdateSensorPosition();
                else if(component.Character.CurrentWeapon is MyHandDrill drill) Multiplayer.DrillContact.Refresh(drill);
            }
            catch(Exception ex) { disabled=true; hostAim=null; Logger.Warning(ex,"Motion tool override disabled; default aiming retained"); }
        }

        // Ironsight mode is synced, so the host computes the same aim for a remote character. The sight line is sampled
        // from the muzzle plane so the gun's own front sight cannot block it; the shot converges on what the sights cover.
        internal static Vector3D SightAim(Vector3D muzzle,Vector3D direction,Vector3D sight,Func<Vector3D,float> reach)
        {
            var start=sight+direction*Vector3D.Dot(muzzle-sight,direction);
            var target=start+direction*Math.Max(.05f,reach(start));
            var aim=target-muzzle;
            return aim.LengthSquared()>1e-6 ? Vector3D.Normalize(aim):direction;
        }
        private static Vector3D? Converge(MyCharacter character,Vector3D origin,Vector3D muzzle,Vector3D direction)
        {
            var profile=Multiplayer.HeldItemPose.Profile(character);
            // Welders and grinders already send their target block.
            if (profile==null || profile.Tool && profile.Kind!=ItemKind.Drill || !origin.IsValid() || !direction.IsValid()) return null;
            float reach=HandInteraction.ObstacleDistance(MatrixD.CreateWorld(muzzle,direction,Vector3D.CalculatePerpendicularVector(direction)),(float)ConvergeRange);
            var aim=muzzle+direction*reach-origin;
            return aim.LengthSquared()>1e-4 ? Vector3D.Normalize(aim):direction;
        }
    }
}
