using System;
using System.Runtime.InteropServices;
using SpaceEngineersVR.Plugin;
using Valve.VR;

namespace SpaceEngineersVR.Player
{
    internal static class RenderPerformance
    {
        private static uint lastFrame;
        private static double gpu,interval;
        private static uint dropped,repeated;
        private static int count;
        private static bool unavailable;
        public static string Summary { get; private set; }="Timing available after playing in VR";
        public static void Sample()
        {
            if(unavailable) return;
            try { ReadTiming(); }
            catch(Exception ex) { unavailable=true; Logger.Warning(ex,"SteamVR timing unavailable"); }
        }
        private static void ReadTiming()
        {
            var timing=new Compositor_FrameTiming { m_nSize=(uint)Marshal.SizeOf(typeof(Compositor_FrameTiming)) };
            if(!OpenVR.Compositor.GetFrameTiming(ref timing,0) || timing.m_nFrameIndex==lastFrame) return;
            lastFrame=timing.m_nFrameIndex;
            gpu+=timing.m_flTotalRenderGpuMs; interval+=timing.m_flClientFrameIntervalMs;
            dropped+=timing.m_nNumDroppedFrames; repeated+=timing.m_nNumFramePresents>1 ? timing.m_nNumFramePresents-1 : 0;
            StereoRenderState.Record("compositor_previous",timing.m_flTotalRenderGpuMs,timing.m_flClientFrameIntervalMs,timing.m_nNumDroppedFrames,timing.m_nReprojectionFlags);
            if(++count<300) return;
            Summary=$"SteamVR GPU {gpu/count:F1} ms · frame interval {interval/count:F1} ms";
            var size=EyeResolution.Current;
            Logger.Info($"VR timing: eyes {size.X}x{size.Y}; {Summary}; dropped {dropped}; repeated presents {repeated}; third person {ThirdPersonView.Active}");
            gpu=interval=0; dropped=repeated=0; count=0;
        }
    }
}
