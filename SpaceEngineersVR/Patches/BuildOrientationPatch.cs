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
        private static void Prefix(ref bool draw,out BuildOrientationHud.Capture __state)
        {
            __state=null;
            if(!Main.VrActive || BuildOrientationHud.Capturing!=null) return;
            // Native callers draw hints only in HudState 1; captured hints never reach the desktop.
            if(!draw && Sandbox.Game.Gui.MyHud.HudState!=1 && !HelmetHud.NativeSuppressed && Sandbox.MySandboxGame.Config.RotationHints && !VRage.Input.MyInput.Static.IsJoystickLastUsed) draw=true;
            __state=BuildOrientationHud.Begin(draw);
        }
        private static void Postfix(BuildOrientationHud.Capture __state) => __state?.Complete();
        private static Exception Finalizer(Exception __exception,BuildOrientationHud.Capture __state)
        { __state?.Dispose(); return __exception; }
    }
    [HarmonyPatch]
    internal static class PlanetRotationHintPatch
    {
        private static readonly System.Reflection.MethodInfo orientation=AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Entities.MyVoxelClipboard"),"GetFirstOrientationMatrix");
        private static System.Reflection.MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Entities.MyVoxelClipboard"),"CalculateRotationHints");
        private static bool Prefix(object __instance,MyBlockBuilderRotationHints hints,bool isRotating,bool ___m_planetMode,VRageMath.Vector3D ___m_pastePosition)
        {
            if(!Main.VrActive || !___m_planetMode) return true;
            // Planets have no preview entity to supply the ordinary native rotation hint.
            Calculate(__instance,hints,___m_pastePosition,!HelmetHud.NativeSuppressed &&
                Sandbox.MySandboxGame.Config.RotationHints && !VRage.Input.MyInput.Static.IsJoystickLastUsed,isRotating);
            return false;
        }
        internal static void Calculate(object clipboard,MyBlockBuilderRotationHints hints,VRageMath.Vector3D position,bool draw,bool rotating)
        {
            var matrix=(VRageMath.MatrixD)orientation.Invoke(clipboard,null);
            matrix.Translation=position;
            hints.CalculateRotationHints(matrix,draw,rotating);
        }
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
