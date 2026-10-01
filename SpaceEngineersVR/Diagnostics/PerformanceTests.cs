using System;
using SpaceEngineersVR.Player;
using Valve.VR;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class PerformanceTests
    {
        private static void Near(double actual,double expected,string message)
        {
            if(double.IsNaN(actual) || Math.Abs(actual-expected)>.0001) throw new Exception(message);
        }
        internal static void Run(Action<string> log)
        {
            var frames=new RenderPerformance.FrameWindow();
            foreach(int hz in new[] {30,60,90})
            {
                frames.Reset(); frames.Add(0,null);
                RenderPerformance.View current=null;
                for(int i=1;i<=hz;i++) current=frames.Add((double)i/hz,new Compositor_FrameTiming {
                    m_flPreSubmitGpuMs=8,m_flPostSubmitGpuMs=2,m_flTotalRenderGpuMs=12,m_nNumFramePresents=2,
                    m_nNumDroppedFrames=(uint)(i==hz ? 1 : 0) });
                if(current==null) throw new Exception("Performance display did not publish a complete window");
                Near(current.Fps,hz,"Application FPS confused with headset refresh");
                Near(current.FrameMs,1000.0/hz,"Frame duration incorrect");
                Near(current.AppGpuMs,10,"Application GPU timing incorrect");
                Near(current.TotalGpuMs,12,"Compositor total timing incorrect");
                Near(current.Repeated,1,"Extra presents counted as application frames");
                Near(current.Dropped,1,"Dropped frame count incorrect");
            }
            frames.Reset(); frames.Add(100,null);
            for(int i=1;i<=30;i++) frames.Add(100+i/60.0,null);
            var hitch=frames.Add(101.5,null);
            Near(hitch.Fps,31/1.5,"A render stall was omitted from FPS");
            if(!double.IsNaN(hitch.AppGpuMs)) throw new Exception("Missing GPU timing presented as zero");
            frames.Reset(); frames.Add(500,null);
            if(frames.Add(500.5,null)!=null) throw new Exception("Menu pause survived the reset");
            if(frames.Add(double.NaN,null)!=null || frames.Add(0,null)!=null) throw new Exception("Invalid clock was published");
            log("PASS performance display: application 30/60/90 Hz, GPU/present counters, render stalls, missing timings and pause reset.");
        }
    }
}
