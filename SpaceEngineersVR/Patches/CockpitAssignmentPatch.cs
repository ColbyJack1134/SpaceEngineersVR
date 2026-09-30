using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase),"OnDragAndDropOnDrop")]
    internal static class CockpitAssignmentPatch
    {
        private static bool Prefix(MyGuiScreenToolbarConfigBase __instance,MyDragAndDropEventArgs eventArgs) =>
            !GUI.CockpitAssignment.HandleDrop(__instance,eventArgs);
    }
}
