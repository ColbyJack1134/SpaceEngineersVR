using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class ParticleDensityPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyGPUEmitter"),"Update");
        private static float Scale(float native) => Main.VrActive || StereoRenderState.Active ? native*(Common.Config?.ParticleDensity ?? .5f) : native;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var multiplier=AccessTools.PropertyGetter(AccessTools.TypeByName("VRageRender.MyGPUEmitters"),"ParticleCountMultiplier");
            int found=0;
            foreach(var instruction in instructions)
            {
                yield return instruction;
                if(instruction.Calls(multiplier))
                {
                    found++;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(ParticleDensityPatch),nameof(Scale)));
                }
            }
            if(found!=1) throw new InvalidOperationException("Particle emission multiplier layout changed");
        }
    }
}
