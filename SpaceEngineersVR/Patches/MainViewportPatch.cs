using System.Reflection;
using System.Threading;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    // Eye passes swap ViewportResolution on the render thread; other threads (EmptyKeys hit tests) need the desktop size.
    [HarmonyPatch]
    internal static class MainViewportPatch
    {
        private static MethodBase TargetMethod() => AccessTools.PropertyGetter(AccessTools.TypeByName("VRageRender.MyDX11Render"),"MainViewport");
        private static Vector2I? Desktop() => EyeResolution.Scene.DesktopFor(Thread.CurrentThread,out var size) ? size : (Vector2I?)null;
        // The prefix covers a scene that ends between the original read and the postfix.
        [HarmonyPrefix]
        private static void Prefix(out Vector2I? __state) { __state=Desktop(); }
        [HarmonyPostfix]
        private static void Postfix(ref MyViewport __result,Vector2I? __state)
        {
            var desktop=Desktop() ?? __state;
            if(desktop.HasValue) __result=new MyViewport(desktop.Value);
        }
    }
}
