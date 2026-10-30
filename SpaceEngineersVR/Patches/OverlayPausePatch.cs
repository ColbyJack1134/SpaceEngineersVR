using HarmonyLib;
using Sandbox;

namespace SpaceEngineersVR.Patches
{
    // After a headset drop released a stranded overlay pause, Steam's late close would pop an unrelated pause such as the Esc menu.
    [HarmonyPatch(typeof(MySandboxGame),"OverlayActivated")]
    internal static class OverlayPausePatch
    {
        internal static bool Released;
        private static bool Prefix(bool isActive)
        {
            bool skip=!isActive && Released;
            Released=false;
            return !skip;
        }
    }
}
