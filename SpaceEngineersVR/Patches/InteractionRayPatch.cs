using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // Replace only the ray's origin/direction, before the native endpoint calculation.
    // Keep geometry ordering, model use-detectors, occlusion, selection lifecycle,
    // interaction distance and action dispatch in the game's implementation.
    [HarmonyPatch]
    internal static class InteractionRayPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(MyCharacterRaycastDetectorComponent), "DoDetection");
            yield return AccessTools.DeclaredMethod(AccessTools.TypeByName(
                "Sandbox.Game.Entities.Character.Components.MyCharacterClosestDetectorComponent"), "DoDetection");
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            var distance = AccessTools.Field(typeof(MyConstants), nameof(MyConstants.DEFAULT_INTERACTIVE_DISTANCE));
            var matches = Enumerable.Range(2, Math.Max(0, code.Count - 2)).Where(i =>
                code[i].LoadsField(distance) && code[i - 2].IsLdloc() && code[i - 1].IsLdloc()).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Unrecognized native interaction ray in " + __originalMethod.DeclaringType.FullName);
            int start = matches[0] - 2;
            int origin = code[start].LocalIndex(), direction = code[start + 1].LocalIndex();
            var locals = __originalMethod.GetMethodBody().LocalVariables;
            Type directionType = locals[direction].LocalType;
            if (locals[origin].LocalType != typeof(Vector3D) ||
                (directionType != typeof(Vector3D) && directionType != typeof(Vector3)))
                throw new InvalidOperationException("Unrecognized native interaction coordinates");
            var replacement = new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.LoadLocal(origin, true),
                CodeInstruction.LoadLocal(direction, true),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InteractionRayPatch),
                    directionType == typeof(Vector3D) ? nameof(OverrideRay) : nameof(OverrideAreaRay)))
            };
            // Both head/camera branches must enter the override, including a branch
            // aimed directly at the endpoint expression in optimized game builds.
            replacement[0].MoveLabelsFrom(code[start]);
            replacement[0].MoveBlocksFrom(code[start]);
            code.InsertRange(start, replacement);
            return code;
        }

        private static void OverrideRay(MyCharacterDetectorComponent detector, ref Vector3D origin, ref Vector3D direction)
        {
            if (!HandInteraction.TryInteractionRay(out LineD ray) ||
                detector.Character != MySession.Static?.LocalCharacter) return;
            origin = ray.From;
            direction = ray.Direction;
        }

        private static void OverrideAreaRay(MyCharacterDetectorComponent detector, ref Vector3D origin, ref Vector3 direction)
        {
            Vector3D preciseDirection = direction;
            OverrideRay(detector, ref origin, ref preciseDirection);
            direction = (Vector3)preciseDirection;
        }
    }
}
