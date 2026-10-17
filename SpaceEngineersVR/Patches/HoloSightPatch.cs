using System;
using HarmonyLib;
using Sandbox.Definitions;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    // Weapon entities read their model from the item definition when created, so the copy must be in place before the world spawns them.
    [HarmonyPatch(typeof(MyDefinitionManager),nameof(MyDefinitionManager.LoadData))]
    internal static class HoloSightPatch
    {
        private static void Postfix()
        {
            try { HoloSight.Prepare(); }
            catch(Exception ex) { Logger.Warning(ex,"Holographic sights unavailable"); }
        }
    }
}
