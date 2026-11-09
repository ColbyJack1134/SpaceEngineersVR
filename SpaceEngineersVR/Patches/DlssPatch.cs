#if !LINUX
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    // The FXAA slot after tone mapping takes the evaluate; its target borrow is where the resolution switches to the eye target size, restored after the scene.
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
        private static void Prefix() => Dlss.UnjitterConstants();
    }
}
#endif
