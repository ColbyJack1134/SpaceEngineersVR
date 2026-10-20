#if SEVR_DIAGNOSTICS
using System;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DynamicResolutionTests
    {
        private sealed class Load
        {
            public Func<double,double> Gpu=s=>15*s*s;
            public Func<double,double,double> Frame=(gpu,s)=>gpu*1.25;
            public Func<double,double> Render=s=>6;
        }
        private struct Result { public float Scale,Lowest; public int Changes; public double Time; }

        private static Result Run(DynamicResolution.Controller controller,Load load,double seconds,double start=0,float target=60,double refresh=90,Func<double,bool> hitch=null)
        {
            var result=new Result {Lowest=controller.Scale};
            double now=start,period=1000/refresh;
            while(now<start+seconds)
            {
                double s=controller.Scale,gpu=load.Gpu(s),frame=Math.Max(period,load.Frame(gpu,s));
                if(hitch!=null && hitch(now)) frame=400;
                now+=frame/1000;
                if(controller.Sample(now,frame,gpu,load.Render(s),refresh,target)) result.Changes++;
                result.Lowest=Math.Min(result.Lowest,controller.Scale);
            }
            result.Scale=controller.Scale; result.Time=now;
            return result;
        }
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }

        internal static void Run(Action<string> log)
        {
            var c=new DynamicResolution.Controller();
            var r=Run(c,new Load(),600);
            Require(Math.Abs(r.Scale-.95f)<.001f && r.Changes<=3,$"GPU-bound load settled at {r.Scale} after {r.Changes} changes");

            c=new DynamicResolution.Controller();
            r=Run(c,new Load {Gpu=s=>10*s*s,Render=s=>17},600);
            Require(r.Scale==1 && r.Changes==0,"CPU-bound frames lowered the resolution");

            c=new DynamicResolution.Controller();
            r=Run(c,new Load {Gpu=s=>14.5*s*s,Frame=(g,s)=>18.5},600);
            Require(r.Scale==1 && r.Changes<=8,$"Pacing-bound frames left the scale at {r.Scale} after {r.Changes} changes");

            c=new DynamicResolution.Controller();
            r=Run(c,new Load {Gpu=s=>40*s*s},20);
            Require(Math.Abs(r.Scale-DynamicResolution.Floor)<.001f,$"Heavy load stopped at {r.Scale} instead of the floor");
            r=Run(c,new Load {Gpu=s=>5*s*s},600,r.Time);
            Require(r.Scale==1,$"Light load stayed at {r.Scale}");

            c=new DynamicResolution.Controller();
            r=Run(c,new Load {Gpu=s=>8*s*s},120,hitch:t=>t%1<.012);
            Require(r.Changes==0,"Hitches lowered the resolution");

            c=new DynamicResolution.Controller();
            r=Run(c,new Load {Gpu=s=>8*s*s},120,target:120);
            Require(r.Changes==0,"A target above the refresh rate lowered the resolution");

            c=new DynamicResolution.Controller();
            r=Run(c,new Load {Gpu=s=>26*s*s},2.5);
            Require(r.Scale<=.9f+.001f,$"Panic step missing: {r.Scale} after 2.5 s");

            c=new DynamicResolution.Controller();
            r=Run(c,new Load {Gpu=s=>2+11.5*s*s,Frame=(g,s)=>g*1.3},1800);
            Require(r.Changes<=12,$"Borderline load changed resolution {r.Changes} times in 30 minutes");
            int borderline=r.Changes;

            c.Reset();
            Require(c.Scale==1,"Reset kept a lowered resolution");
            log($"PASS dynamic resolution: GPU-bound settle, CPU-bound and pacing guards, floor, recovery, hitches, refresh cap, panic step, {borderline} changes in 30 minutes at a borderline load");
        }
    }
}
#endif
