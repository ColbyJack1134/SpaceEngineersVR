using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatchCategory("SharedExposure")]
    [HarmonyPatch]
    internal static class StereoExposurePatch
    {
        private static readonly Type adaptation = AccessTools.TypeByName("VRageRender.MyEyeAdaptation");
        private static MethodBase TargetMethod() => AccessTools.Method(adaptation, "Run");
        private static bool Prefix() => !StereoExposure.DiagnosticDesktop;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var pixelStage = AccessTools.PropertyGetter(AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext"), "PixelShader");
            var setConstantBuffer = AccessTools.Method(AccessTools.TypeByName("VRage.Render11.RenderContext.MyPixelStage"), "SetConstantBuffer");
            var frameConstants = AccessTools.PropertyGetter(AccessTools.TypeByName("VRageRender.MyCommon"), "FrameConstants");
            if (pixelStage == null || setConstantBuffer == null || frameConstants == null)
                throw new InvalidOperationException("Eye adaptation frame constant binding changed");
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
                // The skipped exposure pass binds pixel b0; without bloom nothing rebinds it before FXAA and the billboards.
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Callvirt, pixelStage);
                yield return new CodeInstruction(OpCodes.Ldc_I4_0);
                yield return new CodeInstruction(OpCodes.Call, frameConstants);
                yield return new CodeInstruction(OpCodes.Callvirt, setConstantBuffer);
                yield return new CodeInstruction(OpCodes.Ret);
                var next = new CodeInstruction(OpCodes.Nop);
                next.labels.Add(proceed);
                yield return next;
            }
            if (found != 1) throw new InvalidOperationException("Eye adaptation histogram boundary changed");
        }
    }

    [HarmonyPatchCategory("SharedExposure")]
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
