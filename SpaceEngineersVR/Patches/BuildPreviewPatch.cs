using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class BuildPreviewPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MyCubeBuilder),"UpdateGizmo_Grid");
            yield return AccessTools.Method(typeof(MyCubeBuilder),"UpdateGizmo_VoxelMap");
        }
        internal static bool ContinuePreview(MyCubeBuilderGizmo.MyGizmoSpaceProperties space)
        {
            if(!PlacementControls.TryBuilderPose(out _)) return false;
            space.m_showGizmoCube=true;
            return true;
        }
        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator,MethodBase original)
        {
            int reach=-1,count=0;
            FieldInfo lastStore=null;
            var check=AccessTools.Method(typeof(MyCubeBuilderGizmo),nameof(MyCubeBuilderGizmo.DefaultGizmoCloseEnough));
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(check)) reach=0;
                else if(reach>=0 && ++reach>20) reach=-1;
                if(instruction.opcode==OpCodes.Stfld) lastStore=instruction.operand as FieldInfo;
                if(reach>=0 && instruction.opcode==OpCodes.Ret && lastStore?.Name=="m_removeBlock")
                {
                    reach=-1; count++;
                    var resume=generator.DefineLabel();
                    var gate=new CodeInstruction(OpCodes.Ldarg_1);
                    gate.MoveLabelsFrom(instruction); gate.MoveBlocksFrom(instruction);
                    yield return gate;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(BuildPreviewPatch),nameof(ContinuePreview)));
                    yield return new CodeInstruction(OpCodes.Brtrue,resume);
                    yield return instruction;
                    var next=new CodeInstruction(OpCodes.Nop); next.labels.Add(resume); yield return next;
                }
                else yield return instruction;
            }
            // Native range failures already disable placement; keep drawing their red preview in VR.
            if(count!=(original.Name=="UpdateGizmo_Grid" ? 2:1)) throw new InvalidOperationException("Native out-of-range preview exits changed: "+count);
        }
    }
}
