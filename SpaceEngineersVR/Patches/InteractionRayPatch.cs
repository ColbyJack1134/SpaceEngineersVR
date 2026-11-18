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
    [HarmonyPatchCategory("HandInteraction")]
    [HarmonyPatch]
    internal static class InteractionRayPatch
    {
        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(MyCharacterRaycastDetectorComponent), "DoDetection");
            yield return AccessTools.DeclaredMethod(AccessTools.TypeByName(
                "Sandbox.Game.Entities.Character.Components.MyCharacterClosestDetectorComponent"), "DoDetection");
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod, ILGenerator generator)
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
            var length = generator.DeclareLocal(typeof(float));
            bool area = directionType == typeof(Vector3);
            var replacement = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldarg_0),
                CodeInstruction.LoadLocal(origin, true),
                CodeInstruction.LoadLocal(direction, true)
            };
            if (area)
            {
                if (start + 5 >= code.Count || code[start + 3].opcode != OpCodes.Ldarg_0 || code[start + 4].opcode != OpCodes.Ldfld ||
                    !(code[start + 4].operand is FieldInfo radius) || radius.FieldType != typeof(float) ||
                    code[start + 5].opcode != OpCodes.Sub)
                    throw new InvalidOperationException("Unrecognized native area radius");
                replacement.Add(new CodeInstruction(OpCodes.Ldarg_0));
                replacement.Add(new CodeInstruction(OpCodes.Ldfld, radius));
            }
            replacement.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(InteractionRayPatch),
                area ? nameof(OverrideAreaRay) : nameof(OverrideRay))));
            replacement.Add(new CodeInstruction(OpCodes.Stloc, length));
            if (area)
            {
                var casts = code.Select((instruction, index) => new { instruction, index }).Where(x =>
                    x.instruction.operand is MethodInfo m && m.DeclaringType == typeof(Sandbox.Engine.Physics.MyPhysics) &&
                    m.Name == "CastShapeReturnContactBodyDatas" && m.ReturnType == typeof(bool)).ToArray();
                if (casts.Length != 1 || casts[0].index <= start || casts[0].index + 2 >= code.Count ||
                    code[casts[0].index + 1].opcode != OpCodes.Pop)
                    throw new InvalidOperationException("Unrecognized native area cast");
                var afterCast = generator.DefineLabel();
                code[casts[0].index + 2].labels.Add(afterCast);
                replacement.Add(new CodeInstruction(OpCodes.Ldloc, length));
                replacement.Add(new CodeInstruction(OpCodes.Ldc_R4, 0f));
                replacement.Add(new CodeInstruction(OpCodes.Blt, afterCast));
            }
            // Both head/camera branches must enter the override, including a branch
            // aimed directly at the endpoint expression in optimized game builds.
            replacement[0].MoveLabelsFrom(code[start]);
            replacement[0].MoveBlocksFrom(code[start]);
            var nativeLength = code[start + 2];
            code[start + 2] = new CodeInstruction(OpCodes.Ldloc, length).MoveLabelsFrom(nativeLength).MoveBlocksFrom(nativeLength);
            code.InsertRange(start, replacement);
            return code;
        }

        private static bool TryHandRay(MyCharacterDetectorComponent detector, out LineD ray)
        {
            ray = default(LineD);
            var character = MySession.Static?.LocalCharacter;
            return character != null && detector.Character == character && HandInteraction.TryDetectorRay(out ray);
        }

        private static float OverrideRay(MyCharacterDetectorComponent detector, ref Vector3D origin, ref Vector3D direction)
        {
            if (!TryHandRay(detector, out var ray)) return MyConstants.DEFAULT_INTERACTIVE_DISTANCE;
            origin = ray.From;
            direction = ray.Direction;
            return (float)ray.Length;
        }

        private static float OverrideAreaRay(MyCharacterDetectorComponent detector, ref Vector3D origin, ref Vector3 direction, float radius)
        {
            float reach = MyConstants.DEFAULT_INTERACTIVE_DISTANCE;
            if (!TryHandRay(detector, out var ray)) return reach;
            origin = ray.From;
            direction = (Vector3)ray.Direction;
            var head = detector.Character.GetHeadMatrix(true, true, forceHeadBone: true).Translation;
            // The entire swept sphere must fit. A negative length skips to the native empty-hit check.
            double travel = radius < reach ? Multiplayer.HeldItemPose.ReachAlong(head, origin, ray.Direction, reach - radius) : 0;
            return travel > 0 ? (float)(travel + radius) : -1f;
        }
    }

    [HarmonyPatchCategory("HandInteraction")]
    [HarmonyPatch(typeof(MyCharacterRaycastDetectorComponent), "ProcessUseObjects")]
    internal static class InteractionHitDistancePatch
    {
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var code = instructions.ToList();
            var distance = AccessTools.Field(typeof(MyConstants), nameof(MyConstants.DEFAULT_INTERACTIVE_DISTANCE));
            var args = __originalMethod.GetParameters();
            if (args.Length < 4 || args[2].Name != "from" || args[3].Name != "to" ||
                args[2].ParameterType != typeof(Vector3D).MakeByRefType() || args[3].ParameterType != args[2].ParameterType)
                throw new InvalidOperationException("Unrecognized native use-object segment");
            int index = code.FindIndex(i => i.LoadsField(distance));
            if (index < 0 || code.FindLastIndex(i => i.LoadsField(distance)) != index ||
                index + 1 >= code.Count || code[index + 1].opcode != OpCodes.Mul)
                throw new InvalidOperationException("Unrecognized native use-object distance");
            // RaycastDetectors returns a fraction of the segment. Native occlusion compares metres.
            var loadDetector = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(code[index]).MoveBlocksFrom(code[index]);
            code.RemoveAt(index);
            code.InsertRange(index, new[] { loadDetector, new CodeInstruction(OpCodes.Ldarg_3),
                CodeInstruction.LoadArgument(4), new CodeInstruction(OpCodes.Call,
                    AccessTools.Method(typeof(InteractionHitDistancePatch), nameof(SegmentLength))) });
            return code;
        }

        private static float SegmentLength(MyCharacterRaycastDetectorComponent detector, in Vector3D from, in Vector3D to)
        {
            var character = MySession.Static?.LocalCharacter;
            return character != null && detector.Character == character && Plugin.Main.VrActive
                ? (float)Vector3D.Distance(from, to) : MyConstants.DEFAULT_INTERACTIVE_DISTANCE;
        }
    }
}
