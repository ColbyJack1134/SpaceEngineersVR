using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRage.Game.ModAPI;

namespace SpaceEngineersVR.Patches
{
    // Mouse and VR double-taps both set this flag; EndShoot reads it to keep the tool running.
    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.EndShoot))]
    internal static class ToolLockPatch
    {
        private static void Prefix(MyCharacter __instance,MyShootActionEnum action)
        {
            var detected=MyGuiScreenGamePlay.DoubleClickDetected;
            if(Common.Config?.DoubleTapToolLock==false && detected!=null && (int)action<detected.Length && __instance==MySession.Static?.LocalCharacter)
                detected[(int)action]=false;
        }
    }
}
