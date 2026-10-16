using System;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    // Chromatic aberration and the vignette are per-eye artifacts in a headset; the bloom dirt texture reads as a dirty visor,
    // so it fades in and out with the visor animation.
    internal static class ScreenEffects
    {
        private static bool wasActive;
        private static float wasDirt=-1;
        private static bool Active => Main.VrActive && Common.Config.ReduceScreenEffects;
        private static float Dirt => Active ? HelmetHud.VisorClosure:1;
        public static void Update()
        {
            float dirt=Dirt;
            if(Active==wasActive && Math.Abs(dirt-wasDirt)<.01f) return;
            wasActive=Active; wasDirt=dirt;
            MyPostprocessSettingsWrapper.MarkDirty();
        }
        // The gameplay screen reloads chromatic/vignette from the environment definition whenever the settings are dirty,
        // so the override is applied inside that reload; the dirt ratio is not reloaded natively and is always restored here.
        internal static void Apply(ref MyPostprocessSettings settings,MyPostprocessSettings definition) => Apply(ref settings,definition,Active,Dirt);
        internal static void Apply(ref MyPostprocessSettings settings,MyPostprocessSettings definition,bool active,float dirt)
        {
            if(active) { settings.Data.ChromaticFactor=0; settings.Data.VignetteStart=0; }
            settings.Data.BloomDirtRatio=definition.Data.BloomDirtRatio*(active ? MathHelper.Clamp(dirt,0,1):1);
        }
    }
}
