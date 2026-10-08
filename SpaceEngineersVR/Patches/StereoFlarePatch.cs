using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class StereoFlareQueries
    {
        private static readonly Type data=AccessTools.TypeByName("VRage.Render11.Culling.Occlusion.MyFlareOcclusionData");
        private static readonly FieldInfo area=AccessTools.Field(data,"LastVolumeSquared"),position=AccessTools.Field(data,"Position"),
            size=AccessTools.Field(data,"Size"),shift=AccessTools.Field(data,"Shift");
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Culling.Occlusion.MyFlareOcclusionRenderer"),"Render");
        // One visibility history is intentionally shared by the eyes. Only its
        // source eye may submit/read queries; the desktop must not advance it.
        private static bool Prefix() => StereoRenderState.AdvanceScene;
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int found=0;
            foreach(var instruction in instructions)
            {
                if(instruction.opcode==OpCodes.Stfld && Equals(instruction.operand,area))
                {
                    found++; instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(StereoFlareQueries),nameof(SetArea));
                }
                yield return instruction;
            }
            if(found!=1) throw new InvalidOperationException("Flare query area layout changed");
        }
        private static void SetArea(object query,float nativeArea)
        {
            float result=nativeArea;
            if(StereoRenderState.Active)
            {
                var camera=MyRender11.Environment_Matrices;
                result=StereoRenderState.QueryArea((Vector3D)position.GetValue(query)-camera.CameraPosition,
                    (float)size.GetValue(query),(float)shift.GetValue(query),camera.ViewAt0,camera.Projection,MyRender11.Resolution);
                StereoRenderState.Record("flare_area",nativeArea,result);
            }
            area.SetValue(query,result);
        }
    }

    [HarmonyPatch]
    internal static class StereoFlareVisibility
    {
        private sealed class Sample { public long Frame=-1; public float Factor; }
        private static readonly ConditionalWeakTable<object,Sample> samples=new ConditionalWeakTable<object,Sample>();
        private static readonly FieldInfo factor=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Culling.Occlusion.MyFlareOcclusionData"),"OcclusionFactor");
        private static MethodBase TargetMethod() => AccessTools.TypeByName("VRageRender.MyFlareRenderer")
            .GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="Draw" && m.GetParameters().Length==5);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int found=0;
            foreach(var instruction in instructions)
            {
                if(instruction.opcode==OpCodes.Ldfld && Equals(instruction.operand,factor))
                {
                    found++; instruction.opcode=OpCodes.Call;
                    instruction.operand=AccessTools.Method(typeof(StereoFlareVisibility),nameof(Read));
                }
                yield return instruction;
            }
            if(found!=1) throw new InvalidOperationException("Flare visibility layout changed");
        }
        private static float Read(object query)
        {
            if(!StereoRenderState.Active) return (float)factor.GetValue(query);
            var sample=samples.GetValue(query,_=>new Sample());
            lock(sample)
            {
                if(sample.Frame!=StereoRenderState.Frame)
                { sample.Frame=StereoRenderState.Frame; sample.Factor=(float)factor.GetValue(query); }
                return sample.Factor;
            }
        }
    }

    [HarmonyPatch]
    internal static class StereoFlareGeometry
    {
        private static MethodBase TargetMethod() => AccessTools.TypeByName("VRageRender.MyFlareRenderer")
            .GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="Draw" && m.GetParameters().Length==5);
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator)
        {
            var found=new HashSet<string>();
            var head=generator.DeclareLocal(typeof(Matrix));
            foreach(var instruction in instructions)
            {
                if((instruction.opcode==OpCodes.Ldfld || instruction.opcode==OpCodes.Ldflda) && instruction.operand is FieldInfo field && field.DeclaringType.Name=="MyEnvironmentMatrices")
                {
                    string name=field.Name=="CameraPosition" ? nameof(Camera) : field.Name=="InvViewAt0" ? nameof(Head) :
                        field.Name=="ViewProjectionAt0" ? nameof(ViewProjection) : field.Name=="InvViewProjectionAt0" ? nameof(InverseProjection) : null;
                    if(name!=null)
                    {
                        bool address=instruction.opcode==OpCodes.Ldflda;
                        found.Add(name); instruction.opcode=OpCodes.Call;
                        instruction.operand=AccessTools.Method(typeof(StereoFlareGeometry),name);
                        if(address)
                        {
                            if(field.FieldType!=typeof(Matrix)) throw new InvalidOperationException("Flare camera address type changed");
                            yield return instruction;
                            yield return new CodeInstruction(OpCodes.Stloc,head);
                            yield return new CodeInstruction(OpCodes.Ldloca,head);
                            continue;
                        }
                    }
                }
                yield return instruction;
            }
            if(found.Count!=4) throw new InvalidOperationException("Flare geometry camera layout changed");
        }
        private static Vector3D Camera(object matrices) => StereoRenderState.PhysicalEye ? MatrixD.Invert(StereoRenderState.CenterView).Translation :
            (Vector3D)CockpitRender.Member(matrices,"CameraPosition");
        private static Matrix Head(object matrices)
        {
            if(!StereoRenderState.PhysicalEye) return (Matrix)CockpitRender.Member(matrices,"InvViewAt0");
            var head=MatrixD.Invert(StereoRenderState.CenterView); head.Translation=Vector3D.Zero; return (Matrix)head;
        }
        private static Matrix ViewProjection(object matrices)
        {
            if(!StereoRenderState.PhysicalEye) return (Matrix)CockpitRender.Member(matrices,"ViewProjectionAt0");
            var view=StereoRenderState.CenterView; view.Translation=Vector3D.Zero;
            return (Matrix)view*StereoRenderState.CenterProjection;
        }
        private static Matrix InverseProjection(object matrices) => StereoRenderState.PhysicalEye ? Matrix.Invert(ViewProjection(matrices)) :
            (Matrix)CockpitRender.Member(matrices,"InvViewProjectionAt0");
    }
}
