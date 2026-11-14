using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatchCategory("BuildHints")]
    [HarmonyPatch(typeof(MyCubeBuilder),nameof(MyCubeBuilder.Draw))]
    internal static class SymmetryFramePatch
    {
        private static void Prefix() => SymmetryPlanes.Begin();
        private static void Postfix() => SymmetryPlanes.Commit();
    }
    [HarmonyPatchCategory("BuildHints")]
    [HarmonyPatch(typeof(MyCubeBuilder),"DrawSymmetryPlane")]
    internal static class SymmetryPlanePatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var native=AccessTools.Method(typeof(MyRenderProxy),nameof(MyRenderProxy.DebugDrawTriangle));
            var capture=AccessTools.Method(typeof(SymmetryPlanePatch),nameof(CaptureTriangle));
            int replaced=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(native))
                {
                    instruction.opcode=OpCodes.Call;
                    instruction.operand=capture;
                    replaced++;
                }
                yield return instruction;
            }
            if(replaced!=2) throw new InvalidOperationException("Expected two native symmetry triangle calls, found "+replaced);
        }
        private static void Prefix(out bool __state)
        {
            __state=SymmetryPlanes.Capturing;
            SymmetryPlanes.Capturing=__state || Main.VrActive;
        }
        private static void Finalizer(bool __state) => SymmetryPlanes.Capturing=__state;
        private static void CaptureTriangle(Vector3D vertex0,Vector3D vertex1,Vector3D vertex2,Color color,bool smooth,bool depthRead,bool persistent)
        {
            if(SymmetryPlanes.Capturing) SymmetryPlanes.Add(new SymmetryPlanes.View(vertex0,vertex1,vertex2,color));
            MyRenderProxy.DebugDrawTriangle(vertex0,vertex1,vertex2,color,smooth,depthRead,persistent);
        }
    }
}
