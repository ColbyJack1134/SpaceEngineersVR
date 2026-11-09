#if SEVR_DIAGNOSTICS && !LINUX
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Wrappers;
using VRageMath;
using Device=SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Diagnostics
{
    // Render-thread NGX smoke test on the game's device, no world: init, one DLAA feature, two evaluates with plugin motion vectors.
    // SEVR_DLSS_PROBE_SIZE adds the GPU cost of one evaluate per preset for DLAA and DLSS Quality at that eye size.
    internal static class DlssProbe
    {
        private const int Size=256,Samples=8;
        private static readonly (string Name,uint Value)[] Presets={("K",Ngx.PresetK),("F",Ngx.PresetF)};

        private sealed class Targets : IDisposable
        {
            internal readonly Texture2D Color,Output,Depth;
            internal readonly ShaderResourceView DepthView;
            internal readonly int Render;
            internal Targets(Device device,int render,int output)
            {
                Render=render;
                Color=Target(device,render,Format.R8G8B8A8_UNorm,BindFlags.ShaderResource|BindFlags.RenderTarget);
                Output=Target(device,output,Format.R8G8B8A8_UNorm,BindFlags.ShaderResource|BindFlags.RenderTarget|BindFlags.UnorderedAccess);
                Depth=Target(device,render,Format.R32_Typeless,BindFlags.ShaderResource|BindFlags.DepthStencil);
                DepthView=new ShaderResourceView(device,Depth,new ShaderResourceViewDescription { Format=Format.R32_Float,Dimension=SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,Texture2D={ MipLevels=1 } });
                using(var clear=new DepthStencilView(device,Depth,new DepthStencilViewDescription { Format=Format.D32_Float,Dimension=DepthStencilViewDimension.Texture2D }))
                    device.ImmediateContext.ClearDepthStencilView(clear,DepthStencilClearFlags.Depth,.5f,0);
            }
            internal Texture2D Motion(Device device)
            {
                var projection=(Matrix)VrMath.Projection(-1,1,-1,1,.05);
                return DlssMotion.Generate(device,DepthView,Render,Render,Matrix.Invert(projection),projection,Dlss.Previous(projection,new Vector3D(1,0,0),Vector3D.Zero));
            }
            internal bool Evaluate(IntPtr feature,int frame,bool reset,Texture2D motion)
            {
                var pixels=Dlss.Pixels(frame);
                return Ngx.Evaluate(feature,Color,Depth,motion,Output,pixels.X,pixels.Y,reset,Render,Render);
            }
            public void Dispose() { DepthView.Dispose(); Color.Dispose(); Output.Dispose(); Depth.Dispose(); }
        }

        internal static void Run(Action<string> log)
        {
            Device device=MyRender11.DeviceInstance;
            string runtime=Dlss.RuntimeDirectory();
            if(runtime==null) throw new InvalidOperationException("DLSS probe: "+Dlss.RuntimeFile+" not found; run Deploy.ps1 with the runtime in .tools/dlss/runtime");
            var timer=Stopwatch.StartNew();
            var verdict=Ngx.Init(device,runtime,VRage.FileSystem.MyFileSystem.UserDataPath);
            if(verdict!=Ngx.Verdict.Supported) throw new InvalidOperationException("DLSS probe: NGX "+verdict+": "+Ngx.LastError);
            double init=timer.Elapsed.TotalMilliseconds;
            IntPtr feature=IntPtr.Zero;
            try
            {
                using(var targets=new Targets(device,Size,Size))
                {
                    timer.Restart();
                    feature=Ngx.CreateFeature(Size,Size,Size,Size);
                    if(feature==IntPtr.Zero) throw new InvalidOperationException("DLSS probe: "+Ngx.LastError);
                    double create=timer.Elapsed.TotalMilliseconds;
                    var motion=targets.Motion(device);
                    timer.Restart();
                    for(int i=0;i<2;i++)
                        if(!targets.Evaluate(feature,i,i==0,motion)) throw new InvalidOperationException("DLSS probe evaluate "+i+": "+Ngx.LastError);
                    device.ImmediateContext.Flush();
                    log($"PASS DLSS NGX: driver {Ngx.DriverPath}; runtime {runtime}; init {init:F0} ms; {Size}x{Size} feature {create:F0} ms; two evaluates {timer.Elapsed.TotalMilliseconds:F1} ms");
                }
                Ngx.ReleaseFeature(ref feature);
                int size;
                if(int.TryParse(Environment.GetEnvironmentVariable("SEVR_DLSS_PROBE_SIZE"),NumberStyles.Integer,CultureInfo.InvariantCulture,out size) && size>0)
                    log(Cost(device,size,ref feature));
            }
            finally
            {
                Ngx.ReleaseFeature(ref feature);
                DlssMotion.Dispose();
                MyRender11.RC.ClearState();
            }
        }

        // GPU time of one evaluate per preset and mode, timestamp queries around Samples evaluates after a warm-up.
        private static string Cost(Device device,int size,ref IntPtr feature)
        {
            var context=device.ImmediateContext;
            var line=new StringBuilder("DLSS cost at "+size+"x"+size+" per eye:");
            using(var disjoint=new Query(device,new QueryDescription { Type=QueryType.TimestampDisjoint }))
            using(var start=new Query(device,new QueryDescription { Type=QueryType.Timestamp }))
            using(var end=new Query(device,new QueryDescription { Type=QueryType.Timestamp }))
                foreach(var preset in Presets)
                    foreach(var mode in new[] {1,2})
                        using(var targets=new Targets(device,(int)Math.Round(size*Dlss.ModeScales[mode]),size))
                        {
                            string name="preset "+preset.Name+" "+Dlss.Modes[mode];
                            Ngx.ReleaseFeature(ref feature);
                            var timer=Stopwatch.StartNew();
                            feature=Ngx.CreateFeature(targets.Render,targets.Render,size,size,mode==1 ? Ngx.QualityDlaa : Ngx.QualityMax,preset.Value);
                            if(feature==IntPtr.Zero) { line.Append(" "+name+" unavailable;"); continue; }
                            double create=timer.Elapsed.TotalMilliseconds;
                            var motion=targets.Motion(device);
                            if(!targets.Evaluate(feature,0,true,motion)) throw new InvalidOperationException("DLSS probe "+name+": "+Ngx.LastError);
                            context.Begin(disjoint);
                            context.End(start);
                            for(int i=0;i<Samples;i++)
                                if(!targets.Evaluate(feature,i,false,motion)) throw new InvalidOperationException("DLSS probe "+name+": "+Ngx.LastError);
                            context.End(end);
                            context.End(disjoint);
                            context.Flush();
                            long ticksStart,ticksEnd; QueryDataTimestampDisjoint clock;
                            var wait=Stopwatch.StartNew();
                            while(!context.GetData(disjoint,AsynchronousFlags.None,out clock) || !context.GetData(start,AsynchronousFlags.None,out ticksStart) || !context.GetData(end,AsynchronousFlags.None,out ticksEnd))
                                if(wait.ElapsedMilliseconds>5000) throw new InvalidOperationException("DLSS probe: timestamp queries did not complete");
                            if(clock.Disjoint) { line.Append(" "+name+" clock disjoint;"); continue; }
                            double ms=(ticksEnd-ticksStart)*1000.0/clock.Frequency/Samples;
                            line.Append($" {name} {targets.Render}->{size} {ms:F1} ms (create {create:F0} ms);");
                        }
            return line.ToString();
        }

        private static Texture2D Target(Device device,int size,Format format,BindFlags bind) => new Texture2D(device,new Texture2DDescription
        {
            Width=size,Height=size,MipLevels=1,ArraySize=1,Format=format,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=bind
        });
    }
}
#endif
