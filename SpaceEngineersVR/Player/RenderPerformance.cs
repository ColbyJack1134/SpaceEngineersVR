using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SpaceEngineersVR.Plugin;
using Valve.VR;

namespace SpaceEngineersVR.Player
{
    internal static class RenderPerformance
    {
        internal sealed class View
        {
            public double Fps,FrameMs,AppGpuMs,TotalGpuMs,Repeated,RefreshHz=double.NaN;
            public uint Dropped;
        }
        internal sealed class FrameWindow
        {
            private double start=double.NaN,appGpu,totalGpu;
            private int frames,timings;
            private uint repeats,drops;
            internal void Reset() { start=double.NaN; frames=timings=0; appGpu=totalGpu=0; repeats=drops=0; }
            internal View Add(double now,Compositor_FrameTiming? timing)
            {
                if(double.IsNaN(now) || double.IsInfinity(now)) { Reset(); return null; }
                if(double.IsNaN(start) || now<=start) { Reset(); start=now; return null; }
                frames++;
                if(timing.HasValue)
                {
                    var t=timing.Value;
                    appGpu+=t.m_flPreSubmitGpuMs+t.m_flPostSubmitGpuMs; totalGpu+=t.m_flTotalRenderGpuMs;
                    repeats+=t.m_nNumFramePresents>1 ? t.m_nNumFramePresents-1 : 0;
                    drops+=t.m_nNumDroppedFrames; timings++;
                }
                double duration=now-start;
                if(duration<1) return null;
                var result=new View { Fps=frames/duration,FrameMs=duration*1000/frames,
                    AppGpuMs=timings>0 ? appGpu/timings : double.NaN,TotalGpuMs=timings>0 ? totalGpu/timings : double.NaN,
                    Repeated=timings>0 ? (double)repeats/timings : double.NaN,Dropped=drops };
                Reset(); start=now;
                return result;
            }
        }
        private static readonly FrameWindow window=new FrameWindow();
        private static readonly uint timingSize=(uint)Marshal.SizeOf(typeof(Compositor_FrameTiming));
        internal static volatile View Current;
        private static uint lastFrame;
        private static double gpu,appGpu,preGpu,postGpu,compositorGpu,presentCpu,submitCpu,interval;
        private static uint dropped,repeated,cpuReason,gpuReason,motion,throttled,maxThrottle;
        private static int count;
        private static bool unavailable,refreshUnavailable;
        public static string Summary { get; private set; }="Timing available after playing in VR";
        public static void Sample(bool active)
        {
            if(!active) { window.Reset(); Current=null; ResetLog(); return; }
            Compositor_FrameTiming? timing=null;
            if(!unavailable)
                try { timing=ReadTiming(); }
                catch(Exception ex) { unavailable=true; Logger.Warning(ex,"SteamVR timing unavailable"); }
            var current=window.Add((double)Stopwatch.GetTimestamp()/Stopwatch.Frequency,timing);
            if(current!=null)
            {
                if(!refreshUnavailable)
                    try
                    {
                        var error=ETrackedPropertyError.TrackedProp_Success;
                        float hz=OpenVR.System.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd,ETrackedDeviceProperty.Prop_DisplayFrequency_Float,ref error);
                        if(error==ETrackedPropertyError.TrackedProp_Success && hz>0 && !float.IsInfinity(hz)) current.RefreshHz=hz;
                    }
                    catch(Exception ex) { refreshUnavailable=true; Logger.Warning(ex,"Headset refresh unavailable"); }
                Current=current;
            }
        }
        private static Compositor_FrameTiming? ReadTiming()
        {
            var timing=new Compositor_FrameTiming { m_nSize=timingSize };
            if(!OpenVR.Compositor.GetFrameTiming(ref timing,0) || timing.m_nFrameIndex==lastFrame) return null;
            lastFrame=timing.m_nFrameIndex;
            gpu+=timing.m_flTotalRenderGpuMs; appGpu+=timing.m_flPreSubmitGpuMs+timing.m_flPostSubmitGpuMs;
            preGpu+=timing.m_flPreSubmitGpuMs; postGpu+=timing.m_flPostSubmitGpuMs;
            compositorGpu+=timing.m_flCompositorRenderGpuMs;
            presentCpu+=timing.m_flPresentCallCpuMs; submitCpu+=timing.m_flSubmitFrameMs;
            interval+=timing.m_flClientFrameIntervalMs;
            dropped+=timing.m_nNumDroppedFrames; repeated+=timing.m_nNumFramePresents>1 ? timing.m_nNumFramePresents-1 : 0;
            uint flags=timing.m_nReprojectionFlags,throttle=(flags&0xF00)>>8;
            if((flags&1)!=0) cpuReason++;
            if((flags&2)!=0) gpuReason++;
            if((flags&8)!=0) motion++;
            if(throttle>0) throttled++;
            maxThrottle=Math.Max(maxThrottle,throttle);
            StereoRenderState.Record("compositor_previous",timing.m_flTotalRenderGpuMs,timing.m_flClientFrameIntervalMs,timing.m_nNumDroppedFrames,timing.m_nReprojectionFlags);
            if(++count<300) return timing;
            Summary=$"SteamVR GPU {gpu/count:F1} ms · frame interval {interval/count:F1} ms";
            var size=EyeResolution.Current;
            var current=Current;
            string app=current==null ? "collecting" : $"{current.Fps:F1} FPS / {current.FrameMs:F1} ms (latest 1s); headset {current.RefreshHz:F1} Hz";
            Logger.Info($"VR timing: eyes {size.X}x{size.Y}; {Summary}; dropped {dropped}; repeated presents {repeated}; third person {ThirdPersonView.Active}; application {app}; app GPU {appGpu/count:F1} ms; reprojection CPU/GPU reasons {cpuReason}/{gpuReason}; motion {motion}; throttled {throttled}/{count} (max {maxThrottle})");
            Logger.Info($"VR runtime split: pre/post submit GPU {preGpu/count:F3}/{postGpu/count:F3} ms; compositor GPU {compositorGpu/count:F3} ms; present/submit CPU {presentCpu/count:F3}/{submitCpu/count:F3} ms");
            ResetLog();
            return timing;
        }
        private static void ResetLog()
        {
            gpu=appGpu=preGpu=postGpu=compositorGpu=presentCpu=submitCpu=interval=0; dropped=repeated=cpuReason=gpuReason=motion=throttled=maxThrottle=0; count=0;
        }
    }
}
