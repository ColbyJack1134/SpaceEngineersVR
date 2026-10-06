using HarmonyLib;
using Sandbox.Game.Screens.Helpers.RadialMenuActions;
using Sandbox.Graphics.GUI;

namespace SpaceEngineersVR.Player
{
    // The native chat action waits for a HUD visibility change that never comes in VR; open the chat box directly.
    internal static class VrChat
    {
        private static bool keyboardPending;
        internal static void Open()
        {
            new MyActionChat().OpenChatScreen();
            keyboardPending=true;
        }
        internal static bool IsChat(MyGuiScreenBase screen) => screen?.GetType().Name=="MyGuiScreenChat";
        internal static void Send(MyGuiScreenBase screen,MyGuiControlTextbox textbox)
            => AccessTools.Method(screen.GetType(),"OnInputFieldActivated")?.Invoke(screen,new object[] { textbox });
        internal static void Update()
        {
            if(!keyboardPending || !IsChat(Components.VRGUIManager.TopScreen)) return;
            keyboardPending=false;
            MenuKeyboard.Open();
        }
    }
}
