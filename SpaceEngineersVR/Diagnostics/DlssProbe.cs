#if SEVR_DIAGNOSTICS && !LINUX
using System;
using System.Collections.Generic;
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
                log(Objects(device));
                int size;
                if(int.TryParse(Environment.GetEnvironmentVariable("SEVR_DLSS_PROBE_SIZE"),NumberStyles.Integer,CultureInfo.InvariantCulture,out size) && size>0)
                {
                    log(Cost(device,size,ref feature));
                    log(ObjectCost(device,size));
                }
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
                            IntPtr current=feature;
                            double? ms=Time(context,disjoint,start,end,i=>
                            {
                                if(!targets.Evaluate(current,i,false,motion)) throw new InvalidOperationException("DLSS probe "+name+": "+Ngx.LastError);
                            });
                            line.Append(ms.HasValue ? $" {name} {targets.Render}->{size} {ms:F1} ms (create {create:F0} ms);" : " "+name+" clock disjoint;");
                        }
            return line.ToString();
        }

        // Mean GPU milliseconds of Samples runs; null when the clock was disjoint.
        private static double? Time(DeviceContext context,Query disjoint,Query start,Query end,Action<int> run)
        {
            context.Begin(disjoint);
            context.End(start);
            for(int i=0;i<Samples;i++) run(i);
            context.End(end);
            context.End(disjoint);
            context.Flush();
            long ticksStart,ticksEnd; QueryDataTimestampDisjoint clock;
            var wait=Stopwatch.StartNew();
            while(!context.GetData(disjoint,AsynchronousFlags.None,out clock) || !context.GetData(start,AsynchronousFlags.None,out ticksStart) || !context.GetData(end,AsynchronousFlags.None,out ticksEnd))
                if(wait.ElapsedMilliseconds>5000) throw new InvalidOperationException("DLSS probe: timestamp queries did not complete");
            return clock.Disjoint ? (double?)null : (ticksEnd-ticksStart)*1000.0/clock.Frequency/Samples;
        }

        private const float Near=.05f,SurfaceDepth=.5f;
        private static readonly Matrix projection=(Matrix)VrMath.Projection(-1,1,-1,1,Near);
        // The cleared depth is the plane z = -Near / SurfaceDepth in front of a camera at the origin; each box holds its left half.
        private static void Place(MatrixD world,float minX,float maxX,float back)
        {
            float z=-Near/SurfaceDepth;
            MatrixD previous=world*MatrixD.CreateTranslation(.01,0,0);
            DlssObjects.Emit(new List<DlssObjects.Candidate>
            {
                new DlssObjects.Candidate { Current=world,Previous=previous,Bounds=new BoundingBox(new Vector3(minX,-100,z-.02f),new Vector3(maxX,100,back)) }
            },Vector3D.Zero,projection,Matrix.Invert(projection));
        }

        // Index and vectors read back against the CPU reprojection: a box in front of the camera, one around it and a mirrored one.
        private static string Objects(Device device)
        {
            float z=-Near/SurfaceDepth;
            var cases=new (string Name,MatrixD World,float Min,float Max,float Back)[]
            {
                ("outside",MatrixD.Identity,-100,0,z+.02f),("inside",MatrixD.Identity,-100,0,1),("mirrored",MatrixD.CreateScale(-1,1,1),0,100,z+.02f)
            };
            using(var targets=new Targets(device,Size,Size))
                foreach(var box in cases)
                {
                    Place(box.World,box.Min,box.Max,box.Back);
                    var motion=DlssMotion.Generate(device,targets.DepthView,Size,Size,Matrix.Invert(projection),projection,projection,true);
                    foreach(int x in new[] {Size/4,Size*3/4})
                    {
                        bool inside=x<Size/2;
                        var uv=new Vector2((x+.5f)/Size,.5f+.5f/Size);
                        var clip=Vector4.Transform(new Vector4(uv.X*2-1,1-uv.Y*2,SurfaceDepth,1),Matrix.Invert(projection));
                        var point=new Vector3(clip.X,clip.Y,clip.Z)/clip.W;
                        var before=Vector4.Transform(new Vector4(inside ? Vector3.Transform(point,DlssObjects.Transforms[1]) : point,1),projection);
                        var expected=new Vector2((before.X/before.W*.5f+.5f-uv.X)*Size,(.5f-before.Y/before.W*.5f-uv.Y)*Size);
                        int index=Read(device,DlssMotion.Index,x,Size/2,1)[0];
                        var raw=Read(device,motion,x,Size/2,4);
                        var vector=new VRageMath.PackedVector.HalfVector2 { PackedValue=BitConverter.ToUInt32(raw,0) }.ToVector2();
                        if(index!=(inside ? 1:0)) throw new InvalidOperationException($"DLSS probe objects {box.Name}: index {index} at x {x}");
                        if(Vector2.Distance(vector,expected)>.05f || inside==expected.Length()<1) throw new InvalidOperationException($"DLSS probe objects {box.Name}: vector {vector} at x {x}, expected {expected}");
                    }
                }
            DlssObjects.Clear();
            return "PASS DLSS objects: box index and vectors match the CPU reprojection outside, inside and mirrored";
        }

        // Motion pass cost alone and with eight screen-filling boxes around the camera.
        private static string ObjectCost(Device device,int size)
        {
            var context=device.ImmediateContext;
            using(var disjoint=new Query(device,new QueryDescription { Type=QueryType.TimestampDisjoint }))
            using(var start=new Query(device,new QueryDescription { Type=QueryType.Timestamp }))
            using(var end=new Query(device,new QueryDescription { Type=QueryType.Timestamp }))
            using(var targets=new Targets(device,size,size))
            {
                var list=new List<DlssObjects.Candidate>();
                for(int i=0;i<8;i++)
                    list.Add(new DlssObjects.Candidate { Current=MatrixD.CreateTranslation(i*.001,0,0),Previous=MatrixD.CreateTranslation(i*.001+.01,0,0),Bounds=new BoundingBox(new Vector3(-100),new Vector3(100)) });
                var inverse=Matrix.Invert(projection);
                double?[] ms=new double?[2];
                for(int pass=0;pass<2;pass++)
                {
                    if(pass==1) DlssObjects.Emit(list,Vector3D.Zero,projection,inverse);
                    DlssMotion.Generate(device,targets.DepthView,size,size,inverse,projection,projection,pass==1);
                    ms[pass]=Time(context,disjoint,start,end,i=>DlssMotion.Generate(device,targets.DepthView,size,size,inverse,projection,projection,pass==1));
                }
                DlssObjects.Clear();
                return ms[0].HasValue && ms[1].HasValue ? $"DLSS motion pass at {size}x{size}: camera only {ms[0]:F2} ms, with 8 full-screen boxes {ms[1]:F2} ms" : "DLSS motion pass: clock disjoint";
            }
        }

        private static byte[] Read(Device device,Texture2D source,int x,int y,int bytes)
        {
            var description=source.Description;
            description.Usage=ResourceUsage.Staging; description.BindFlags=BindFlags.None; description.CpuAccessFlags=CpuAccessFlags.Read;
            using(var staging=new Texture2D(device,description))
            {
                device.ImmediateContext.CopyResource(source,staging);
                var mapped=device.ImmediateContext.MapSubresource(staging,0,MapMode.Read,SharpDX.Direct3D11.MapFlags.None);
                var data=new byte[bytes];
                System.Runtime.InteropServices.Marshal.Copy(mapped.DataPointer+y*mapped.RowPitch+x*bytes,data,0,bytes);
                device.ImmediateContext.UnmapSubresource(staging,0);
                return data;
            }
        }

        private static Texture2D Target(Device device,int size,Format format,BindFlags bind) => new Texture2D(device,new Texture2DDescription
        {
            Width=size,Height=size,MipLevels=1,ArraySize=1,Format=format,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=bind
        });
    }
}
#endif
