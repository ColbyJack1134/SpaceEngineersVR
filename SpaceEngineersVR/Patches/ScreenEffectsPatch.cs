using HarmonyLib;
using SpaceEngineersVR.Player;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyPostprocessSettingsWrapper),nameof(MyPostprocessSettingsWrapper.ReloadSettingsFrom))]
    internal static class ScreenEffectsPatch
    {
        private static void Postfix(MyPostprocessSettings definition) => ScreenEffects.Apply(ref MyPostprocessSettingsWrapper.Settings,definition);
    }
}
