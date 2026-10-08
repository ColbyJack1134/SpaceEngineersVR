using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class StereoParticleUpdatePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyGPUParticleRenderer"),"Update");
        private static readonly FieldInfo count=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyGPUParticleRenderer"),"m_emitterCount");
        private static bool Prefix(ref int __result)
        {
            if(StereoParticles.Advance) return true;
            __result=(int)count.GetValue(null); return false;
        }
    }
    [HarmonyPatch]
    internal static class StereoParticleRunPatch
    {
        private static readonly Type native=AccessTools.TypeByName("VRageRender.MyGPUParticleRenderer");
        private static MethodBase TargetMethod() => AccessTools.Method(native,"Run");
        private static readonly FieldInfo reset=AccessTools.Field(native,"m_resetSystem");
        private static bool Prefix() => StereoParticles.Advance || !(bool)reset.GetValue(null);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
        {
            var code=new List<CodeInstruction>(instructions);
            var alive=AccessTools.Field(native,"m_aliveIndexBuffer1");
            var simulate=AccessTools.Method(native,"Simulate");
            var frame=AccessTools.PropertyGetter(AccessTools.TypeByName("VRageRender.MyCommon"),"FrameConstants");
            int begin=code.FindIndex(i=>i.opcode==OpCodes.Ldsfld && Equals(i.operand,alive));
            int end=code.FindIndex(i=>i.Calls(simulate))+1;
            if(begin<0 || end<=begin) throw new InvalidOperationException("Particle simulation layout changed");
            var run=generator.DefineLabel(); var rendered=generator.DefineLabel();
            var gate=new CodeInstruction(OpCodes.Call,AccessTools.PropertyGetter(typeof(StereoParticles),nameof(StereoParticles.Advance)));
            gate.MoveLabelsFrom(code[begin]);
            code[begin].labels.Add(run); code[end].labels.Add(rendered);
            int frames=0;
            for(int index=0;index<code.Count;index++)
            {
                if(index==begin)
                {
                    yield return gate;
                    yield return new CodeInstruction(OpCodes.Brtrue,run);
                    yield return new CodeInstruction(OpCodes.Br,rendered);
                }
                var instruction=code[index]; yield return instruction;
                if(instruction.Calls(frame))
                {
                    frames++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(StereoParticles),nameof(StereoParticles.FrameConstants)));
                    yield return new CodeInstruction(OpCodes.Castclass,frame.ReturnType);
                }
            }
            if(frames!=1) throw new InvalidOperationException("Particle frame constants layout changed");
        }
    }
    [HarmonyPatch]
    internal static class StereoParticleVertexPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyGPUParticleRenderer"),"Render");
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int found=0;
            foreach(var instruction in instructions)
            {
                if(instruction.operand is MethodInfo method && method.DeclaringType.Name=="MyVertexStage" && method.Name=="Set")
                {
                    found++;
                    var arg=new CodeInstruction(OpCodes.Ldarg_0); arg.MoveLabelsFrom(instruction); yield return arg;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(StereoParticles),nameof(StereoParticles.Select)));
                }
                yield return instruction;
            }
            if(found!=1) throw new InvalidOperationException("Particle vertex shader binding layout changed");
        }
    }
    [HarmonyPatch]
    internal static class StereoParticleResetPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type=AccessTools.TypeByName("VRageRender.MyGPUParticleRenderer");
            foreach(string name in new[] {"Init","OnSessionEnd","OnDeviceReset"}) yield return AccessTools.Method(type,name);
        }
        private static void Prefix() => StereoParticles.ResetCamera();
    }
}
