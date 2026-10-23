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
using SpaceEngineersVR.Util;

namespace SpaceEngineersVR.Player
{
    // Skips scene geometry under the SteamVR hidden-area mesh for each eye. Depth at the
    // near plane rejects geometry until occlusion queries and lighting, which see cleared depth.
    internal static class HiddenAreaMask
    {
        private const string Shader=@"
cbuffer Constants : register(b0) { float4 Depth; };
float4 VS(float2 position : POSITION) : SV_Position { return float4(position,Depth.x,1); }
float4 PS() : SV_Target { return Depth.y; }";
        // Motion smoothing samples just outside the visible edge; keep a rendered margin there.
        internal const float OutwardScale=1.1f;
        private const string DepthDeclaration="Texture2D<float> Depth : register(t1);",Weight="weight *= depthWeight;",Center="uv = abs(uv - 0.5);";
        // Quest 3 visible-area outline in left-eye projection tangents (the right eye mirrors it), captured from SteamVR.
        // Headsets that report no mesh measure exposure over this area instead of the whole render.
        internal static readonly float[] QuestVisible={
            0f,-1.39f, .172f,-1.3062f, .3276f,-1.2226f, .4712f,-1.1375f, .6052f,-1.0482f, .7302f,-.9516f, .8391f,-.845f, .8391f,-.7358f,
            .8391f,-.6231f, .8391f,-.4953f, .8391f,-.347f, .8391f,-.179f, .8391f,0f, .8391f,.1767f, .8391f,.3422f, .8391f,.4898f,
            .8391f,.6145f, .8391f,.7125f, .7814f,.7814f, .6337f,.8259f, .4942f,.8559f, .3638f,.8783f, .2411f,.8999f, .1221f,.9272f,
            0f,.965f, -.1326f,.9657f, -.2791f,.9657f, -.4396f,.9657f, -.6116f,.9657f, -.7902f,.9657f, -.9687f,.9657f, -1.1195f,.859f,
            -1.2208f,.7048f, -1.2802f,.5303f, -1.3129f,.3518f, -1.3391f,.1763f, -1.3764f,0f, -1.3764f,-.1882f, -1.3764f,-.3911f, -1.3764f,-.6046f,
            -1.3764f,-.82f, -1.335f,-1.0244f, -1.2021f,-1.2021f, -1.0287f,-1.3407f, -.8293f,-1.4281f, -.6159f,-1.4281f, -.4001f,-1.4281f, -.1921f,-1.4281f };
        private static readonly Type renderer=AccessTools.TypeByName("VRageRender.MyRender11"),postprocess=AccessTools.TypeByName("VRageRender.MyPostprocessSettingsWrapper");
        private static Device device;
        private static DeviceContext context;
        private static VertexShader vertex;
        private static PixelShader pixel;
        private static InputLayout layout;
        private static Buffer constants,centerConstants;
        private static DepthStencilState writeDepth;
        private static RasterizerState rasterizer;
        private static readonly Buffer[] meshes=new Buffer[2];
        private static readonly int[] counts=new int[2];
        private static readonly ComputeShader[] histograms=new ComputeShader[2];
        private static readonly Texture2D[] visible=new Texture2D[2];
        private static readonly ShaderResourceView[] visibleViews=new ShaderResourceView[2];
        private static readonly RenderTargetView[] visibleTargets=new RenderTargetView[2];
        private static bool initialized,failed,exposureOnly;
        private static float near,clear;
        private static DepthStencilView marked;
        private static int markedEye;
        [ThreadStatic] private static int exposureEye;

        internal static int Eye => Resolve(false);
        private static int Resolve(bool exposure)
        {
            int eye=StereoRenderState.Active ? StereoRenderState.View : -1;
            if(eye<0 || eye>1 || failed || Common.Config?.HiddenAreaMask!=true) return -1;
            if(!initialized) Initialize();
            return failed || counts[eye]==0 || exposureOnly && !exposure ? -1 : eye;
        }

        internal static RawVector2[] Triangles(HmdVector2_t[] uv)
        {
            var result=new RawVector2[uv.Length];
            for(int i=0;i<uv.Length;i++) result[i]=new RawVector2((uv[i].v0*2-1)*OutwardScale,(1-uv[i].v1*2)*OutwardScale);
            return result;
        }

        // Native center weighting measures from the texture center, which sits off each eye's axis in an asymmetric projection.
        internal static string PatchHistogram(string source)
        {
            if(Count(source,DepthDeclaration)!=1 || Count(source,Weight)!=1 || Count(source,Center)!=1) return null;
            return source.Replace(DepthDeclaration,DepthDeclaration+"\nTexture2D<float> HiddenAreaVisible : register(t2);\ncbuffer HiddenAreaConstants : register(b2) { float4 HiddenAreaCenter; };")
                .Replace(Center,"uv = abs(uv - HiddenAreaCenter.xy);")
                .Replace(Weight,Weight+"\n        if (HiddenAreaVisible[dispatchId] < 0.5f) weight = 0;");
        }

        internal static RawVector2 AxisCenter(float left,float right,float top,float bottom) => new RawVector2(-left/(right-left),-top/(bottom-top));

        // Fan of the Quest outline in clip space for a projection given as raw tangents.
        internal static RawVector2[] QuestVisibleArea(bool leftEye,float left,float right,float top,float bottom)
        {
            int count=QuestVisible.Length/2;
            RawVector2 Clip(float x,float y) => new RawVector2((x-left)/(right-left)*2-1,1-(y-top)/(bottom-top)*2);
            var center=Clip(0,0);
            var result=new RawVector2[count*3];
            for(int i=0;i<count;i++)
            {
                int next=(i+1)%count;
                result[i*3]=center;
                result[i*3+1]=Clip(leftEye ? QuestVisible[i*2]:-QuestVisible[i*2],QuestVisible[i*2+1]);
                result[i*3+2]=Clip(leftEye ? QuestVisible[next*2]:-QuestVisible[next*2],QuestVisible[next*2+1]);
            }
            return result;
        }

        internal static ComputeShader[] CompileHistograms(Device target,string shaders)
        {
            string path=Path.Combine(shaders,"Postprocess","EyeAdaptation","UpdateHistogram.hlsl");
            string source=PatchHistogram(File.ReadAllText(NativePath.Of(path))) ?? throw new InvalidDataException("Unrecognized eye adaptation histogram shader");
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

        internal static void Create(Device target,RawVector2[][] eyes,ComputeShader[] histogram,bool complementary,bool visibleOnly=false,RawVector2[] centers=null)
        {
            DisposeResources();
            device=target;
            context=new DeviceContext(device);
            using(var vs=ShaderBytecode.Compile(Shader,"VS","vs_4_0"))
            using(var ps=ShaderBytecode.Compile(Shader,"PS","ps_4_0"))
            {
                vertex=new VertexShader(device,vs); pixel=new PixelShader(device,ps);
                layout=new InputLayout(device,ShaderSignature.GetInputSignature(vs),new[] { new InputElement("POSITION",0,Format.R32G32_Float,0) });
            }
            constants=new Buffer(device,16,ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
            centerConstants=Buffer.Create(device,BindFlags.ConstantBuffer,new[] {
                new RawVector4(centers?[0].X ?? .5f,centers?[0].Y ?? .5f,0,0),new RawVector4(centers?[1].X ?? .5f,centers?[1].Y ?? .5f,0,0) });
            writeDepth=new DepthStencilState(device,new DepthStencilStateDescription { IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.All,DepthComparison=Comparison.Always });
            rasterizer=new RasterizerState(device,new RasterizerStateDescription { FillMode=FillMode.Solid,CullMode=CullMode.None,IsDepthClipEnabled=false });
            for(int i=0;i<2;i++)
            {
                counts[i]=eyes[i]?.Length ?? 0;
                if(counts[i]>0) meshes[i]=Buffer.Create(device,BindFlags.VertexBuffer,eyes[i]);
            }
            histograms[0]=histogram[0]; histograms[1]=histogram[1];
            near=complementary ? 1:0; clear=complementary ? 0:1;
            exposureOnly=visibleOnly;
            initialized=true;
        }

        private static void Initialize()
        {
            initialized=true;
            try
            {
                var eyes=new RawVector2[2][];
                var projections=new float[2][];
                var centers=new RawVector2[2];
                for(int i=0;i<2;i++)
                {
                    float l=0,r=0,t=0,b=0;
                    OpenVR.System.GetProjectionRaw((EVREye)i,ref l,ref r,ref t,ref b);
                    projections[i]=new[] {l,r,t,b};
                    centers[i]=AxisCenter(l,r,t,b);
                    var mesh=OpenVR.System.GetHiddenAreaMesh((EVREye)i,EHiddenAreaMeshType.k_eHiddenAreaMesh_Standard);
                    if(mesh.unTriangleCount==0 || mesh.pVertexData==IntPtr.Zero) continue;
                    var uv=new HmdVector2_t[mesh.unTriangleCount*3];
                    int size=Marshal.SizeOf<HmdVector2_t>();
                    for(int v=0;v<uv.Length;v++) uv[v]=Marshal.PtrToStructure<HmdVector2_t>(mesh.pVertexData+v*size);
                    eyes[i]=Triangles(uv);
                }
                bool fallback=eyes[0]==null && eyes[1]==null;
                if(fallback)
                    for(int i=0;i<2;i++) eyes[i]=QuestVisibleArea(i==0,projections[i][0],projections[i][1],projections[i][2],projections[i][3]);
                var target=Wrappers.MyRender11.DeviceInstance;
                Create(target,eyes,CompileHistograms(target,Path.Combine(MyFileSystem.ShadersBasePath,"Shaders")),
                    (bool)AccessTools.Field(renderer,"UseComplementaryDepthBuffer").GetValue(null),fallback,centers);
                Logger.Info(fallback ? "Hidden-area mask unavailable: SteamVR reported no mesh; exposure uses the Quest 3 visible area" :
                    $"Hidden-area mask: {counts[0]/3} left and {counts[1]/3} right triangles");
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
            Mesh(eye,depth,0);
            context.PixelShader.Set(null);
            context.Draw(counts[eye],0);
            Execute(immediate);
        }

        private static void Mesh(int eye,float depth,float value)
        {
            var data=new RawVector4(depth,value,0,0);
            context.UpdateSubresource(ref data,constants);
            context.InputAssembler.InputLayout=layout;
            context.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;
            context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(meshes[eye],8,0));
            context.VertexShader.Set(vertex);
            context.VertexShader.SetConstantBuffer(0,constants);
            context.PixelShader.SetConstantBuffer(0,constants);
        }

        // The engine's state cache stays valid because the immediate context state is restored.
        private static void Execute(DeviceContext immediate)
        {
            using(var commands=context.FinishCommandList(false)) immediate.ExecuteCommandList(commands,true);
        }

        internal static bool ExposurePending => exposureEye>0;
        internal static void BeginExposure() => exposureEye=Resolve(true)+1;
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
            context.ComputeShader.SetConstantBuffer(2,centerConstants);
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
            // A visible-area mesh draws the area to keep; a hidden-area mesh draws the area to drop.
            float keep=exposureOnly ? 0:1;
            context.ClearState();
            context.ClearRenderTargetView(visibleTargets[eye],new RawColor4(keep,keep,keep,keep));
            context.OutputMerger.SetTargets(visibleTargets[eye]);
            context.Rasterizer.State=rasterizer;
            context.Rasterizer.SetViewport(0,0,width,height);
            Mesh(eye,0,1-keep);
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

        private static void DisposeResources()
        {
            context?.Dispose(); vertex?.Dispose(); pixel?.Dispose(); layout?.Dispose(); constants?.Dispose(); centerConstants?.Dispose();
            writeDepth?.Dispose(); rasterizer?.Dispose();
            for(int i=0;i<2;i++)
            {
                meshes[i]?.Dispose(); visibleViews[i]?.Dispose(); visibleTargets[i]?.Dispose(); visible[i]?.Dispose();
                meshes[i]=null; visibleViews[i]=null; visibleTargets[i]=null; visible[i]=null; counts[i]=0;
            }
        }

        private static void Fail(Exception ex)
        {
            if(!failed) Logger.Warning(ex,"Hidden-area mask disabled");
            failed=true;
        }

        internal sealed class Includes : CallbackBase,Include
        {
            private readonly string root,local;
            public Includes(string root,string local) { this.root=root; this.local=local; }
            public Stream Open(IncludeType type,string fileName,Stream parentStream)
            {
                string path=Path.Combine(parentStream is FileStream parent ? Path.GetDirectoryName(parent.Name) : local,fileName);
                path=NativePath.Of(path);
                return File.OpenRead(type==IncludeType.Local && File.Exists(path) ? path : NativePath.Of(Path.Combine(root,fileName)));
            }
            public void Close(Stream stream) => stream.Dispose();
        }
    }
}
