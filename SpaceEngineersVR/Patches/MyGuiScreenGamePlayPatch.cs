using SpaceEngineersVR.Player.Components;
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
        private static bool GameplayInput() => !Main.VrActive || InputRouter.Gameplay || MenuKeyboard.Hotkeys;

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MyGuiScreenGamePlay.MoveAndRotatePlayerOrCamera))]
        public static bool Prefix()
        {
            if (!Main.VrActive || Main.MenuOpen) return true;
            if(SpectatorView.Active) return false;
            if (ThirdPersonView.Manipulating) return false;
            if (VRMovementComponent.UsingControllerMovement)
                return false;

            return Common.Config.EnableKeyboardAndMouseControls;
        }
    }
}
