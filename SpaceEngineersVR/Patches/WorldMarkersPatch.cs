using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.AddGPS))]
    internal static class GpsMarkerPatch
    {
        private static void Postfix(MyHudMarkerRender __instance,Sandbox.Game.Screens.Helpers.MyGps gps) => WorldMarkers.CaptureGps(__instance,gps);
    }
    [HarmonyPatch(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.ChangeSignalMode))]
    internal static class SignalModePatch
    {
        private static void Postfix()
        {
            if(!SpaceEngineersVR.Plugin.Main.VrActive) return;
            var mode=MyHudMarkerRender.SignalDisplayMode;
            var config=SpaceEngineersVR.Plugin.Common.Config;
            config.WaypointMode=mode==MyHudMarkerRender.SignalMode.Off ? 0:mode==MyHudMarkerRender.SignalMode.NoNames ? 1:2;
        }
    }
    [HarmonyPatch(typeof(MyHudMarkerRender), nameof(MyHudMarkerRender.Draw))]
    internal static class WorldMarkersPatch
    {
        private static void Prefix(MyHudMarkerRender __instance) => WorldMarkers.Capture(__instance);
    }
}
