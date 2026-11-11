#if !LINUX
using System;
using System.Runtime.InteropServices;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRageMath;
using Buffer=SharpDX.Direct3D11.Buffer;
using Device=SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Player
{
    // Motion vectors from depth after SE-DLSS Mv.hlsl (MIT): closest of nine depths reprojected through the previous view projection, after its object's own motion inside a DlssObjects box.
    internal static class DlssMotion
    {
        private const string Shader=@"
#pragma pack_matrix(row_major)
cbuffer Constants : register(b0)
{
    float4x4 InvViewProj;
    float4x4 UnjitteredViewProj;
    float4x4 PrevViewProj;
    float2 RenderSize;
    float2 InvRenderSize;
    float2 Offset;
    float2 Pad;
};
struct Box { float4x4 ToClip; float4x4 ToLocal; float3 Min; uint Index; float3 Max; float Flip; };
Texture2D DepthTex : register(t0);
StructuredBuffer<Box> Boxes : register(t1);
// A struct, because pack_matrix does not reach a bare matrix element type.
struct Motion { float4x4 ToPrevious; };
StructuredBuffer<Motion> Reproject : register(t2);
Texture2D<uint> IndexTex : register(t3);
SamplerState PointSamp : register(s0);
// Triangles wind towards the inside of the box.
static const uint kCube[36] = { 0,2,6, 0,6,4, 1,5,7, 1,7,3, 0,4,5, 0,5,1, 2,3,7, 2,7,6, 0,1,3, 0,3,2, 4,6,7, 4,7,5 };
static const float2 kClosestOff[8] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1), float2(1,1), float2(-1,1), float2(1,-1), float2(-1,-1) };
void VS(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv = float2((id << 1) & 2, id & 2);
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}
float4 Unproject(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(float4(ndc, depth, 1.0), InvViewProj);
    return world / max(world.w, 1e-6);
}
float2 Velocity(float4 world, float4 previous)
{
    float4 currClip = mul(world, UnjitteredViewProj);
    currClip /= max(currClip.w, 1e-6);
    float4 prevClip = mul(previous, PrevViewProj);
    prevClip /= max(prevClip.w, 1e-6);
    float2 currUv = float2(currClip.x * 0.5 + 0.5, 0.5 - currClip.y * 0.5);
    float2 prevUv = float2(prevClip.x * 0.5 + 0.5, 0.5 - prevClip.y * 0.5);
    return (prevUv - currUv) * RenderSize;
}
float Closest(inout float2 uv)
{
    float closest = DepthTex.SampleLevel(PointSamp, uv, 0).r;
    float2 centre = uv;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float2 nuv = centre + kClosestOff[i] * InvRenderSize;
        float nd = DepthTex.SampleLevel(PointSamp, nuv, 0).r;
        if (nd > closest) { closest = nd; uv = nuv; }
    }
    return closest;
}
float4 PS(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float closest = Closest(uv);
    float4 world = Unproject(uv, closest);
    return float4(Velocity(world, world), 0, 1);
}
float4 PSObjects(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float closest = Closest(uv);
    float4 world = Unproject(uv, closest);
    uint index = IndexTex.Load(int3(uv * RenderSize, 0));
    return float4(Velocity(world, mul(world, Reproject[index].ToPrevious)), 0, 1);
}
void VSBox(uint id : SV_VertexID, uint instance : SV_InstanceID, out float4 pos : SV_Position, out nointerpolation uint box : BOX)
{
    Box b = Boxes[instance];
    uint corner = kCube[id];
    pos = mul(float4(lerp(b.Min, b.Max, float3(corner & 1, (corner >> 1) & 1, (corner >> 2) & 1)), 1), b.ToClip);
    box = instance;
}
// Far faces only, so a camera inside the box still covers it; the scene surface at the pixel decides membership.
uint PSBox(float4 pos : SV_Position, nointerpolation uint box : BOX, bool front : SV_IsFrontFace) : SV_Target
{
    Box b = Boxes[box];
    if (front != (b.Flip < 0)) discard;
    float depth = DepthTex.Load(int3(pos.xy, 0)).r;
    if (depth <= 0) discard;
    float2 uv = pos.xy * InvRenderSize;
    float4 placed = mul(float4(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0, depth, 1.0), b.ToLocal);
    float3 local = placed.xyz / placed.w;
    if (any(local < b.Min) || any(local > b.Max)) discard;
    return b.Index;
}
float PSDepth(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float2 p = uv * RenderSize + Offset - 0.5;
    float2 corner = floor(p);
    float2 f = p - corner;
    int2 top = (int2)RenderSize - 1;
    int2 i = (int2)corner;
    float a = DepthTex.Load(int3(clamp(i, 0, top), 0)).r;
    float b = DepthTex.Load(int3(clamp(i + int2(1, 0), 0, top), 0)).r;
    float c = DepthTex.Load(int3(clamp(i + int2(0, 1), 0, top), 0)).r;
    float d = DepthTex.Load(int3(clamp(i + int2(1, 1), 0, top), 0)).r;
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}
float PSWrite(float4 pos : SV_Position) : SV_Depth
{
    return DepthTex.Load(int3(pos.xy, 0)).r;
}";
        [StructLayout(LayoutKind.Sequential)]
        private struct Constants { public Matrix InverseJittered,Unjittered,Previous; public Vector2 Size,InverseSize,Offset,Pad; }
        private static Device device;
        private static VertexShader vertex,boxVertex;
        private static PixelShader pixel,depthPixel,writePixel,objectsPixel,boxPixel;
        private static DepthStencilState writeAlways;
        private static RasterizerState bothFaces;
        private static Buffer constants,boxes,transforms;
        private static ShaderResourceView boxesView,transformsView,indexView;
        private static SamplerState sampler;
        private static Texture2D texture,indexTexture;
        private static RenderTargetView target,indexTarget;
#if SEVR_DIAGNOSTICS
        internal static Texture2D Index => indexTexture;
#endif

        // Jittered render depth at eye target size, interpolated at the jitter-shifted position.
        internal sealed class Resample : IDisposable
        {
            private Texture2D texture;
            private RenderTargetView target;
            internal ShaderResourceView View { get; private set; }
            internal void Run(Device current,ShaderResourceView depth,int width,int height,Vector2I source,Vector2 offset)
            {
                Ensure(current,0,0);
                var data=new Constants { Size=new Vector2(source.X,source.Y),Offset=offset };
                current.ImmediateContext.UpdateSubresource(ref data,constants);
                if(texture==null || texture.Device.NativePointer!=current.NativePointer || texture.Description.Width!=width || texture.Description.Height!=height)
                {
                    Dispose();
                    texture=new Texture2D(current,new Texture2DDescription
                    {
                        Width=width,Height=height,MipLevels=1,ArraySize=1,Format=Format.R32_Float,SampleDescription=new SampleDescription(1,0),
                        Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource
                    });
                    target=new RenderTargetView(current,texture);
                    View=new ShaderResourceView(current,texture);
                }
                Draw(current.ImmediateContext,target,width,height,depthPixel,depth);
            }
            public void Dispose() { View?.Dispose(); target?.Dispose(); texture?.Dispose(); View=null; target=null; texture=null; }
        }
        private static readonly ShaderResourceView[] nullViews=new ShaderResourceView[8];

        internal static void WriteDepth(Device current,ShaderResourceView depth,DepthStencilView output,int width,int height)
        {
            Ensure(current,0,0);
            var context=current.ImmediateContext;
            context.ClearDepthStencilView(output,DepthStencilClearFlags.Stencil,1,0);
            context.OutputMerger.SetTargets(output,(RenderTargetView)null);
            Draw(context,null,width,height,writePixel,depth,writeAlways);
        }

        internal static Texture2D Generate(Device current,ShaderResourceView depth,int width,int height,Matrix inverseJittered,Matrix unjittered,Matrix previous,bool objects=false)
        {
            Ensure(current,width,height);
            var context=current.ImmediateContext;
            var data=new Constants { InverseJittered=inverseJittered,Unjittered=unjittered,Previous=previous,Size=new Vector2(width,height),InverseSize=new Vector2(1f/width,1f/height) };
            context.UpdateSubresource(ref data,constants);
            if(!objects || DlssObjects.BoxCount==0) { Draw(context,target,width,height,pixel,depth); return texture; }
            Write(context,boxes,DlssObjects.Boxes,DlssObjects.BoxCount);
            Write(context,transforms,DlssObjects.Transforms,DlssObjects.TransformCount);
            context.ClearRenderTargetView(indexTarget,new SharpDX.Mathematics.Interop.RawColor4(0,0,0,0));
            context.VertexShader.SetShaderResource(1,boxesView);
            context.PixelShader.SetShaderResource(1,boxesView);
            Draw(context,indexTarget,width,height,boxPixel,depth,null,boxVertex,bothFaces,DlssObjects.BoxCount);
            context.PixelShader.SetShaderResource(2,transformsView);
            context.PixelShader.SetShaderResource(3,indexView);
            Draw(context,target,width,height,objectsPixel,depth);
            return texture;
        }
        private static void Write<T>(DeviceContext context,Buffer buffer,T[] values,int count) where T : struct
        {
            var mapped=context.MapSubresource(buffer,0,MapMode.WriteDiscard,SharpDX.Direct3D11.MapFlags.None);
            SharpDX.Utilities.Write(mapped.DataPointer,values,0,count);
            context.UnmapSubresource(buffer,0);
        }

        // One pass with the pipeline state the engine may have left behind reset to defaults.
        private static void Draw(DeviceContext context,RenderTargetView rtv,int width,int height,PixelShader shader,ShaderResourceView source,DepthStencilState depthState=null,
            VertexShader boxShader=null,RasterizerState rasterizer=null,int boxCount=0)
        {
            if(rtv!=null) context.OutputMerger.SetTargets((DepthStencilView)null,rtv);
            context.OutputMerger.SetBlendState(null);
            context.OutputMerger.SetDepthStencilState(depthState);
            context.Rasterizer.State=rasterizer;
            context.Rasterizer.SetViewport(0,0,width,height);
            context.InputAssembler.InputLayout=null;
            context.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;
            context.VertexShader.Set(boxShader ?? vertex);
            context.VertexShader.SetConstantBuffer(0,constants);
            context.PixelShader.Set(shader);
            context.PixelShader.SetConstantBuffer(0,constants);
            context.PixelShader.SetSampler(0,sampler);
            context.PixelShader.SetShaderResource(0,source);
            if(boxShader==null) context.Draw(3,0);
            else context.DrawInstanced(36,boxCount,0,0);
            Unbind(context);
        }

        // NGX binds its own views; anything still bound as a target would be a read/write hazard.
        internal static void Unbind(DeviceContext context)
        {
            context.OutputMerger.ResetTargets();
            context.PixelShader.SetShaderResources(0,nullViews);
            context.VertexShader.SetShaderResources(0,nullViews);
            context.ComputeShader.SetShaderResources(0,nullViews);
        }

        private static void Ensure(Device current,int width,int height)
        {
            if(device==null || device.NativePointer!=current.NativePointer)
            {
                Dispose();
                using(var code=ShaderBytecode.Compile(Shader,"VS","vs_5_0")) vertex=new VertexShader(current,code);
                using(var code=ShaderBytecode.Compile(Shader,"PS","ps_5_0")) pixel=new PixelShader(current,code);
                using(var code=ShaderBytecode.Compile(Shader,"PSDepth","ps_5_0")) depthPixel=new PixelShader(current,code);
                using(var code=ShaderBytecode.Compile(Shader,"PSWrite","ps_5_0")) writePixel=new PixelShader(current,code);
                using(var code=ShaderBytecode.Compile(Shader,"PSObjects","ps_5_0")) objectsPixel=new PixelShader(current,code);
                using(var code=ShaderBytecode.Compile(Shader,"VSBox","vs_5_0")) boxVertex=new VertexShader(current,code);
                using(var code=ShaderBytecode.Compile(Shader,"PSBox","ps_5_0")) boxPixel=new PixelShader(current,code);
                // No depth clip: far faces behind the far plane still mark their pixels.
                bothFaces=new RasterizerState(current,new RasterizerStateDescription { CullMode=CullMode.None,FillMode=FillMode.Solid,IsDepthClipEnabled=false });
                boxes=Structured(current,Marshal.SizeOf(typeof(DlssObjects.Box)),DlssObjects.MaxBoxes);
                transforms=Structured(current,Marshal.SizeOf(typeof(Matrix)),DlssObjects.MaxBoxes+1);
                boxesView=new ShaderResourceView(current,boxes); transformsView=new ShaderResourceView(current,transforms);
                writeAlways=new DepthStencilState(current,new DepthStencilStateDescription { IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.All,DepthComparison=Comparison.Always });
                constants=new Buffer(current,new BufferDescription(Marshal.SizeOf(typeof(Constants)),ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0));
                sampler=new SamplerState(current,new SamplerStateDescription { Filter=Filter.MinMagMipPoint,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp });
                device=current;
            }
            if(width==0 || (texture!=null && texture.Description.Width==width && texture.Description.Height==height)) return;
            target?.Dispose(); texture?.Dispose(); indexView?.Dispose(); indexTarget?.Dispose(); indexTexture?.Dispose();
            texture=Target(current,width,height,Format.R16G16_Float);
            target=new RenderTargetView(current,texture);
            indexTexture=Target(current,width,height,Format.R8_UInt);
            indexTarget=new RenderTargetView(current,indexTexture);
            indexView=new ShaderResourceView(current,indexTexture);
        }
        private static Texture2D Target(Device current,int width,int height,Format format) => new Texture2D(current,new Texture2DDescription
        {
            Width=width,Height=height,MipLevels=1,ArraySize=1,Format=format,SampleDescription=new SampleDescription(1,0),
            Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource
        });
        private static Buffer Structured(Device current,int stride,int count) =>
            new Buffer(current,new BufferDescription(stride*count,ResourceUsage.Dynamic,BindFlags.ShaderResource,CpuAccessFlags.Write,ResourceOptionFlags.BufferStructured,stride));

        internal static void Dispose()
        {
            foreach(var resource in new IDisposable[] {target,texture,indexView,indexTarget,indexTexture,boxesView,transformsView,boxes,transforms,sampler,constants,pixel,depthPixel,writePixel,objectsPixel,boxPixel,writeAlways,bothFaces,vertex,boxVertex})
                resource?.Dispose();
            target=null; texture=null; sampler=null; constants=null; pixel=null; depthPixel=null; writePixel=null; writeAlways=null; vertex=null; device=null;
            indexTarget=null; indexTexture=null; indexView=null; boxesView=null; transformsView=null; boxes=null; transforms=null; objectsPixel=null; boxPixel=null; bothFaces=null; boxVertex=null;
        }
    }
}
#endif
