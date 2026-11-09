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
    // Camera motion vectors from depth after SE-DLSS Mv.hlsl (MIT): closest of nine depths reprojected through the previous view projection; static world only.
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
};
Texture2D DepthTex : register(t0);
SamplerState PointSamp : register(s0);
static const float2 kClosestOff[8] = { float2(1,0), float2(-1,0), float2(0,1), float2(0,-1), float2(1,1), float2(-1,1), float2(1,-1), float2(-1,-1) };
void VS(uint id : SV_VertexID, out float4 pos : SV_Position, out float2 uv : TEXCOORD0)
{
    uv = float2((id << 1) & 2, id & 2);
    pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}
float2 CameraVelocity(float2 uv, float depth)
{
    float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
    float4 world = mul(float4(ndc, depth, 1.0), InvViewProj);
    world /= max(world.w, 1e-6);
    float4 currClip = mul(world, UnjitteredViewProj);
    currClip /= max(currClip.w, 1e-6);
    float4 prevClip = mul(world, PrevViewProj);
    prevClip /= max(prevClip.w, 1e-6);
    float2 currUv = float2(currClip.x * 0.5 + 0.5, 0.5 - currClip.y * 0.5);
    float2 prevUv = float2(prevClip.x * 0.5 + 0.5, 0.5 - prevClip.y * 0.5);
    return (prevUv - currUv) * RenderSize;
}
float4 PS(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    float closest = DepthTex.SampleLevel(PointSamp, uv, 0).r;
    float2 bestUv = uv;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float2 nuv = uv + kClosestOff[i] * InvRenderSize;
        float nd = DepthTex.SampleLevel(PointSamp, nuv, 0).r;
        if (nd > closest) { closest = nd; bestUv = nuv; }
    }
    return float4(CameraVelocity(bestUv, closest), 0, 1);
}
float PSDepth(float4 pos : SV_Position, float2 uv : TEXCOORD0) : SV_Target
{
    return DepthTex.SampleLevel(PointSamp, uv, 0).r;
}";
        [StructLayout(LayoutKind.Sequential)]
        private struct Constants { public Matrix InverseJittered,Unjittered,Previous; public Vector2 Size,InverseSize; }
        private static Device device;
        private static VertexShader vertex;
        private static PixelShader pixel,depthPixel;
        private static Buffer constants;
        private static SamplerState sampler;
        private static Texture2D texture;
        private static RenderTargetView target;

        // Point resample of a depth texture to the eye target size for the overlays' pixel depth tests.
        internal sealed class Resample : IDisposable
        {
            private Texture2D texture;
            private RenderTargetView target;
            internal ShaderResourceView View { get; private set; }
            internal void Run(Device current,ShaderResourceView depth,int width,int height)
            {
                Ensure(current,0,0);
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

        internal static Texture2D Generate(Device current,ShaderResourceView depth,int width,int height,Matrix inverseJittered,Matrix unjittered,Matrix previous)
        {
            Ensure(current,width,height);
            var data=new Constants { InverseJittered=inverseJittered,Unjittered=unjittered,Previous=previous,Size=new Vector2(width,height),InverseSize=new Vector2(1f/width,1f/height) };
            current.ImmediateContext.UpdateSubresource(ref data,constants);
            Draw(current.ImmediateContext,target,width,height,pixel,depth);
            return texture;
        }

        // Fullscreen pass with the pipeline state the engine may have left behind reset to defaults.
        private static void Draw(DeviceContext context,RenderTargetView rtv,int width,int height,PixelShader shader,ShaderResourceView source)
        {
            context.OutputMerger.SetTargets((DepthStencilView)null,rtv);
            context.OutputMerger.SetBlendState(null);
            context.OutputMerger.SetDepthStencilState(null);
            context.Rasterizer.State=null;
            context.Rasterizer.SetViewport(0,0,width,height);
            context.InputAssembler.InputLayout=null;
            context.InputAssembler.PrimitiveTopology=PrimitiveTopology.TriangleList;
            context.VertexShader.Set(vertex);
            context.VertexShader.SetConstantBuffer(0,constants);
            context.PixelShader.Set(shader);
            context.PixelShader.SetConstantBuffer(0,constants);
            context.PixelShader.SetSampler(0,sampler);
            context.PixelShader.SetShaderResource(0,source);
            context.Draw(3,0);
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
                constants=new Buffer(current,new BufferDescription(Marshal.SizeOf(typeof(Constants)),ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0));
                sampler=new SamplerState(current,new SamplerStateDescription { Filter=Filter.MinMagMipPoint,AddressU=TextureAddressMode.Clamp,AddressV=TextureAddressMode.Clamp,AddressW=TextureAddressMode.Clamp });
                device=current;
            }
            if(width==0 || (texture!=null && texture.Description.Width==width && texture.Description.Height==height)) return;
            target?.Dispose(); texture?.Dispose();
            texture=new Texture2D(current,new Texture2DDescription
            {
                Width=width,Height=height,MipLevels=1,ArraySize=1,Format=Format.R16G16_Float,SampleDescription=new SampleDescription(1,0),
                Usage=ResourceUsage.Default,BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource
            });
            target=new RenderTargetView(current,texture);
        }

        internal static void Dispose()
        {
            target?.Dispose(); texture?.Dispose(); sampler?.Dispose(); constants?.Dispose(); pixel?.Dispose(); depthPixel?.Dispose(); vertex?.Dispose();
            target=null; texture=null; sampler=null; constants=null; pixel=null; depthPixel=null; vertex=null; device=null;
        }
    }
}
#endif
