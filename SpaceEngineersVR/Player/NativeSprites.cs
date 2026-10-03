using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SharpDX.Direct3D11;
using SharpDX.D3DCompiler;
using SharpDX.DXGI;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRageMath;
using Buffer = SharpDX.Direct3D11.Buffer;
using GameImage = SharpDX.Toolkit.Graphics.Image;
using Device = SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Player
{
    internal struct NativeSprite
    {
        public string Path;
        public ShaderResourceView Texture;
        public RectangleF Bounds;
        public Vector4 Tint;
        public Vector4 UV;
        public Vector2 Rounded;
        public bool Projected;
        public bool IgnoreSceneDepth;
        public bool EncodeSrgb;
        public bool Opaque;
        public bool Premultiplied;
        public Vector4 TopLeft, TopRight, BottomLeft, BottomRight;
        public Vector4 Clip0,Clip1,Clip2,Clip3;
        public NativeSprite(string path, RectangleF bounds, Vector4 tint)
        {
            Path=path; Bounds=bounds; Tint=tint; Texture=null; UV=new Vector4(0,0,1,1);
            EncodeSrgb=false; Opaque=false; Premultiplied=false; Projected=IgnoreSceneDepth=false; TopLeft=TopRight=BottomLeft=BottomRight=Vector4.Zero;
            Clip0=Clip1=Clip2=Clip3=Vector4.Zero; Rounded=Vector2.Zero;
        }
    }

    internal static class NativeSprites
    {
        private sealed class Icon
        {
            public Task<GameImage> Loading;
            public Texture2D Texture;
            public ShaderResourceView View;
            public long Used;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Parameters { public Vector4 TopLeft, TopRight, BottomLeft, BottomRight, Tint, UV, DepthTest, Clip0, Clip1, Clip2, Clip3; }
        private static readonly Dictionary<string, Icon> icons = new Dictionary<string, Icon>(StringComparer.OrdinalIgnoreCase);
        private static DeviceContext context;
        private static Device device;
        private static VertexShader vertex;
        private static PixelShader pixel;
        private static Buffer constants;
        private static BlendState blend;
        private static RasterizerState rasterizer;
        private static DepthStencilState depth;
        private static SamplerState sampler;
        private static long serial;
        public static int Revision { get; private set; }
        internal static bool Pending => icons.Values.Any(i=>i.Loading!=null);
        internal static int Loaded => icons.Values.Count(i=>i.View!=null);
        public static string Hud(string name) => @"Textures\GUI\Icons\HUD 2017\" + name + ".png";
        private const string Shader = @"
cbuffer Params : register(b0) { float4 TopLeft; float4 TopRight; float4 BottomLeft; float4 BottomRight; float4 Tint; float4 UV; float4 DepthTest; float4 Clip0; float4 Clip1; float4 Clip2; float4 Clip3; };
Texture2D Icon : register(t0); Texture2D SceneDepth : register(t1); Texture2D HandDepth : register(t2); Texture2D WorldDepth : register(t3); SamplerState Linear : register(s0);
struct P { float4 p:SV_POSITION; float2 uv:TEXCOORD; noperspective float2 ndc:TEXCOORD1; };
P VS(uint id:SV_VertexID) {
    float2 q=float2(id&1,id>>1); P o;
    o.p=id==0 ? TopLeft : id==1 ? TopRight : id==2 ? BottomLeft : BottomRight;
    o.uv=UV.xy+q*UV.zw; o.ndc=o.p.xy/o.p.w; return o;
}
float4 PS(P p):SV_TARGET {
    float3 clipPosition=float3(p.ndc,1);
    if(dot(clipPosition,Clip0.xyz)<0 || dot(clipPosition,Clip1.xyz)<0 || dot(clipPosition,Clip2.xyz)<0 || dot(clipPosition,Clip3.xyz)<0) discard;
    if (DepthTest.x > 0 && SceneDepth.Load(int3(p.p.xy,0)).r > p.p.z + 0.000002) discard;
    if (fmod(floor(DepthTest.w/4),2) > 0) {
        float hand=HandDepth.Load(int3(p.p.xy,0)).r;
        if(hand > 0 && hand >= WorldDepth.Load(int3(p.p.xy,0)).r - 0.000002) discard;
    }
    if (DepthTest.y > 0 && DepthTest.z > 0) {
        float2 uv=(p.uv-UV.xy)/UV.zw;
        float2 corner=max(abs(uv-.5)-(.5-DepthTest.yz),0)/DepthTest.yz;
        if (dot(corner,corner)>1) discard;
    }
    float4 color=Icon.Sample(Linear,p.uv);
    if (DepthTest.w >= 8) color.rgb/=max(color.a,0.00001);
    color*=Tint;
    if (fmod(DepthTest.w,2) > 0)
        color.rgb=lerp(color.rgb*12.92,1.055*pow(max(color.rgb,0),1.0/2.4)-.055,step(.0031308,color.rgb));
    if (fmod(floor(DepthTest.w/2),2) > 0) color.a=1;
    return color;
}";

        private static void Init(Device owner)
        {
            if (context != null) return;
            device=owner;
            using (var vs=ShaderBytecode.Compile(Shader,"VS","vs_4_0"))
            using (var ps=ShaderBytecode.Compile(Shader,"PS","ps_4_0"))
            { vertex=new VertexShader(device,vs); pixel=new PixelShader(device,ps); }
            constants=new Buffer(device,Marshal.SizeOf(typeof(Parameters)),ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
            var blending=new BlendStateDescription();
            blending.RenderTarget[0]=new RenderTargetBlendDescription {
                IsBlendEnabled=true, SourceBlend=BlendOption.SourceAlpha, DestinationBlend=BlendOption.InverseSourceAlpha,
                BlendOperation=BlendOperation.Add, SourceAlphaBlend=BlendOption.One, DestinationAlphaBlend=BlendOption.InverseSourceAlpha,
                AlphaBlendOperation=BlendOperation.Add, RenderTargetWriteMask=ColorWriteMaskFlags.All };
            blend=new BlendState(device,blending);
            rasterizer=new RasterizerState(device,new RasterizerStateDescription { FillMode=FillMode.Solid,CullMode=CullMode.None,IsDepthClipEnabled=true });
            depth=new DepthStencilState(device,new DepthStencilStateDescription { IsDepthEnabled=false });
            sampler=new SamplerState(device,new SamplerStateDescription {
                Filter=Filter.MinMagMipLinear,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,
                AddressW=TextureAddressMode.Clamp,MaximumLod=float.MaxValue });
            context=new DeviceContext(device);
        }

        private static ShaderResourceView Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!icons.TryGetValue(path,out var icon))
            {
                Trim();
                if (icons.Count>=256) return null;
                string file=Path.IsPathRooted(path) ? path : Path.Combine(MyFileSystem.ContentPath,path);
                icon=new Icon { Loading=Task.Run(() => {
                    try { using (var stream=MyFileSystem.OpenRead(file)) return stream == null ? null : GameImage.Load(stream,file); }
                    catch (Exception ex) { Logger.Warning("Native icon unavailable: "+path+" / "+ex.Message); return null; }
                }) };
                icons.Add(path,icon);
            }
            icon.Used=++serial;
            return icon.View;
        }

        public static void Poll()
        {
            // Decode off the render thread; bound GPU uploads to one new icon per frame.
            foreach (var icon in icons.Values)
            {
                if (icon.Loading == null || !icon.Loading.IsCompleted) continue;
                var loading=icon.Loading;
                icon.Loading=null;
                try
                {
                    using (var image=loading.GetAwaiter().GetResult())
                    {
                        if (image != null)
                        {
                            var d=image.Description;
                            if (d.Width>2048 || d.Height>2048 || d.ArraySize!=1 || d.Depth>1) continue;
                            icon.Texture=new Texture2D(device,new Texture2DDescription {
                                Width=d.Width,Height=d.Height,MipLevels=d.MipLevels,ArraySize=1,Format=d.Format,
                                SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource },image.ToDataBox());
                            icon.View=new ShaderResourceView(device,icon.Texture);
                        }
                    }
                }
                catch (Exception ex)
                {
                    icon.View?.Dispose(); icon.Texture?.Dispose(); icon.View=null; icon.Texture=null;
                    Logger.Warning(ex,"Native icon upload skipped");
                }
                Revision++;
                break;
            }
        }

        private static void Trim()
        {
            if (icons.Count<256) return;
            string oldest=null; long used=long.MaxValue;
            foreach (var pair in icons)
                if (pair.Value.Loading==null && pair.Value.Used<used) { oldest=pair.Key; used=pair.Value.Used; }
            if (oldest==null) return;
            icons[oldest].View?.Dispose(); icons[oldest].Texture?.Dispose(); icons.Remove(oldest);
        }

        public static void Draw(Texture2D target, IList<NativeSprite> sprites,ShaderResourceView sceneDepth=null,ShaderResourceView handDepth=null)
        {
            if (sprites.Count==0) return;
            if (context==null) Init(target.Device);
            context.ClearState();
            using (var rtv=new RenderTargetView(device,target))
            {
                context.OutputMerger.SetRenderTargets(rtv);
                context.OutputMerger.SetBlendState(blend);
                context.OutputMerger.SetDepthStencilState(depth);
                context.Rasterizer.State=rasterizer;
                var size=target.Description;
                context.Rasterizer.SetViewport(0,0,size.Width,size.Height);
                context.InputAssembler.PrimitiveTopology=SharpDX.Direct3D.PrimitiveTopology.TriangleStrip;
                context.VertexShader.Set(vertex); context.PixelShader.Set(pixel);
                context.VertexShader.SetConstantBuffer(0,constants); context.PixelShader.SetConstantBuffer(0,constants);
                context.PixelShader.SetSampler(0,sampler);
                context.PixelShader.SetShaderResource(1,sceneDepth);
                context.PixelShader.SetShaderResource(2,handDepth);
                context.PixelShader.SetShaderResource(3,handDepth==null ? null:PhysicalSurface.SceneDepth());
                foreach (var sprite in sprites)
                {
                    var texture=sprite.Texture ?? Get(sprite.Path);
                    if (texture==null) continue;
                    var data=new Parameters { Clip0=sprite.Clip0,Clip1=sprite.Clip1,Clip2=sprite.Clip2,Clip3=sprite.Clip3,Tint=sprite.Tint,UV=sprite.UV,DepthTest=new Vector4(sceneDepth==null || sprite.IgnoreSceneDepth ? 0 : 1,sprite.Rounded.X,sprite.Rounded.Y,(sprite.EncodeSrgb ? 1 : 0)+(sprite.Opaque ? 2 : 0)+(handDepth!=null ? 4:0)+(sprite.Premultiplied ? 8:0)) };
                    if (sprite.Projected)
                    {
                        data.TopLeft=sprite.TopLeft; data.TopRight=sprite.TopRight;
                        data.BottomLeft=sprite.BottomLeft; data.BottomRight=sprite.BottomRight;
                    }
                    else
                    {
                        var b=sprite.Bounds;
                        float left=b.X/size.Width*2-1, top=1-b.Y/size.Height*2;
                        float right=left+b.Width/size.Width*2, bottom=top-b.Height/size.Height*2;
                        data.TopLeft=new Vector4(left,top,0,1); data.TopRight=new Vector4(right,top,0,1);
                        data.BottomLeft=new Vector4(left,bottom,0,1); data.BottomRight=new Vector4(right,bottom,0,1);
                    }
                    context.UpdateSubresource(ref data,constants);
                    context.PixelShader.SetShaderResource(0,texture); context.Draw(4,0);
                }
                using (var commands=context.FinishCommandList(false)) device.ImmediateContext.ExecuteCommandList(commands,true);
            }
        }
    }
}
