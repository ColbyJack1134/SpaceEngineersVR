using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    // While active, the game confines the cursor to its window on every render frame. In VR that keeps the
    // mouse and SteamVR's desktop pointer from reaching other monitors.
    [HarmonyPatch]
    internal static class CursorClipPatch
    {
        [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(
            Type.GetType("VRage.Platform.Windows.Forms.MyGameWindow, VRage.Platform.Windows",true),"UpdateClip");
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if(!Main.VrActive) return true;
            ClipCursor(IntPtr.Zero);
            return false;
        }
    }
}
