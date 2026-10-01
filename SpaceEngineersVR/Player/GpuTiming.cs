using System;
using System.Diagnostics;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // Render-thread only. Sample one frame every two seconds without waiting for the GPU.
    internal static class GpuTiming
    {
        internal enum Area { SceneLeft,WorldUiLeft,SceneRight,WorldUiRight,Companion,Hud }
        private const int Count=6;
        private static readonly Query[] stamps=new Query[Count*2];
        private static readonly bool[] written=new bool[Count*2];
        private static readonly ulong[] ticks=new ulong[Count*2];
        private static Query disjoint;
        private static DeviceContext context;
        private static IntPtr device;
        private static bool active,pending,failed,thirdPerson,cockpit;
        private static long next,submitted;
        private static VRageMath.Vector2I size;
        internal static int Completed { get; private set; }

        internal static void BeginFrame(Device suppliedDevice=null)
        {
            if(failed) return;
            try
            {
                var current=suppliedDevice ?? Wrappers.MyRender11.DeviceInstance;
                if(device!=current.NativePointer) { Reset(); device=current.NativePointer; context=current.ImmediateContext; }
                long now=Stopwatch.GetTimestamp();
                if(pending)
                {
                    Poll();
                    if(pending && now-submitted>Stopwatch.Frequency*5) throw new TimeoutException("GPU timestamps did not complete");
                }
                if(active || pending || now<next) return;
                if(disjoint==null)
                {
                    disjoint=new Query(current,new QueryDescription { Type=QueryType.TimestampDisjoint });
                    for(int i=0;i<stamps.Length;i++) stamps[i]=new Query(current,new QueryDescription { Type=QueryType.Timestamp });
                }
                Array.Clear(written,0,written.Length);
                cockpit=EssentialHud.Current?.Piloting==true;
                thirdPerson=ThirdPersonView.Active;
                context.Begin(disjoint); active=true; next=now+Stopwatch.Frequency*2;
            }
            catch(Exception ex) { Fail(ex); }
        }
        internal static void Begin(Area area) => Mark((int)area*2);
        internal static void End(Area area) => Mark((int)area*2+1);
        private static void Mark(int index)
        {
            if(!active) return;
            try { context.End(stamps[index]); written[index]=true; }
            catch(Exception ex) { Fail(ex); }
        }
        internal static void EndFrame()
        {
            if(!active) return;
            try { context.End(disjoint); active=false; pending=true; size=EyeResolution.Current; submitted=Stopwatch.GetTimestamp(); }
            catch(Exception ex) { Fail(ex); }
        }
        private static void Poll()
        {
            QueryDataTimestampDisjoint clock;
            if(!context.GetData(disjoint,AsynchronousFlags.DoNotFlush,out clock)) return;
            if(clock.Disjoint || clock.Frequency<=0) { pending=false; return; }
            for(int i=0;i<stamps.Length;i++)
                if(written[i] && !context.GetData(stamps[i],AsynchronousFlags.DoNotFlush,out ticks[i])) return;
            pending=false;
            Completed++;
            double Ms(Area area)
            {
                int i=(int)area*2;
                return written[i] && written[i+1] && ticks[i+1]>=ticks[i] ? (ticks[i+1]-ticks[i])*1000.0/clock.Frequency : double.NaN;
            }
            Logger.Info($"VR GPU sample: eyes {size.X}x{size.Y}; cockpit {cockpit}; third person {thirdPerson}; native scene L/R {Ms(Area.SceneLeft):F3}/{Ms(Area.SceneRight):F3} ms; world UI L/R {Ms(Area.WorldUiLeft):F3}/{Ms(Area.WorldUiRight):F3} ms; companion {Ms(Area.Companion):F3} ms; HUD upload/draw {Ms(Area.Hud):F3} ms");
        }
        internal static void Reset()
        {
            if(active && context!=null) { try { context.End(disjoint); } catch { } }
            active=pending=false; next=0;
            for(int i=0;i<stamps.Length;i++) { stamps[i]?.Dispose(); stamps[i]=null; }
            disjoint?.Dispose(); disjoint=null; context=null; device=IntPtr.Zero;
        }
        private static void Fail(Exception ex)
        {
            failed=true; Reset(); Logger.Warning(ex,"GPU timing disabled; rendering retained");
        }
    }
}
