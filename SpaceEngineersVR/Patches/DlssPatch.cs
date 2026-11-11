#if !LINUX
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    // DLSS evaluates in the FXAA slot; borrowing the FXAA target switches the eye to the output size until the scene ends.
    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssFxaaGatePatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(AccessTools.TypeByName("VRageRender.MyRender11"),"FxaaEnabled");
        private static bool Prefix(ref bool __result)
        {
            if(!Dlss.Live) return true;
            __result=true;
            return false;
        }
    }

    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssFxaaPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyFXAA"),"Run");
        private static bool Prefix(object rc,object destination,object source) => !Dlss.Replace(rc,destination,source);
    }

    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssBorrowPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Resources.MyBorrowedRwTextureManager"),"BorrowCustom",new[] {typeof(string),typeof(int),typeof(int)});
        private static void Prefix(string debugName) => Dlss.BeforeBorrow(debugName);
    }

    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssScenePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"DrawGameScene");
        private static void Postfix() => Dlss.AfterScene();
    }

    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssPostPpPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyBillboardRenderer"),"RenderPostPP");
        private static void Prefix(object[] __args) => Dlss.BeforePostPp(__args);
        private static void Finalizer() => Dlss.RestoreDepth();
    }

    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssObjectsPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Render.MyRenderScheduler"),"Done");
        private static void Prefix() => Dlss.CollectObjects();
    }

    // Previews are screen-door dithered; DLAA and DLSS keep the holes sharp and smear them while the preview moves.
    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssSolidPreviewPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Entities.Cube.MyBlockBuilderRenderData"),"EndCollectingInstanceData");
        private static void Prefix(ref bool useTransparency) { if(Dlss.SolidPreviews) useTransparency=false; }
    }

    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssSolidPastePatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(AccessTools.TypeByName("Sandbox.Game.Entities.Cube.MyGridClipboard"),"Transparency");
        private static bool Prefix(ref float __result)
        {
            if(!Dlss.SolidPreviews) return true;
            __result=0;
            return false;
        }
    }

    // Buildable blocks drop the hologram; the rest keep it at the smallest transparency (see Dlss.SetSolidDither).
    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch]
    internal static class DlssSolidProjectionPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Entities.Blocks.MyProjectorBase"),"SetTransparency");
        private static void Prefix(ref float transparency)
        {
            if(Dlss.SolidPreviews && transparency>0 && transparency<1) transparency=transparency<=.25f ? 0 : .001f;
        }
    }

    // Build Info draws rotor, hinge, piston and wheel top parts as its own preview entities.
    [HarmonyPatchCategory("Dlss")]
    [HarmonyPatch(typeof(VRage.Game.Components.MyRenderComponentBase),nameof(VRage.Game.Components.MyRenderComponentBase.UpdateTransparency))]
    internal static class DlssSolidPreviewEntityPatch
    {
        private static void Prefix(VRage.Game.Components.MyRenderComponentBase __instance)
        {
            if(Dlss.SolidPreviews && __instance.Transparency>0 && __instance.Transparency<1 && __instance.Container?.Entity is VRage.Game.Entity.MyEntity entity && entity.IsPreview)
                __instance.Transparency=0;
        }
    }
}
#endif
