using System;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // Render-thread only.
    internal static class DynamicResolution
    {
        internal const float Floor=.7f,Step=.05f;

        internal sealed class Controller
        {
            private struct Window
            {
                public double Since,Frame,Gpu,Render;
                public int Count,GpuCount;
                public void Add(double now,double frame,double gpu,double render)
                {
                    if(Count==0) { Since=now; Frame=Gpu=Render=0; GpuCount=0; }
                    Count++; Frame+=frame; Render+=render;
                    if(!double.IsNaN(gpu) && !double.IsInfinity(gpu)) { Gpu+=gpu; GpuCount++; }
                }
                public double Elapsed(double now) => Count==0 ? 0:now-Since;
                public double FrameMean => Frame/Count;
                public double RenderMean => Render/Count;
                public double GpuMean => GpuCount>0 ? Gpu/GpuCount:double.NaN;
            }
            // Panic catches missed frames quickly; down and up use longer means so a change follows load, not noise.
            internal const double SettleSeconds=1.5,PanicSeconds=.5,DownSeconds=2,UpSeconds=8;
            internal const double UpHoldAfterUp=15,UpHoldAfterDown=30,MaxUpHold=240,NoGainBlock=60,MaxNoGainBlock=600;
            private Window panic,down,up;
            private static readonly int MaxSteps=(int)Math.Floor((1-Floor)/Step+1e-4);
            private int steps,verifySteps=-1;
            private double settleUntil,upHoldUntil,downBlockedUntil,lastUp=double.NegativeInfinity,lastChange,upHold=UpHoldAfterDown,noGainBlock=NoGainBlock,verifyFrame;
            internal float Scale => 1-steps*Step;
            internal string Reason { get; private set; }="";

            internal void Reset() { steps=0; verifySteps=-1; upHold=UpHoldAfterDown; noGainBlock=NoGainBlock; settleUntil=upHoldUntil=downBlockedUntil=0; lastUp=double.NegativeInfinity; Clear(); }
            internal void Clear() { panic.Count=down.Count=up.Count=0; }

            // Fewer pixels help only while the GPU fills most of the frame and the render thread is not saturated.
            private static bool GpuLimited(Window w)
            {
                double frame=w.FrameMean;
                if(w.RenderMean>=frame*.9) return false;
                double gpu=w.GpuMean;
                return double.IsNaN(gpu) || gpu>=frame*.6;
            }

            // now in seconds, the rest in milliseconds; gpu is NaN when unknown.
            internal bool Sample(double now,double frame,double gpu,double render,double refreshHz,float target)
            {
                if(double.IsNaN(frame) || frame<=0 || double.IsInfinity(frame)) return false;
                double rate=refreshHz>0 && !double.IsInfinity(refreshHz) ? Math.Min(target,refreshHz):target;
                double period=1000/Math.Max(1,rate);
                if(now<settleUntil) return false;
                // Loading and spawning hitches are not GPU load; leave them out of every window.
                if(frame>Math.Max(100,period*4)) return false;
                panic.Add(now,frame,gpu,render); down.Add(now,frame,gpu,render); up.Add(now,frame,gpu,render);

                if(verifySteps>=0 && down.Elapsed(now)>=DownSeconds)
                {
                    int previous=verifySteps; verifySteps=-1;
                    // Frames paced by something other than the GPU do not speed up; each repeat waits longer before trying again.
                    if(down.FrameMean>verifyFrame*.97 && down.FrameMean>period*1.04)
                    {
                        downBlockedUntil=now+noGainBlock; noGainBlock=Math.Min(MaxNoGainBlock,noGainBlock*2);
                        return Set(previous,now,Describe("no gain at the lower resolution",down,period));
                    }
                    noGainBlock=NoGainBlock;
                }
                if(steps<MaxSteps && now>=downBlockedUntil)
                {
                    if(panic.Elapsed(now)>=PanicSeconds)
                    {
                        if(panic.FrameMean>period*1.3 && GpuLimited(panic)) return Down(2,now,panic,period);
                        panic.Count=0;
                    }
                    if(down.Elapsed(now)>=DownSeconds)
                    {
                        if(down.FrameMean>period*1.04 && GpuLimited(down)) return Down(down.FrameMean>period*1.15 ? 2:1,now,down,period);
                        down.Count=0;
                    }
                }
                else { panic.Count=0; if(down.Elapsed(now)>=DownSeconds) down.Count=0; }
                if(up.Elapsed(now)<UpSeconds) return false;
                var window=up; up.Count=0;
                if(steps==0 || now<upHoldUntil || window.FrameMean>period*1.02) return false;
                double ratio=(Scale+Step)/Scale; ratio*=ratio;
                double gpuMean=window.GpuMean;
                bool room=double.IsNaN(gpuMean) ? window.FrameMean*ratio<period*.85 : gpuMean*ratio<period*.75;
                if(!room) return false;
                lastUp=now; upHoldUntil=now+UpHoldAfterUp;
                return Set(steps-1,now,Describe("room for a higher resolution",window,period));
            }
            private bool Down(int count,double now,Window w,double period)
            {
                // A step down soon after a step up means that resolution does not hold; wait longer before trying it again.
                upHold=now-lastUp<20 ? Math.Min(MaxUpHold,upHold*2) : now-lastChange>120 ? UpHoldAfterDown : upHold;
                upHoldUntil=now+upHold;
                verifySteps=steps; verifyFrame=w.FrameMean;
                return Set(Math.Min(MaxSteps,steps+count),now,Describe("below the target",w,period));
            }
            private bool Set(int value,double now,string reason)
            {
                if(value==steps) return false;
                steps=value; lastChange=now; settleUntil=now+SettleSeconds; Reason=reason; Clear();
                return true;
            }
            private static string Describe(string why,Window w,double period) =>
                $"{why}: {1000/w.FrameMean:F1} fps for {1000/period:F0}, GPU {(double.IsNaN(w.GpuMean) ? "--":w.GpuMean.ToString("F1"))} ms, render thread {w.RenderMean:F1} ms";
        }

        private static readonly Controller controller=new Controller();
        private static float configuredTarget=float.NaN;
        internal static float Scale { get; private set; }=1;

        internal static void Sample(bool active,double now,double frame,double gpu,double render,double refreshHz)
        {
            var config=Common.Config;
            if(config==null) return;
            if(!config.DynamicResolution)
            {
                if(Scale!=1) { controller.Reset(); Scale=1; Logger.Info("Dynamic resolution off: eyes back to the configured resolution"); }
                return;
            }
            if(config.TargetFrameRate!=configuredTarget) { configuredTarget=config.TargetFrameRate; controller.Clear(); }
            if(!active)
            {
                controller.Clear();
                if(!Main.WorldAvailable && Scale!=1) { controller.Reset(); Scale=1; }
                return;
            }
            float from=Scale;
            if(!controller.Sample(now,frame,gpu,render,refreshHz,config.TargetFrameRate)) return;
            Scale=controller.Scale;
            var size=EyeResolution.Size(EyeResolution.Recommended,Scale);
            Logger.Info($"Dynamic resolution {from*100:F0}% -> {Scale*100:F0}% ({size.X}x{size.Y}): {controller.Reason}");
        }
        internal static string Describe()
        {
            var config=Common.Config;
            if(config==null || !config.DynamicResolution) return "Dynamic resolution off";
            return $"Dynamic {Scale*100:F0}% · target {config.TargetFrameRate:F0} FPS";
        }
    }
}
