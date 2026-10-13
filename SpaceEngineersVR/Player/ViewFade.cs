using System;
using Valve.VR;

namespace SpaceEngineersVR.Player
{
    // The darkest request wins the single compositor fade.
    internal static class ViewFade
    {
        internal enum Source { Transition, Wall }
        private static readonly float[] levels=new float[2];
        private static float applied=-1;
        private static readonly object sync=new object();
        internal static void Set(Source source,float alpha,float seconds)
        {
            lock(sync)
            {
                levels[(int)source]=alpha;
                float level=Math.Max(levels[0],levels[1]);
                if(level==applied) return;
                applied=level;
                OpenVR.Compositor?.FadeToColor(seconds,0,0,0,level,false);
            }
        }
    }
}
