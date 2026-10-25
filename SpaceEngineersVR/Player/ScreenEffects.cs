using System;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    // Chromatic aberration and vignette are per-eye artifacts in a headset; the bloom dirt texture reads as a dirty visor.
    internal static class ScreenEffects
    {
        private static bool wasActive;
        private static float wasDirt=-1;
        private static bool Active => Main.VrActive && Common.Config.ReduceScreenEffects;
        private static float Dirt => !Main.VrActive ? 1 : !Common.Config.VisorDust ? 0 : Common.Config.HideDustWithVisorOpen ? HelmetHud.VisorClosure:1;
        public static void Update()
        {
            float dirt=Dirt;
            if(Active==wasActive && Math.Abs(dirt-wasDirt)<.01f) return;
            wasActive=Active; wasDirt=dirt;
            MyPostprocessSettingsWrapper.MarkDirty();
        }
        // Applied inside the native reload that rewrites chromatic/vignette from the definition; the dirt ratio and bloom are not reloaded natively.
        internal static void Apply(ref MyPostprocessSettings settings,MyPostprocessSettings definition) => Apply(ref settings,definition,Active,Dirt);
        internal static void Apply(ref MyPostprocessSettings settings,MyPostprocessSettings definition,bool reduce,float dirt)
        {
            if(reduce) { settings.Data.ChromaticFactor=0; settings.Data.VignetteStart=0; }
            float closedRatio=MathHelper.Clamp(definition.Data.BloomDirtRatio,0,1),ratio=closedRatio*MathHelper.Clamp(dirt,0,1);
            settings.Data.BloomDirtRatio=ratio;
            // Bloom is scaled by dirt*ratio+(1-ratio); keep the clean-area bloom at its closed-visor level so only the specks fade.
            settings.Data.BloomMult=definition.Data.BloomMult*(ratio<1 ? (1-closedRatio)/(1-ratio):1);
        }
    }
}
