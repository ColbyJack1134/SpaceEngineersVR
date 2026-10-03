using System;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class HiddenAreaMarkPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Clear");
        private static void Postfix(object __instance)
        {
            int eye=HiddenAreaMask.Eye;
            if(eye>=0) HiddenAreaMask.Mark(__instance,eye);
        }
    }

    // Occlusion queries consume first after all geometry; lighting follows.
    [HarmonyPatch]
    internal static class HiddenAreaRestorePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Culling.Occlusion.MyOcclusionTask"),"Consume");
        private static void Prefix() => HiddenAreaMask.Restore();
    }

    [HarmonyPatch]
    internal static class HiddenAreaExposurePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyEyeAdaptation"),"Run");
        private static void Prefix() => HiddenAreaMask.BeginExposure();
        private static Exception Finalizer(Exception __exception) { HiddenAreaMask.EndExposure(); return __exception; }
    }

    [HarmonyPatch]
    internal static class HiddenAreaDispatchPatch
    {
        private static readonly Type contextType=AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext");
        private static readonly FieldInfo deviceContext=AccessTools.Field(contextType,"m_deviceContext");
        private static MethodBase TargetMethod() => AccessTools.Method(contextType,"Dispatch",new[] { typeof(int),typeof(int),typeof(int) });
        private static bool Prefix(object __instance,int threadGroupCountX,int threadGroupCountY,int threadGroupCountZ) =>
            !HiddenAreaMask.ExposurePending ||
            !HiddenAreaMask.Dispatch((DeviceContext)deviceContext.GetValue(__instance),threadGroupCountX,threadGroupCountY,threadGroupCountZ);
    }
}
