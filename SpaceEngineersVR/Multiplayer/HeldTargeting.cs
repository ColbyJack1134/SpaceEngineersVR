using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.EntityComponents;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class HeldTargeting
    {
        internal static bool TryAim(MyCharacter character,out MatrixD aim)
        {
            aim=MatrixD.Identity;
            if(character?.IsHandWeaponTargetLockingCapable!=true || !HeldItemPose.TryGet(character,out var model,out var profile)) return false;
            aim=MatrixD.CreateWorld(Vector3D.Transform(profile.Sight ?? profile.Muzzle,model),
                Vector3D.TransformNormal(profile.Sight.HasValue ? profile.SightDirection:profile.Direction,model),model.Up);
            return aim.IsValid();
        }
        internal static MatrixD LockAim(MyCharacter character,bool includeY,bool includeX,bool forceHeadAnim,bool forceHeadBone,bool preferLocalOverSync) =>
            TryAim(character,out var aim) ? aim:character.GetHeadMatrix(includeY,includeX,forceHeadAnim,forceHeadBone,preferLocalOverSync);
    }

    [HarmonyPatchCategory("HeldTools")]
    [HarmonyPatch(typeof(MyTargetLockingComponent),"CanTarget",new[] {typeof(Sandbox.ModAPI.IMyTargetingCapableBlock),typeof(VRage.Game.Entity.MyEntity),typeof(bool)})]
    internal static class HeldTargetingPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var head=AccessTools.Method(typeof(MyCharacter),nameof(MyCharacter.GetHeadMatrix));
            var aim=AccessTools.Method(typeof(HeldTargeting),nameof(HeldTargeting.LockAim));
            int replaced=0;
            foreach(var instruction in instructions)
            {
                // Replace only the handheld aim matrix; keep native range, cone,
                // permission and visibility checks.
                if(instruction.Calls(head)) { instruction.opcode=OpCodes.Call; instruction.operand=aim; replaced++; }
                yield return instruction;
            }
            if(replaced!=1) throw new InvalidOperationException("Native handheld lock aim changed");
        }
    }
}
