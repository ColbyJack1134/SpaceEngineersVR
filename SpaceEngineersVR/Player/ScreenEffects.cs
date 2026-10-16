using SpaceEngineersVR.Plugin;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    // Chromatic aberration and the vignette are per-eye artifacts in a headset; the bloom dirt texture reads as a dirty visor.
    internal static class ScreenEffects
    {
        private static bool wasActive,wasHidingDirt;
        private static bool Active => Main.VrActive && Common.Config.ReduceScreenEffects;
        private static bool HideDirt => Active && !HelmetHud.VisorClosed;
        public static void Update()
        {
            if(Active==wasActive && HideDirt==wasHidingDirt) return;
            wasActive=Active; wasHidingDirt=HideDirt;
            MyPostprocessSettingsWrapper.MarkDirty();
        }
        // The gameplay screen reloads chromatic/vignette from the environment definition whenever the settings are dirty,
        // so the override is applied inside that reload; the dirt ratio is not reloaded natively and is always restored here.
        internal static void Apply(ref MyPostprocessSettings settings,MyPostprocessSettings definition) => Apply(ref settings,definition,Active,HideDirt);
        internal static void Apply(ref MyPostprocessSettings settings,MyPostprocessSettings definition,bool active,bool hideDirt)
        {
            if(active) { settings.Data.ChromaticFactor=0; settings.Data.VignetteStart=0; }
            settings.Data.BloomDirtRatio=hideDirt ? 0:definition.Data.BloomDirtRatio;
        }
    }
}
