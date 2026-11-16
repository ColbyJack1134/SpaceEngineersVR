using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatchCategory("HudMarkers")]
    [HarmonyPatch(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.AddGPS))]
    internal static class GpsMarkerPatch
    {
        private static void Prefix(System.Collections.IList ___m_pointsOfInterest,out int __state) => __state=___m_pointsOfInterest.Count;
        private static void Postfix(MyHudMarkerRender __instance,Sandbox.Game.Screens.Helpers.MyGps gps,System.Collections.IList ___m_pointsOfInterest,int __state)
        {
            if(___m_pointsOfInterest.Count==__state+1) WorldMarkers.CaptureGps(__instance,gps);
        }
    }
    // AsyncUpdate ends by starting the parallel HUD draw, so the capture must run before it.
    [HarmonyPatchCategory("HudMarkers")]
    [HarmonyPatch(typeof(Sandbox.Game.Gui.MyGuiScreenHudSpace),"AsyncUpdate")]
    internal static class HiddenHudMarkersPatch
    {
        private static void Prefix(Sandbox.Game.Gui.MyGuiScreenHudSpace __instance,MyHudMarkerRender ___m_markerRender)
        {
            if(!SpaceEngineersVR.Plugin.Main.VrActive) return;
            WorldMarkers.CaptureHidden(__instance,___m_markerRender);
            WorldMarkers.UpdateTargetingStats();
            HudEmp.UpdateHidden(__instance);
        }
    }
    [HarmonyPatchCategory("HudMarkers")]
    [HarmonyPatch(typeof(MyHudMarkerRender), nameof(MyHudMarkerRender.Draw))]
    internal static class WorldMarkersPatch
    {
        private static void Prefix(MyHudMarkerRender __instance) => WorldMarkers.Capture(__instance);
    }
    [HarmonyPatchCategory("HudMarkers")]
    [HarmonyPatch(typeof(Sandbox.Game.Gui.MyHudEmpDistortion),nameof(Sandbox.Game.Gui.MyHudEmpDistortion.Draw))]
    internal static class EmpMarkersPatch
    {
        private static void Postfix() => HudEmp.Capture();
    }
}
