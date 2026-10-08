using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class StereoProbePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var type=AccessTools.TypeByName("VRage.Render11.LightingStage.EnvironmentProbe.MyEnvironmentProbe");
            foreach(string name in new[] { "UpdateProbe","UpdateCullQuery","FinalizeEnvProbes" })
                yield return AccessTools.Method(type,name);
        }
        private static bool Prefix() => StereoRenderState.AdvanceScene;
    }

    [HarmonyPatch]
    internal static class StereoAmbientOcclusionPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyHBAO"),"InitConstantBuffer");
        private static readonly FieldInfo offset=AccessTools.Field(((MethodInfo)TargetMethod()).ReturnType,"UVToViewB");
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var type=((MethodInfo)TargetMethod()).ReturnType;
            foreach(var instruction in instructions)
            {
                if(instruction.opcode==OpCodes.Ret)
                {
                    var box=new CodeInstruction(OpCodes.Box,type); box.MoveLabelsFrom(instruction);
                    yield return box;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(StereoAmbientOcclusionPatch),nameof(Adjust)));
                    yield return new CodeInstruction(OpCodes.Unbox_Any,type);
                }
                yield return instruction;
            }
        }
        private static object Adjust(object constants)
        {
            offset.SetValue(constants,ViewOffset(MyRender11.Environment_Matrices.Projection));
            return constants;
        }
        internal static Vector2 ViewOffset(Matrix projection) => new Vector2(
            (projection.M31-1)/Math.Abs(projection.M11),(projection.M32+1)/Math.Abs(projection.M22));
    }

    [HarmonyPatch]
    internal static class StereoBillboardPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyBillboardRenderer"),"GatherInternal");
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            int billboard=__originalMethod.GetMethodBody().LocalVariables.Single(v=>v.LocalType==typeof(VRageRender.MyBillboard)).LocalIndex;
            int found=0;
            foreach(var instruction in instructions)
            {
                if(instruction.operand is MethodInfo method && method.DeclaringType==typeof(VRage.Utils.MyUtils) &&
                    (method.Name=="GetBillboardQuadAdvancedRotated" || method.Name=="GetPolyLineQuad"))
                {
                    found++;
                    var load=CodeInstruction.LoadLocal(billboard); load.MoveLabelsFrom(instruction); yield return load;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(StereoBillboardPatch),nameof(Camera)));
                }
                else if(instruction.operand is MethodInfo normalize && normalize.DeclaringType==typeof(VRage.Utils.MyUtils) &&
                    normalize.Name=="Normalize" && normalize.GetParameters().Length==1 && normalize.GetParameters()[0].ParameterType==typeof(Vector3D))
                {
                    found++;
                    var load=CodeInstruction.LoadLocal(billboard); load.MoveLabelsFrom(instruction); yield return load;
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(StereoBillboardPatch),nameof(Direction)));
                }
                yield return instruction;
            }
            if(found!=3) throw new InvalidOperationException("Native billboard facing layout changed");
        }
        private static Vector3D Direction(Vector3D direction,VRageRender.MyBillboard billboard)
        {
            var eye=MyRender11.Environment_Matrices.CameraPosition;
            return direction+(Camera(eye,billboard)-eye);
        }
        private static Vector3D Camera(Vector3D eye,VRageRender.MyBillboard billboard) => StereoRenderState.PhysicalEye && billboard.CustomViewProjection==-1 ?
            MatrixD.Invert(StereoRenderState.CenterView).Translation : eye;
    }

    [HarmonyPatch]
    internal static class SceneFlarePoolPatch
    {
        private static readonly FieldInfo pool=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyBillboardRenderer"),"m_billboardsOncePool");
        private static readonly FieldInfo count=AccessTools.Field(pool.FieldType,"m_nextAllocateIndex");
        private static readonly MethodInfo clear=AccessTools.Method(pool.FieldType,"ClearAllAllocated");
        private static int start=-1;
        internal static bool Scoped { get; private set; }
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyTransparentRendering"),"Render");
        private static void Prefix() { start=-1; Scoped=StereoRenderState.Active || RemoteScene.Active; }
        internal static void Capture()
        {
            if(Scoped && start<0) start=(int)count.GetValue(pool.GetValue(null));
        }
        private static Exception Finalizer(Exception __exception)
        {
            // Flare quads belong to this scene, unlike simulation-generated billboards.
            if(start>=0)
            {
                var value=pool.GetValue(null);
                clear.Invoke(value,null);
                count.SetValue(value,start);
            }
            start=-1; Scoped=false;
            return __exception;
        }
    }
    [HarmonyPatch]
    internal static class SceneFlareDrawPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyFlareRenderer"),"Draw",
            new[] {typeof(IEnumerable<>).MakeGenericType(AccessTools.TypeByName("VRage.Render11.Scene.Components.MyLightComponent")),typeof(float)});
        private static void Prefix() => SceneFlarePoolPatch.Capture();
    }
}
