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
            public double RenderMs=double.NaN,PoseWaitMs=double.NaN,GameMs=double.NaN,WorkerMs=double.NaN,SimSpeed=double.NaN;
            public double VideoMemoryGb=double.NaN,VideoBudgetGb=double.NaN;
            public uint Dropped;
            public bool DesktopVSync;
            public string Resolution;
            // The desktop Present runs every VR frame, so the game's VSync waits for the monitor on the render thread.
            public string Limit => Fps>=RefreshHz*.97 ? "headset refresh rate" : RenderMs>=FrameMs*.9 ? DesktopVSync ? "desktop VSync" : "render thread (CPU)" :
                AppGpuMs>=FrameMs*.6 ? "GPU" : double.IsNaN(AppGpuMs) || double.IsNaN(RenderMs) ? "--" : "frame pacing";
        }
        internal sealed class FrameWindow
        {
            private double start=double.NaN,appGpu,totalGpu,render,wait;
            private int frames,timings,cpu;
            private uint repeats,drops;
            internal void Reset() { start=double.NaN; frames=timings=cpu=0; appGpu=totalGpu=render=wait=0; repeats=drops=0; }
            internal View Add(double now,Compositor_FrameTiming? timing,double renderMs=double.NaN,double waitMs=double.NaN)
            {
                if(double.IsNaN(now) || double.IsInfinity(now)) { Reset(); return null; }
                if(double.IsNaN(start) || now<=start) { Reset(); start=now; return null; }
                frames++;
                if(!double.IsNaN(renderMs) && !double.IsNaN(waitMs)) { render+=renderMs; wait+=waitMs; cpu++; }
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
                    Repeated=timings>0 ? (double)repeats/timings : double.NaN,Dropped=drops,
                    RenderMs=cpu>0 ? render/cpu : double.NaN,PoseWaitMs=cpu>0 ? wait/cpu : double.NaN };
                Reset(); start=now;
                return result;
            }
        }
        private static readonly FrameWindow window=new FrameWindow();
        private static readonly uint timingSize=(uint)Marshal.SizeOf(typeof(Compositor_FrameTiming));
        internal static volatile View Current;
        // Kept through menus for the settings screens.
        internal static volatile View Last;
        internal static double PoseWait=double.NaN,RefreshHz=double.NaN;
        private static double lastCycle=double.NaN;
        private static uint lastFrame;
        private static double gpu,appGpu,preGpu,postGpu,compositorGpu,presentCpu,submitCpu,interval;
        private static uint dropped,repeated,cpuReason,gpuReason,motion,throttled,maxThrottle;
        private static int count;
        private static bool unavailable,refreshUnavailable;
        public static string Summary { get; private set; }="Timing available after playing in VR";
        public static void Sample(bool active)
        {
            double now=(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency;
            if(!active)
            {
                window.Reset(); Current=null; ResetLog(); lastCycle=double.NaN;
                DynamicResolution.Sample(false,now,double.NaN,double.NaN,double.NaN,RefreshHz);
                return;
            }
            Compositor_FrameTiming? timing=null;
            if(!unavailable)
                try { timing=ReadTiming(); }
                catch(Exception ex) { unavailable=true; Logger.Warning(ex,"SteamVR timing unavailable"); }
            // The render thread waits in WaitGetPoses for the headset; the rest of the cycle is its own work or GPU back-pressure.
            double frame=double.IsNaN(lastCycle) ? double.NaN : (now-lastCycle)*1000,wait=PoseWait;
            double render=double.IsNaN(frame) || double.IsNaN(wait) ? double.NaN : Math.Max(0,frame-wait);
            lastCycle=now; PoseWait=double.NaN;
            double frameGpu=timing.HasValue ? timing.Value.m_flPreSubmitGpuMs+timing.Value.m_flPostSubmitGpuMs : double.NaN;
            DynamicResolution.Sample(true,now,frame,frameGpu,render,RefreshHz);
            var current=window.Add(now,timing,render,double.IsNaN(render) ? double.NaN : wait);
            if(current!=null)
            {
                if(!refreshUnavailable)
                    try
                    {
                        var error=ETrackedPropertyError.TrackedProp_Success;
                        float hz=OpenVR.System.GetFloatTrackedDeviceProperty(OpenVR.k_unTrackedDeviceIndex_Hmd,ETrackedDeviceProperty.Prop_DisplayFrequency_Float,ref error);
                        if(error==ETrackedPropertyError.TrackedProp_Success && hz>0 && !float.IsInfinity(hz)) current.RefreshHz=RefreshHz=hz;
                    }
                    catch(Exception ex) { refreshUnavailable=true; Logger.Warning(ex,"Headset refresh unavailable"); }
                Describe(current);
                Current=Last=current;
            }
        }
        internal static bool DesktopVSync => Sandbox.Engine.Platform.VideoMode.MyVideoSettingsManager.CurrentDeviceSettings.VSync>0;
        private static SharpDX.DXGI.Adapter3 adapter;
        private static IntPtr adapterDevice;
        private static bool memoryUnavailable;
        private static void Describe(View view)
        {
            var game=Sandbox.MySandboxGame.Static;
            if(game!=null) { view.GameMs=game.CPUTimeSmooth; view.WorkerMs=game.ThreadTimeSmooth; }
            float sim=Sandbox.Game.Multiplayer.Sync.ServerSimulationRatio;
            view.SimSpeed=float.IsNaN(sim) || float.IsInfinity(sim) ? double.NaN : sim;
            view.DesktopVSync=DesktopVSync;
            var size=EyeResolution.Current;
            var hud=EyeResolution.Hud;
            view.Resolution=$"Eyes {size.X}x{size.Y}"+(hud.X>0 && size.X!=hud.X ? $" of {hud.X}x{hud.Y}" : "")+"; "+DynamicResolution.Describe();
            if(memoryUnavailable) return;
            try
            {
                var device=Wrappers.MyRender11.DeviceInstance;
                if(adapter==null || adapterDevice!=device.NativePointer)
                {
                    adapter?.Dispose(); adapter=null; adapterDevice=device.NativePointer;
                    using(var dxgi=device.QueryInterface<SharpDX.DXGI.Device>())
                    using(var parent=dxgi.Adapter) adapter=parent.QueryInterfaceOrNull<SharpDX.DXGI.Adapter3>();
                    if(adapter==null) { memoryUnavailable=true; return; }
                }
                var memory=adapter.QueryVideoMemoryInfo(0,SharpDX.DXGI.MemorySegmentGroup.Local);
                view.VideoMemoryGb=memory.CurrentUsage/1073741824.0; view.VideoBudgetGb=memory.Budget/1073741824.0;
            }
            catch(Exception ex) { memoryUnavailable=true; adapter?.Dispose(); adapter=null; Logger.Warning(ex,"Video memory usage unavailable"); }
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
            string cpu=current==null ? "" : $"; render thread {current.RenderMs:F1} ms, pose wait {current.PoseWaitMs:F1} ms; game thread {current.GameMs:F1} ms, workers {current.WorkerMs:F1} ms, sim speed {current.SimSpeed:F2}; video memory {current.VideoMemoryGb:F2}/{current.VideoBudgetGb:F2} GB; {current.Resolution}; limited by {current.Limit}";
            Logger.Info($"VR timing: eyes {size.X}x{size.Y}; {Summary}; dropped {dropped}; repeated presents {repeated}; third person {ThirdPersonView.Active}; application {app}; app GPU {appGpu/count:F1} ms; reprojection CPU/GPU reasons {cpuReason}/{gpuReason}; motion {motion}; throttled {throttled}/{count} (max {maxThrottle}){cpu}");
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
