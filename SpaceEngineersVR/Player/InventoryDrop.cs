using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player.Components;
using VRage.Game.ModAPI;

namespace SpaceEngineersVR.Player
{
    internal static class InventoryDrop
    {
        private static readonly FieldInfo tabs=AccessTools.Field(typeof(MyGuiScreenTerminal),"m_terminalTabs");
        private static readonly FieldInfo controller=AccessTools.Field(typeof(MyGuiScreenTerminal),"m_controllerInventory");
        private static readonly FieldInfo throwOut=AccessTools.Field(BuildPlannerActions.InventoryController,"m_throwOutButton");

        public static bool TryThrow()
        {
            if(!(VRGUIManager.TopScreen is MyGuiScreenTerminal terminal) ||
                (tabs.GetValue(terminal) as MyGuiControlTabControl)?.SelectedPage!=(int)MyTerminalPageEnum.Inventory) return false;
            var inventory=controller.GetValue(terminal);
            var button=inventory==null ? null:throwOut.GetValue(inventory) as MyGuiControlButton;
            if(button==null || !button.Enabled) return false;
            button.RaiseButtonClicked();
            return true;
        }

        // A grid drag drops where the button is released, and the terminal throws an item dropped outside every control.
        public static bool Dragging(IMyGuiControlsParent parent)
        {
            if(parent==null) return false;
            foreach(var control in parent.Controls)
                if(control is MyGuiControlGridDragAndDrop drag && drag.IsActive() || control is IMyGuiControlsParent child && Dragging(child)) return true;
            return false;
        }
    }
}
