using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using HarmonyLib;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Player;
using VRageMath;
using Buffer=SharpDX.Direct3D11.Buffer;
using Device=SharpDX.Direct3D11.Device;
using GameImage=SharpDX.Toolkit.Graphics.Image;

namespace SpaceEngineersVR.Diagnostics
{
    public static class StereoSmokeTests
    {
        private const int Size=128;
        private const string Vertex=@"
cbuffer Smoke : register(b1) { float4 Color; float4 UV; float4 Extras; };
struct P { float4 p:SV_POSITION; float3 uv:TEXCOORD0; float3 extras:TEXCOORD1; float4 color:COLOR0; };
P VS(uint id:SV_VertexID) {
    float2 q=float2(id&1,id>>1); P o;
    o.p=float4(q*float2(2,-2)+float2(-1,1),.006,1);
    o.uv=float3(UV.xy+q*UV.zw,0); o.extras=Extras.xyz; o.color=Color; return o;
}";
        public static void Export(string game,string output,Action<string> log)
        {
            UiTests.Initialize(game,Path.Combine(output,"data"));
            using(var device=new Device(DriverType.Warp)) Run(device,Path.GetFullPath(Path.Combine(game,"..","Content","Shaders")),log,output);
        }
        internal static void Run(Device device,string shaders,Action<string> log,string output=null)
        {
            string content=Path.GetDirectoryName(shaders),local=Path.Combine(shaders,"Transparent","GPUParticles");
            string source=File.ReadAllText(Path.Combine(local,"Render.hlsl"));
            var definitions=XDocument.Load(Path.Combine(content,"Data","Particles_B.sbc"));
            output=output ?? Path.Combine(VRage.FileSystem.MyFileSystem.UserDataPath,"stereo-smoke");
            Directory.CreateDirectory(output);
            using(var includes=new HiddenAreaMask.Includes(shaders,local))
            using(var psCode=ShaderBytecode.Compile(source,"__pixel_shader","ps_5_0",ShaderFlags.OptimizationLevel3,EffectFlags.None,
                new[] {new ShaderMacro("STREAKS",null),new ShaderMacro("LIT_PARTICLE",null)},includes))
            using(var vsCode=ShaderBytecode.Compile(Vertex,"VS","vs_5_0"))
            using(var ps=new PixelShader(device,psCode))
            using(var vs=new VertexShader(device,vsCode))
            using(var image=GameImage.Load(Path.Combine(content,"Textures","Particles","Atlas_D_01_ca.dds")))
            using(var atlas=new Texture2D(device,new Texture2DDescription {Width=image.Description.Width,Height=image.Description.Height,
                ArraySize=1,MipLevels=image.Description.MipLevels,Format=image.Description.Format,SampleDescription=new SampleDescription(1,0),
                Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource},image.ToDataBox()))
            using(var atlasView=new ShaderResourceView(device,atlas,new ShaderResourceViewDescription {Format=image.Description.Format,
                Dimension=ShaderResourceViewDimension.Texture2DArray,Texture2DArray=new ShaderResourceViewDescription.Texture2DArrayResource {
                    ArraySize=1,MipLevels=image.Description.MipLevels}}))
            using(var target=new Texture2D(device,new Texture2DDescription {Width=Size,Height=Size,ArraySize=1,MipLevels=1,
                Format=Format.R8G8B8A8_UNorm,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget}))
            using(var rtv=new RenderTargetView(device,target))
            using(var raster=new RasterizerState(device,new RasterizerStateDescription {CullMode=CullMode.None,FillMode=FillMode.Solid,IsDepthClipEnabled=true}))
            using(var sampler=new SamplerState(device,new SamplerStateDescription {Filter=Filter.MinMagMipLinear,
                AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue}))
            using(var blend=Premultiplied(device))
            using(var frame=Frame(device))
            {
                var rc=device.ImmediateContext;
                rc.ClearState(); rc.Rasterizer.State=raster; rc.Rasterizer.SetViewport(0,0,Size,Size);
                rc.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleStrip;
                rc.VertexShader.Set(vs); rc.PixelShader.Set(ps); rc.PixelShader.SetConstantBuffer(0,frame);
                rc.PixelShader.SetSampler(0,sampler); rc.PixelShader.SetShaderResource(1,atlasView);
                rc.OutputMerger.SetTargets(rtv); rc.OutputMerger.SetBlendState(blend);
                foreach(string effect in new[] {"Smoke_DrillDust","BlockDestroyedExplosion_Large"})
                {
                    var definition=definitions.Descendants("ParticleEffect").Single(e=>e.Element("Id").Attribute("Subtype").Value==effect);
                    var smoke=definition.Descendants("ParticleGeneration").First(g=>g.Attribute("Name").Value==(effect=="Smoke_DrillDust" ? "Smoke" : "SmokeDarker"));
                    XElement Property(string name) => smoke.Descendants("Property").Single(p=>p.Attribute("Name").Value==name);
                    float Value(XElement e) => float.Parse(e.Value,CultureInfo.InvariantCulture);
                    var color=Property("Color").Descendants("ValueVector4").First(v=>Value(v.Element("W"))>.01);
                    var dimensions=Property("Array size").Element("ValueVector3");
                    int index=(int)Property("Array offset").Element("ValueInt")+(int)Property("Array modulo").Element("ValueInt")/2;
                    float x=Value(dimensions.Element("X")),y=Value(dimensions.Element("Y"));
                    float soft=Value(Property("Soft particle distance scale").Element("ValueFloat"));
                    var values=new[] {new Vector4(Value(color.Element("X")),Value(color.Element("Y")),Value(color.Element("Z")),Value(color.Element("W"))),
                        new Vector4(index%(int)x/x,index/(int)x/y,1/x,1/y),new Vector4(1,soft,0,0)};
                    using(var constants=Buffer.Create(device,BindFlags.ConstantBuffer,values))
                    {
                        rc.VertexShader.SetConstantBuffer(1,constants);
                        // The left eye fades smoke intersecting a surface; an unbound right-eye depth returns zero.
                        var left=Draw(.006f,"before-"+effect+"-left");
                        var right=Draw(null,"before-"+effect+"-right");
                        Require(Background(left),"Surface-intersecting smoke did not disappear with its real depth");
                        Require(!Background(right),"Missing depth did not reproduce extra smoke");
                        var fixedLeft=Draw(.003f,"after-"+effect+"-left");
                        var fixedRight=Draw(.003f,"after-"+effect+"-right");
                        Require(fixedLeft.SequenceEqual(fixedRight) && !Background(fixedLeft),"Visible smoke pixels disagree with correct per-view depth");
                        var occludedLeft=Draw(.01f,null); var occludedRight=Draw(.01f,null);
                        Require(occludedLeft.SequenceEqual(occludedRight) && Background(occludedLeft),"Correct depth failed to clip occluded smoke");
                    }
                }
                byte[] Draw(float? depth,string name)
                {
                    Texture2D texture=null; ShaderResourceView view=null;
                    try
                    {
                        if(depth.HasValue)
                        {
                            texture=new Texture2D(device,new Texture2DDescription {Width=Size,Height=Size,ArraySize=1,MipLevels=1,
                                Format=Format.R32_Float,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource});
                            rc.UpdateSubresource(Enumerable.Repeat(depth.Value,Size*Size).ToArray(),texture,0,Size*4);
                            view=new ShaderResourceView(device,texture);
                        }
                        rc.PixelShader.SetShaderResource(0,view); rc.ClearRenderTargetView(rtv,new RawColor4(.75f,.75f,.75f,1)); rc.Draw(4,0);
                        var pixels=HiddenAreaTests.Read<byte>(device,target);
                        if(name!=null) UiTests.Save(target,Path.Combine(output,name+".png"));
                        return pixels;
                    }
                    finally { rc.PixelShader.SetShaderResource(0,null); view?.Dispose(); texture?.Dispose(); }
                }
                rc.ClearState();
            }
            log("PASS native smoke pixel shader: mining/explosion atlas and colors reproduce extra right-eye smoke with missing depth; visible, intersecting and occluded smoke agree with per-view depth");
        }
        private static bool Background(byte[] pixels) => pixels.Where((v,i)=>i%4!=3).All(v=>v==191);
        private static BlendState Premultiplied(Device device)
        {
            var description=new BlendStateDescription();
            description.RenderTarget[0]=new RenderTargetBlendDescription {IsBlendEnabled=true,SourceBlend=BlendOption.One,
                DestinationBlend=BlendOption.InverseSourceAlpha,BlendOperation=BlendOperation.Add,SourceAlphaBlend=BlendOption.One,
                DestinationAlphaBlend=BlendOption.InverseSourceAlpha,AlphaBlendOperation=BlendOperation.Add,RenderTargetWriteMask=ColorWriteMaskFlags.All};
            return new BlendState(device,description);
        }
        private static Buffer Frame(Device device)
        {
            var type=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyCommon"),"FrameConstantsData").FieldType;
            object data=Activator.CreateInstance(type);
            var environment=AccessTools.Field(type,"Environment"); object env=environment.GetValue(data);
            AccessTools.Field(env.GetType(),"Projection").SetValue(env,Matrix.Transpose((Matrix)VrMath.Projection(-1,1,-1,1,.03)));
            environment.SetValue(data,env);
            int size=Marshal.SizeOf(type); var bytes=new byte[size]; var handle=GCHandle.Alloc(bytes,GCHandleType.Pinned);
            try { Marshal.StructureToPtr(data,handle.AddrOfPinnedObject(),false); }
            finally { handle.Free(); }
            return Buffer.Create(device,BindFlags.ConstantBuffer,bytes);
        }
        private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
