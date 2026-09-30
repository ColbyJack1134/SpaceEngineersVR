using System;
using HarmonyLib;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyBlockBuilderRotationHints),nameof(MyBlockBuilderRotationHints.CalculateRotationHints))]
    internal static class BuildOrientationPatch
    {
        private static void Prefix(bool draw,out BuildOrientationHud.Capture __state)
        { __state=Main.VrActive && BuildOrientationHud.Capturing==null ? BuildOrientationHud.Begin(draw) : null; }
        private static void Postfix(BuildOrientationHud.Capture __state) => __state?.Complete();
        private static Exception Finalizer(Exception __exception,BuildOrientationHud.Capture __state)
        { __state?.Dispose(); return __exception; }
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.AddBillboard))]
    internal static class BuildHintBillboardPatch
    {
        private static bool Prefix(MyBillboard billboard)
        {
            var capture=BuildOrientationHud.Capturing;
            if(capture==null) return true;
            capture.Add(billboard); return false;
        }
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.AddBillboardViewProjection))]
    internal static class BuildHintProjectionPatch
    {
        // Keep the native entry: CreateBillboard reads its camera position.
        private static void Prefix(MyBillboardViewProjection billboardViewProjection) => BuildOrientationHud.Capturing?.Projection(billboardViewProjection);
    }
    [HarmonyPatch(typeof(MyBlockBuilderRotationHints),"ShouldDrawText")]
    internal static class BuildHintTextPatch
    {
        private static bool Prefix(ref bool __result)
        { if(BuildOrientationHud.Capturing==null) return true; __result=false; return false; }
    }
}
