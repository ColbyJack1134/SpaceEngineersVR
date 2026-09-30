using HarmonyLib;
using Sandbox.Game.GUI.HudViewers;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyHudMarkerRender), nameof(MyHudMarkerRender.Draw))]
    internal static class WorldMarkersPatch
    {
        private static void Prefix(MyHudMarkerRender __instance) => WorldMarkers.Capture(__instance);
    }
}
