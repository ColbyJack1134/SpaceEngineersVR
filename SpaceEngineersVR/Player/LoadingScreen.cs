using HarmonyLib;
using Sandbox.Game.Gui;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    internal static class LoadingScreen
    {
        internal static volatile bool Loading;
        private static System.IntPtr warmed;

        // Render thread. NGX start-up and runtime shader compiles would otherwise block the first world frame for seconds.
        internal static void Warm()
        {
            if(!Loading) return;
            Dlss.Prepare();
            var device=Wrappers.MyRender11.DeviceInstance;
            if(device==null || device.NativePointer==warmed) return;
            warmed=device.NativePointer;
            HiddenAreaMask.Prepare();
            // Both retry and report on first use.
            try { if(PatchGroups.SharedExposure.Enabled) StereoExposure.Prepare(device); } catch { }
            try { if(PatchGroups.StereoParticles.Enabled) StereoParticles.Ensure(device); } catch { }
        }
    }

    [HarmonyPatchCategory("LoadingScreen")]
    [HarmonyPatch(typeof(MyGuiScreenLoading))]
    internal static class LoadingScreenPatch
    {
        [HarmonyPostfix,HarmonyPatch(nameof(MyGuiScreenLoading.LoadContent))]
        private static void Opened() => LoadingScreen.Loading=true;
        [HarmonyFinalizer,HarmonyPatch(nameof(MyGuiScreenLoading.UnloadContent))]
        private static void Closed() => LoadingScreen.Loading=false;
    }
}
