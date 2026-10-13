using HarmonyLib;
using Sandbox.Game.Gui;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyGuiScreenGamePlay))]
    public static class MyGuiScreenGamePlayPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MyGuiScreenGamePlay.HandleInput))]
        private static bool GameplayInput() => !Main.VrActive || InputRouter.Gameplay || InputRouter.KeyboardMouse || MenuKeyboard.Hotkeys;

        // VRMovementComponent merges keyboard and mouse into its own MoveAndRotate call.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(MyGuiScreenGamePlay.MoveAndRotatePlayerOrCamera))]
        public static bool Prefix() => !Main.VrActive || Main.MenuOpen;
    }
}
