#if !LINUX
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    // The game clips the cursor to its window every render frame, which keeps the mouse and SteamVR's desktop pointer off other monitors.
    // .NET 10 can inline UpdateClip before Harmony attaches, so Preloader also gates it on FreeField; the prefix covers launches without the preloader.
    [HarmonyPatch]
    internal static class CursorClipPatch
    {
        internal const string FreeField="SpaceEngineersVRFreeCursor";
        private static readonly Type window=Type.GetType("VRage.Platform.Windows.Forms.MyGameWindow, VRage.Platform.Windows",true);
        private static readonly FieldInfo preloaded=window.GetField(FreeField,BindingFlags.Public|BindingFlags.Static);
        private static bool logged;

        private static bool Free => Main.VrActive && !(Common.Config.EnableKeyboardAndMouseControls && Common.Config.LockMouseToWindow);

        internal static void Update()
        {
            if(!logged) { logged=true; Logger.Info("Cursor clip gate: "+(preloaded!=null ? "preloaded" : "Harmony prefix only")); }
            preloaded?.SetValue(null,Free);
        }

        [DllImport("user32.dll")] private static extern bool ClipCursor(IntPtr rect);
        private static MethodBase TargetMethod() => AccessTools.DeclaredMethod(window,"UpdateClip");
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if(!Free) return true;
            ClipCursor(IntPtr.Zero);
            return false;
        }
    }
}
#endif
