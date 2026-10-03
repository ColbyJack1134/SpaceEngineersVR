using System;
using System.IO;
using System.Runtime.InteropServices;
using HarmonyLib;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Plugin;
using Valve.VR;
using VRage.FileSystem;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Player
{
    // Skips scene geometry under the SteamVR hidden-area mesh for each eye. Depth at the
    // near plane rejects geometry until occlusion queries and lighting, which see cleared depth.
    internal static class HiddenAreaMask
    {
        private const string Shader=@"
cbuffer Constants : register(b0) { float4 Depth; };
float4 VS(float2 position : POSITION) : SV_Position { return float4(position,Depth.x,1); }
float4 PS() : SV_Target { return 0; }";
        private const string DepthDeclaration="Texture2D<float> Depth : register(t1);",Weight="weight *= depthWeight;";
        private static readonly Type renderer=AccessTools.TypeByName("VRageRender.MyRender11"),postprocess=AccessTools.TypeByName("VRageRender.MyPostprocessSettingsWrapper");
        private static Device device;
        private static DeviceContext context;
        private static VertexShader vertex;
        private static PixelShader pixel;
        private static InputLayout layout;
        private static Buffer constants;
        private static DepthStencilState writeDepth;
        private static RasterizerState rasterizer;
        private static readonly Buffer[] meshes=new Buffer[2];
        private static readonly int[] counts=new int[2];
        private static readonly ComputeShader[] histograms=new ComputeShader[2];
        private static readonly Texture2D[] visible=new Texture2D[2];
        private static readonly ShaderResourceView[] visibleViews=new ShaderResourceView[2];
        private static readonly RenderTargetView[] visibleTargets=new RenderTargetView[2];
        private static bool initialized,failed;
        private static float near,clear;
        private static DepthStencilView marked;
        private static int markedEye;
        [ThreadStatic] private static int exposureEye;

        internal static int Eye
        {
            get
            {
                int eye=StereoRenderState.Active ? StereoRenderState.View : -1;
                if(eye<0 || eye>1 || failed || Common.Config?.HiddenAreaMask!=true) return -1;
                if(!initialized) Initialize();
                return failed || counts[eye]==0 ? -1 : eye;
            }
        }

        internal static RawVector2[] Triangles(HmdVector2_t[] uv)
        {
            var result=new RawVector2[uv.Length];
            for(int i=0;i<uv.Length;i++) result[i]=new RawVector2(uv[i].v0*2-1,1-uv[i].v1*2);
            return result;
        }

        internal static string PatchHistogram(string source)
        {
            if(Count(source,DepthDeclaration)!=1 || Count(source,Weight)!=1) return null;
            return source.Replace(DepthDeclaration,DepthDeclaration+"\nTexture2D<float> HiddenAreaVisible : register(t2);")
                .Replace(Weight,Weight+"\n        if (HiddenAreaVisible[dispatchId] < 0.5f) weight = 0;");
        }

        internal static ComputeShader[] CompileHistograms(Device target,string shaders)
        {
            string path=Path.Combine(shaders,"Postprocess","EyeAdaptation","UpdateHistogram.hlsl");
            string source=PatchHistogram(File.ReadAllText(path)) ?? throw new InvalidDataException("Unrecognized eye adaptation histogram shader");
            var result=new ComputeShader[2];
            using(var includes=new Includes(shaders,Path.GetDirectoryName(path)))
                for(int i=0;i<2;i++)
                {
                    var macros=i==0 ? null : new[] { new SharpDX.Direct3D.ShaderMacro("PRIORITIZE_SCREEN_CENTER",null) };
                    using(var bytecode=ShaderBytecode.Compile(source,"__compute_shader","cs_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,macros,includes,path))
                        result[i]=new ComputeShader(target,bytecode);
                }
            return result;
        }

        internal static void Create(Device target,RawVector2[][] eyes,ComputeShader[] histogram,bool complementary)
        {
            device=target;
            context=new DeviceContext(device);
            using(var vs=ShaderBytecode.Compile(Shader,"VS","vs_4_0"))
            using(var ps=ShaderBytecode.Compile(Shader,"PS","ps_4_0"))
            {
                vertex=new VertexShader(device,vs); pixel=new PixelShader(device,ps);
                layout=new InputLayout(device,ShaderSignature.GetInputSignature(vs),new[] { new InputElement("POSITION",0,Format.R32G32_Float,0) });
            }
            constants=new Buffer(device,16,ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
            writeDepth=new DepthStencilState(device,new DepthStencilStateDescription { IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.All,DepthComparison=Comparison.Always });
            rasterizer=new RasterizerState(device,new RasterizerStateDescription { FillMode=FillMode.Solid,CullMode=CullMode.None,IsDepthClipEnabled=false });
            for(int i=0;i<2;i++)
            {
                counts[i]=eyes[i]?.Length ?? 0;
                if(counts[i]>0) meshes[i]=Buffer.Create(device,BindFlags.VertexBuffer,eyes[i]);
            }
            histograms[0]=histogram[0]; histograms[1]=histogram[1];
            near=complementary ? 1:0; clear=complementary ? 0:1;
            initialized=true;
        }

        private static void Initialize()
        {
            initialized=true;
            try
            {
                var eyes=new RawVector2[2][];
                for(int i=0;i<2;i++)
                {
                    var mesh=OpenVR.System.GetHiddenAreaMesh((EVREye)i,EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);
                    if(mesh.unTriangleCount==0 || mesh.pVertexData==IntPtr.Zero) continue;
                    var uv=new HmdVector2_t[mesh.unTriangleCount*3];
                    int size=Marshal.SizeOf<HmdVector2_t>();
                    for(int v=0;v<uv.Length;v++) uv[v]=Marshal.PtrToStructure<HmdVector2_t>(mesh.pVertexData+v*size);
                    eyes[i]=Triangles(uv);
                }
                if(eyes[0]==null && eyes[1]==null) { Logger.Info("Hidden-area mask unavailable: SteamVR reported no mesh"); failed=true; return; }
                var target=Wrappers.MyRender11.DeviceInstance;
                Create(target,eyes,CompileHistograms(target,Path.Combine(MyFileSystem.ShadersBasePath,"Shaders")),
                    (bool)AccessTools.Field(renderer,"UseComplementaryDepthBuffer").GetValue(null));
                Logger.Info($"Hidden-area mask: {counts[0]/3} left and {counts[1]/3} right triangles");
            }
            catch(Exception ex) { Fail(ex); }
        }

        internal static void Mark(object gbuffer,int eye)
        {
            try
            {
                marked=(DepthStencilView)CockpitRender.Member(CockpitRender.Member(CockpitRender.Member(gbuffer,"DepthStencil"),"Dsv"),"Dsv");
                markedEye=eye;
                DrawDepth(device.ImmediateContext,marked,eye,near);
            }
            catch(Exception ex) { marked=null; Fail(ex); }
        }

        internal static void Restore()
        {
            if(marked==null) return;
            try { DrawDepth(device.ImmediateContext,marked,markedEye,clear); }
            catch(Exception ex) { Fail(ex); }
            finally { marked=null; }
        }

        internal static void DrawDepth(DeviceContext immediate,DepthStencilView target,int eye,float depth)
        {
            int width,height;
            using(var resource=target.ResourceAs<Texture2D>()) { width=resource.Description.Width; height=resource.Description.Height; }
            context.ClearState();
            context.OutputMerger.SetTargets(target);
            context.OutputMerger.SetDepthStencilState(writeDepth);
            context.Rasterizer.State=rasterizer;
            context.Rasterizer.SetViewport(0,0,width,height);
            Mesh(eye,depth);
            context.PixelShader.Set(null);
            context.Draw(counts[eye],0);
            Execute(immediate);
        }

        private static void Mesh(int eye,float depth)
        {
            var data=new RawVector4(depth,0,0,0);
            context.UpdateSubresource(ref data,constants);
            context.InputAssembler.InputLayout=layout;
            context.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(meshes[eye],8,0));
            context.VertexShader.Set(vertex);
            context.VertexShader.SetConstantBuffer(0,constants);
        }

        // The engine's state cache stays valid because the immediate context state is restored.
        private static void Execute(DeviceContext immediate)
        {
            using(var commands=context.FinishCommandList(false)) immediate.ExecuteCommandList(commands,true);
        }

        internal static bool ExposurePending => exposureEye>0;
        internal static void BeginExposure() => exposureEye=Eye+1;
        internal static void EndExposure() => exposureEye=0;

        // Replaces the native histogram dispatch so hidden pixels carry no exposure weight.
        internal static bool Dispatch(DeviceContext immediate,int x,int y,int z)
        {
            int eye=exposureEye-1;
            if(eye<0) return false;
            exposureEye=0;
            var buffers=immediate.ComputeShader.GetConstantBuffers(1,1);
            var uavs=immediate.ComputeShader.GetUnorderedAccessViews(0,1);
            var sources=immediate.ComputeShader.GetShaderResources(0,2);
            try
            {
                if(sources[0]==null) return false;
                bool center=(bool)CockpitRender.Member(Settings(),"EyeAdaptationPrioritizeScreenCenter");
                return Histogram(immediate,eye,center,buffers[0],uavs[0],sources[0],sources[1],x,y,z);
            }
            catch(Exception ex) { Fail(ex); return false; }
            finally
            {
                foreach(var item in buffers) item?.Dispose();
                foreach(var item in uavs) item?.Dispose();
                foreach(var item in sources) item?.Dispose();
            }
        }

        internal static bool Histogram(DeviceContext immediate,int eye,bool center,Buffer histogramConstants,UnorderedAccessView histogram,
            ShaderResourceView source,ShaderResourceView depth,int x,int y,int z)
        {
            int width,height;
            using(var resource=source.ResourceAs<Texture2D>()) { width=resource.Description.Width; height=resource.Description.Height; }
            var mask=Visible(immediate,eye,width,height);
            context.ClearState();
            context.ComputeShader.Set(histograms[center ? 1:0]);
            context.ComputeShader.SetConstantBuffer(1,histogramConstants);
            context.ComputeShader.SetUnorderedAccessView(0,histogram);
            context.ComputeShader.SetShaderResources(0,source,depth,mask);
            context.Dispatch(x,y,z);
            Execute(immediate);
            return true;
        }

        private static ShaderResourceView Visible(DeviceContext immediate,int eye,int width,int height)
        {
            var texture=visible[eye];
            if(texture!=null && texture.Description.Width==width && texture.Description.Height==height) return visibleViews[eye];
            visibleViews[eye]?.Dispose(); visibleTargets[eye]?.Dispose(); texture?.Dispose();
            visible[eye]=texture=new Texture2D(device,new Texture2DDescription { Width=width,Height=height,MipLevels=1,ArraySize=1,Format=Format.R8_UNorm,
                SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource });
            visibleViews[eye]=new ShaderResourceView(device,texture);
            visibleTargets[eye]=new RenderTargetView(device,texture);
            context.ClearState();
            context.ClearRenderTargetView(visibleTargets[eye],new RawColor4(1,1,1,1));
            context.OutputMerger.SetTargets(visibleTargets[eye]);
            context.Rasterizer.State=rasterizer;
            context.Rasterizer.SetViewport(0,0,width,height);
            Mesh(eye,0);
            context.PixelShader.Set(pixel);
            context.Draw(counts[eye],0);
            Execute(immediate);
            return visibleViews[eye];
        }

        private static object Settings()
        {
            var member=CockpitRender.Find(postprocess,"Settings");
            return member is System.Reflection.FieldInfo field ? field.GetValue(null) : ((System.Reflection.PropertyInfo)member).GetValue(null);
        }

        private static int Count(string text,string value)
        {
            int count=0;
            for(int i=text.IndexOf(value,StringComparison.Ordinal);i>=0;i=text.IndexOf(value,i+value.Length,StringComparison.Ordinal)) count++;
            return count;
        }

        private static void Fail(Exception ex)
        {
            if(!failed) Logger.Warning(ex,"Hidden-area mask disabled");
            failed=true;
        }

        private sealed class Includes : CallbackBase,Include
        {
            private readonly string root,local;
            public Includes(string root,string local) { this.root=root; this.local=local; }
            public Stream Open(IncludeType type,string fileName,Stream parentStream)
            {
                string path=Path.Combine(local,fileName);
                return File.OpenRead(type==IncludeType.Local && File.Exists(path) ? path : Path.Combine(root,fileName));
            }
            public void Close(Stream stream) => stream.Dispose();
        }
    }
}
