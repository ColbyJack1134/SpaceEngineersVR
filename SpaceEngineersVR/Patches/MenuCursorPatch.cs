using HarmonyLib;
using Sandbox.Engine.Utils;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyDX9Gui),"Draw")]
    internal static class MenuCursorPatch
    {
        [HarmonyPrefix]
        private static void Prefix(out bool __state)
        {
            __state=MyFakes.FORCE_SOFTWARE_MOUSE_DRAW;
            // Use the engine's own cursor texture, hotspot, hover state and UI scaling.
            // The hardware cursor otherwise never reaches the texture shown in VR.
            if(Main.VrActive && Main.MenuOpen) MyFakes.FORCE_SOFTWARE_MOUSE_DRAW=true;
        }
        [HarmonyFinalizer]
        private static void Restore(bool __state) { MyFakes.FORCE_SOFTWARE_MOUSE_DRAW=__state; }
    }
}
