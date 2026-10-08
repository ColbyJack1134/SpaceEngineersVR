using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class StereoExposurePatch
    {
        private static readonly Type adaptation = AccessTools.TypeByName("VRageRender.MyEyeAdaptation");
        private static MethodBase TargetMethod() => AccessTools.Method(adaptation, "Run");
        private static bool Prefix() => !StereoExposure.DiagnosticDesktop;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            int found = 0;
            foreach (var instruction in instructions)
            {
                yield return instruction;
                if (!(instruction.operand is MethodInfo method) || method.Name != "Release" ||
                    method.GetParameters().Length != 0 || method.ReturnType != typeof(void)) continue;
                found++;
                var proceed = generator.DefineLabel();
                yield return new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(adaptation, "m_histogram"));
                yield return CodeInstruction.Call(typeof(StereoExposure), nameof(StereoExposure.Collect));
                yield return new CodeInstruction(OpCodes.Brfalse, proceed);
                yield return new CodeInstruction(OpCodes.Ret);
                var next = new CodeInstruction(OpCodes.Nop);
                next.labels.Add(proceed);
                yield return next;
            }
            if (found != 1) throw new InvalidOperationException("Eye adaptation histogram boundary changed");
        }
    }

    [HarmonyPatch]
    internal static class StereoExposureReadPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyEyeAdaptation"), "GetExposure");
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            foreach (var instruction in instructions)
            {
                if (instruction.opcode == OpCodes.Ret)
                {
                    var select = CodeInstruction.Call(typeof(StereoExposure), nameof(StereoExposure.Select));
                    select.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    yield return select;
                    yield return new CodeInstruction(OpCodes.Castclass, ((MethodInfo)__originalMethod).ReturnType);
                }
                yield return instruction;
            }
        }
    }
}
