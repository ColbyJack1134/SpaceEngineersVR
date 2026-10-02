using System;
using System.Diagnostics;
using System.Threading;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    internal static class FeatureTiming
    {
        internal enum Area { Touch,TouchQuery,HudSample,HudPaint,Visor,Cockpit,CockpitTouch,CockpitButtons,Arms,CockpitGeometry,CockpitActors,CockpitAction,WaitPoses,SceneLeft,SceneRight,DesktopPresent,RemoteFeed }
        internal sealed class Measurement
        {
            public double Mean,P95,Peak;
            public long Time;
            public int Count;
        }
        private sealed class Samples
        {
            public readonly double[] Values=new double[600];
            public readonly double[] Sorted=new double[600];
            public int Count;
            public double Sum;
            public long Published;
            public Measurement Current;
        }
        private static readonly Samples[] samples=CreateSamples();
        private static Samples[] CreateSamples()
        {
            var result=new Samples[Enum.GetValues(typeof(Area)).Length];
            for(int i=0;i<result.Length;i++) result[i]=new Samples();
            return result;
        }
        internal static Measurement Latest(Area area) => Volatile.Read(ref samples[(int)area].Current);
        internal static long Start() => Stopwatch.GetTimestamp();
        internal static void End(Area area,long start)
        {
            var sample=samples[(int)area];
            long now=Stopwatch.GetTimestamp();
            double elapsed=(now-start)*1000.0/Stopwatch.Frequency;
            sample.Values[sample.Count++]=elapsed; sample.Sum+=elapsed;
            if(sample.Count==1 || sample.Count==sample.Values.Length || now-sample.Published>=Stopwatch.Frequency)
            {
                // Each area has one collecting thread; the renderer reads only published snapshots.
                Array.Copy(sample.Values,sample.Sorted,sample.Count); Array.Sort(sample.Sorted,0,sample.Count);
                Volatile.Write(ref sample.Current,new Measurement { Mean=sample.Sum/sample.Count,
                    P95=sample.Sorted[(int)Math.Ceiling(sample.Count*.95)-1],Peak=sample.Sorted[sample.Count-1],Time=now,Count=sample.Count });
                sample.Published=now;
            }
            if(area>=Area.CockpitGeometry && area<=Area.CockpitAction && Common.Plugin!=null)
                Logger.Info($"VR operation {area}: {elapsed:F3} ms");
            if(sample.Count<sample.Values.Length) return;
            var current=Latest(area);
            if(Common.Plugin!=null)
                Logger.Info($"VR feature {area}: mean {current.Mean:F3} ms; p95 {current.P95:F3}; peak {current.Peak:F3}; samples {sample.Count}");
            sample.Count=0; sample.Sum=0;
        }
    }
}
