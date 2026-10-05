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
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase),"OnGridItemDoubleClicked",new[] {typeof(MyGuiControlGrid),typeof(MyGuiControlGrid.EventArgs),typeof(bool)})]
    internal static class ToolbarAssignmentDoubleClickPatch
    {
        private static bool Prefix(MyGuiScreenToolbarConfigBase __instance,MyGuiControlGrid sender,MyGuiControlGrid.EventArgs eventArgs) =>
            !GUI.CockpitAssignment.HandleDoubleClick(__instance,sender,eventArgs);
    }
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase),"UpdateContextMenu")]
    internal static class SwitchActionRankPatch
    {
        private static bool Prefix(MyGuiScreenToolbarConfigBase __instance,ref MyGuiControlContextMenu currentContextMenu,MyToolbarItemActions item,ref bool __result)
        {
            if(!GUI.CockpitAssignment.FillActions(__instance,currentContextMenu,item,out bool filled)) return true;
            __result=filled; return false;
        }
    }
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase),nameof(MyGuiScreenToolbarConfigBase.RequestItemParameters))]
    internal static class HandleParameterPatch
    {
        private static bool Prefix(MyToolbarItem item,System.Action<bool> callback)
        {
            if(!GUI.CockpitAssignment.SkipParameters(item)) return true;
            callback(true); return false;
        }
    }
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase),nameof(MyGuiScreenToolbarConfigBase.HandleInput))]
    internal static class ToolbarAssignmentInputPatch
    {
        private static bool Prefix(MyGuiScreenToolbarConfigBase __instance) => !GUI.CockpitAssignment.HandleInput(__instance);
    }
    [HarmonyPatch(typeof(MyToolbarItemTerminalBlock),nameof(MyToolbarItemTerminalBlock.PossibleActions))]
    internal static class CockpitViewAssignmentPatch
    {
        // Switches are the pilot's ship controls: offer the ship-only view, control and Jump actions native button panels exclude.
        private static void Prefix(MyToolbarItemTerminalBlock __instance,ref MyToolbarType type)
        {
            if(type==MyToolbarType.ButtonPanel && CockpitActions.Toolbar!=null && ReferenceEquals(MyToolbarComponent.CurrentToolbar,CockpitActions.Toolbar) &&
                (CockpitSwitchState.ViewBlock(__instance.Block) || __instance.Block is Sandbox.Game.Entities.MyJumpDrive))
                type=MyToolbarType.Ship;
        }
    }
}
