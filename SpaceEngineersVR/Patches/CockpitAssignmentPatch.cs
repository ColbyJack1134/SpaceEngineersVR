using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using Sandbox.Game.Screens.Helpers;
using SpaceEngineersVR.Player;
using VRage.Game;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase),nameof(MyGuiScreenToolbarConfigBase.Update))]
    internal static class ToolbarAssignmentUpdatePatch
    {
        private static void Postfix(MyGuiScreenToolbarConfigBase __instance) => GUI.CockpitAssignment.Update(__instance);
    }
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase),"OnDragAndDropOnDrop")]
    internal static class CockpitAssignmentPatch
    {
        private static bool Prefix(MyGuiScreenToolbarConfigBase __instance,MyDragAndDropEventArgs eventArgs) =>
            !GUI.CockpitAssignment.HandleDrop(__instance,eventArgs);
    }
    [HarmonyPatch(typeof(MyToolbarItemTerminalBlock),nameof(MyToolbarItemTerminalBlock.PossibleActions))]
    internal static class CockpitViewAssignmentPatch
    {
        private static void Prefix(MyToolbarItemTerminalBlock __instance,ref MyToolbarType type)
        {
            if(type==MyToolbarType.ButtonPanel && CockpitActions.Toolbar!=null && ReferenceEquals(MyToolbarComponent.CurrentToolbar,CockpitActions.Toolbar) && CockpitSwitchState.ViewBlock(__instance.Block))
                type=MyToolbarType.Ship;
        }
    }
}
