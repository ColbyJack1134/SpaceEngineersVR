using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using SpaceEngineersVR.Wrappers;
using Valve.VR;
using VRageMath;
using Buffer=SharpDX.Direct3D11.Buffer;

namespace SpaceEngineersVR.Player
{
    // The menu has no game world. Render driver meshes directly in standing tracking
    // space, using the SAME predicted pose batch as the HMD (no game-camera offset).
    internal static class MenuHands
    {
        [StructLayout(LayoutKind.Sequential)] private struct Vertex { public Vector3 Position,Normal; }
        [StructLayout(LayoutKind.Sequential)] private struct Constants { public Matrix Transform; public Vector4 Color; }
        private sealed class Mesh : IDisposable
        {
            public Buffer Vertices,Indices; public int Count;
            public void Dispose() { Vertices?.Dispose(); Indices?.Dispose(); }
        }
        private sealed class Model { public Mesh Mesh; public int Retry; public bool Failed; }
        private static readonly Dictionary<string,Model> models=new Dictionary<string,Model>();
        private static DeviceContext context;
        private static VertexShader vertexShader;
        private static PixelShader pixelShader;
        private static InputLayout layout;
        private static Buffer constants;
        private static RasterizerState rasterizer;
        private static DepthStencilState depthState;
        private static Mesh box;
        private static readonly Texture2D[] eyes=new Texture2D[2];
        private static readonly RenderTargetView[] targets=new RenderTargetView[2];
        private static Texture2D depth;
        private static DepthStencilView depthView;
        public static ShaderResourceView Depth { get; private set; }
        private static bool failed;
        public static bool Available => !failed;
        private const string Shader=@"
cbuffer Params : register(b0) { row_major float4x4 Transform; float4 Color; };
struct V { float3 p:POSITION; float3 n:NORMAL; };
struct P { float4 p:SV_POSITION; float4 c:COLOR; };
P VS(V v) { P o; o.p=mul(float4(v.p,1),Transform); o.c=float4(Color.rgb*(0.45+0.55*abs(dot(v.n,normalize(float3(0.3,0.8,0.5))))),1); return o; }
float4 PS(P p):SV_TARGET { return p.c; }";
        private static Mesh CreateMesh(Vertex[] vertices,ushort[] indices)
        {
            var device=MyRender11.DeviceInstance;
            return new Mesh { Vertices=Buffer.Create(device,BindFlags.VertexBuffer,vertices),
                Indices=Buffer.Create(device,BindFlags.IndexBuffer,indices),Count=indices.Length };
        }
        private static void Init()
        {
            if(context!=null) return;
            var device=MyRender11.DeviceInstance;
            using(var vs=ShaderBytecode.Compile(Shader,"VS","vs_4_0"))
            using(var ps=ShaderBytecode.Compile(Shader,"PS","ps_4_0"))
            {
                vertexShader=new VertexShader(device,vs);
                pixelShader=new PixelShader(device,ps);
                layout=new InputLayout(device,vs,new[] {
                    new InputElement("POSITION",0,Format.R32G32B32_Float,0,0),
                    new InputElement("NORMAL",0,Format.R32G32B32_Float,12,0) });
            }
            constants=new Buffer(device,80,ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
            rasterizer=new RasterizerState(device,new RasterizerStateDescription {
                FillMode=FillMode.Solid,CullMode=CullMode.None,IsDepthClipEnabled=true });
            depthState=new DepthStencilState(device,new DepthStencilStateDescription {
                IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.All,DepthComparison=Comparison.GreaterEqual });
            var vertices=new Vertex[8];
            for(int i=0;i<8;i++) vertices[i]=new Vertex { Position=new Vector3((i&1)==0 ? -0.5f:0.5f,(i&2)==0 ? -0.5f:0.5f,(i&4)==0 ? -0.5f:0.5f),Normal=Vector3.Up };
            box=CreateMesh(vertices,new ushort[] {0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3});
            context=new DeviceContext(device);
        }
        private static Mesh GetModel(Controller hand)
        {
            var name=new StringBuilder(1024);
            var error=ETrackedPropertyError.TrackedProp_Success;
            OpenVR.System.GetStringTrackedDeviceProperty(hand.deviceId,ETrackedDeviceProperty.Prop_RenderModelName_String,name,1024,ref error);
            if(error!=ETrackedPropertyError.TrackedProp_Success) return null;
            string key=name.ToString();
            if(!models.TryGetValue(key,out Model model)) models[key]=model=new Model();
            if(model.Mesh!=null || model.Failed || model.Retry++%30!=0) return model.Mesh;
            IntPtr ptr=IntPtr.Zero;
            var result=OpenVR.RenderModels.LoadRenderModel_Async(key,ref ptr);
            if(result==EVRRenderModelError.Loading) return null;
            if(result!=EVRRenderModelError.None) { model.Failed=true; Logger.Warning("Menu controller mesh unavailable: "+key+" / "+result); return null; }
            try
            {
                var native=(RenderModel_t)Marshal.PtrToStructure(ptr,typeof(RenderModel_t));
                var vertices=new Vertex[native.unVertexCount];
                int stride=Marshal.SizeOf(typeof(RenderModel_Vertex_t));
                for(int i=0;i<vertices.Length;i++)
                {
                    var v=(RenderModel_Vertex_t)Marshal.PtrToStructure(IntPtr.Add(native.rVertexData,i*stride),typeof(RenderModel_Vertex_t));
                    vertices[i]=new Vertex { Position=v.vPosition.ToVector(),Normal=v.vNormal.ToVector() };
                }
                var signed=new short[native.unTriangleCount*3]; Marshal.Copy(native.rIndexData,signed,0,signed.Length);
                var indices=new ushort[signed.Length]; System.Buffer.BlockCopy(signed,0,indices,0,signed.Length*2);
                model.Mesh=CreateMesh(vertices,indices);
                Logger.Info("Menu controller mesh loaded at native tracking origin: "+key);
            }
            finally { OpenVR.RenderModels.FreeRenderModel(ptr); }
            return model.Mesh;
        }
        private static void Resize(Vector2I size)
        {
            if(eyes[0]!=null && eyes[0].Description.Width==size.X && eyes[0].Description.Height==size.Y) return;
            for(int i=0;i<2;i++) { targets[i]?.Dispose(); eyes[i]?.Dispose(); }
            Depth?.Dispose(); depthView?.Dispose(); depth?.Dispose();
            var desc=new Texture2DDescription { Width=size.X,Height=size.Y,MipLevels=1,ArraySize=1,
                Format=Format.R8G8B8A8_UNorm,SampleDescription=new SampleDescription(1,0),BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource };
            for(int i=0;i<2;i++) { eyes[i]=new Texture2D(MyRender11.DeviceInstance,desc); targets[i]=new RenderTargetView(MyRender11.DeviceInstance,eyes[i]); }
            depth=CreateDepth(MyRender11.DeviceInstance,size,out depthView,out var readable);
            Depth=readable;
        }
        internal static Texture2D CreateDepth(SharpDX.Direct3D11.Device device,Vector2I size,out DepthStencilView target,out ShaderResourceView readable)
        {
            var texture=new Texture2D(device,new Texture2DDescription { Width=size.X,Height=size.Y,MipLevels=1,ArraySize=1,
                Format=Format.R32_Typeless,SampleDescription=new SampleDescription(1,0),BindFlags=BindFlags.DepthStencil|BindFlags.ShaderResource });
            target=new DepthStencilView(device,texture,new DepthStencilViewDescription { Format=Format.D32_Float,Dimension=DepthStencilViewDimension.Texture2D });
            readable=new ShaderResourceView(device,texture,new ShaderResourceViewDescription { Format=Format.R32_Float,
                Dimension=SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,Texture2D=new ShaderResourceViewDescription.Texture2DResource { MipLevels=1 } });
            return texture;
        }
        private static void DrawMesh(Mesh mesh,Matrix world,Matrix viewProjection,Vector4 color)
        {
            var data=new Constants { Transform=world*viewProjection,Color=color };
            context.UpdateSubresource(ref data,constants);
            context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(mesh.Vertices,24,0));
            context.InputAssembler.SetIndexBuffer(mesh.Indices,Format.R16_UInt,0);
            context.DrawIndexed(mesh.Count,0,0);
        }
        public static void DrawInWorld(Texture2D texture, EVREye eye)
        {
            if (failed) return;
            try
            {
                Init(); var size = MyRender11.Resolution; Resize(size);
                var hands = new[] { Player.HandL, Player.HandR };
                var meshes = new Mesh[2];
                for (int h = 0; h < 2; h++) if (hands[h].renderPose.isTracked) meshes[h] = GetModel(hands[h]);
                Components.VRGUIManager.DrawStereo(texture,eye);
                using (var target = new RenderTargetView(MyRender11.DeviceInstance, texture))
                    DrawEye(target, eye, size, hands, meshes, false);
            }
            catch (Exception ex) { failed = true; Logger.Warning(ex, "World-menu controller rendering disabled"); }
        }

        private static void DrawEye(RenderTargetView target, EVREye eye, Vector2I size, Controller[] hands, Mesh[] meshes, bool clear)
        {
            context.ClearState();
            if (clear) context.ClearRenderTargetView(target,new RawColor4(0,0,0,1));
            context.ClearDepthStencilView(depthView,DepthStencilClearFlags.Depth,0,0);
            context.OutputMerger.SetRenderTargets(depthView,target);
            context.OutputMerger.SetDepthStencilState(depthState);
            context.Rasterizer.State=rasterizer;
            context.Rasterizer.SetViewport(0,0,size.X,size.Y);
            context.InputAssembler.InputLayout=layout;
            context.InputAssembler.PrimitiveTopology=SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.VertexShader.Set(vertexShader); context.PixelShader.Set(pixelShader);
            context.VertexShader.SetConstantBuffer(0,constants);
            var which=eye;
            Matrix view=Matrix.Invert(OpenVR.System.GetEyeToHeadTransform(which).ToMatrix()*Player.Headset.renderPose.deviceToAbsolute.matrix);
            float l=0,r=0,t=0,b=0; OpenVR.System.GetProjectionRaw(which,ref l,ref r,ref t,ref b);
            Matrix vp=view*(Matrix)VrMath.Projection(l,r,t,b,0.03);
            for(int h=0;h<2;h++)
            {
                var hand=hands[h]; if(!hand.renderPose.isTracked) continue;
                Matrix raw=hand.renderPose.deviceToAbsolute.matrix;
                if(!Main.WorldAvailable || MenuKeyboard.IsOpen || !TrackedArms.Applied)
                {
                    if(meshes[h]!=null) DrawMesh(meshes[h],raw,vp,new Vector4(0.8f,0.85f,0.9f,1));
                    else DrawMesh(box,Matrix.CreateScale(0.035f,0.075f,0.04f)*raw,vp,new Vector4(0.6f,0.7f,0.8f,1));
                }
                if(h==1 && Common.Config.ControllerMenuPointer)
                {
                    Matrix tip=hand.RenderAimTracking;
                    float distance=MenuKeyboard.IsOpen ? FloatingKeyboard.PointerDistance(tip) : Components.VRGUIManager.PointerDistance(tip);
                    Matrix ray=Matrix.CreateScale(0.002f,0.002f,distance)*Matrix.CreateTranslation(0,0,-distance*0.5f)*tip;
                    DrawMesh(box,ray,vp,new Vector4(0.2f,0.9f,1,1));
                }
            }
            // Restore the engine's immediate-context state, including its cached bindings.
            using(var commands=context.FinishCommandList(false)) MyRender11.DeviceInstance.ImmediateContext.ExecuteCommandList(commands,true);
        }

        public static bool Submit()
        {
            if(failed || !Player.Headset.renderPose.isTracked) return false;
            try
            {
                Init(); var size=MyRender11.Resolution; Resize(size);
                var hands=new[] { Player.HandL,Player.HandR };
                var meshes=new Mesh[2];
                for(int h=0;h<2;h++) if(hands[h].renderPose.isTracked) meshes[h]=GetModel(hands[h]);
                for(int eye=0;eye<2;eye++)
                {
                    var which=(EVREye)eye;
                    MyRender11.DeviceInstance.ImmediateContext.ClearRenderTargetView(targets[eye],new RawColor4(0,0,0,1));
                    Components.VRGUIManager.DrawStereo(eyes[eye],which);
                    DrawEye(targets[eye],which,size,hands,meshes,false);
                    Matrix view=Matrix.Invert(OpenVR.System.GetEyeToHeadTransform(which).ToMatrix()*Player.Headset.renderPose.deviceToAbsolute.matrix);
                    float l=0,r=0,t=0,b=0; OpenVR.System.GetProjectionRaw(which,ref l,ref r,ref t,ref b);
                    SpatialUi.Draw(eyes[eye],view,VrMath.Projection(l,r,t,b,.03),tracking:true);
                    FloatingKeyboard.Draw(eyes[eye],which);
                    var texture=new Texture_t { eColorSpace=EColorSpace.Gamma,eType=ETextureType.DirectX,handle=eyes[eye].NativePointer };
                    var bounds=new VRTextureBounds_t { uMax=1,vMax=1 };
                    var result=OpenVR.Compositor.Submit(which,ref texture,ref bounds,EVRSubmitFlags.Submit_Default);
                    if(result!=EVRCompositorError.None && result!=EVRCompositorError.DoNotHaveFocus) throw new InvalidOperationException("Menu hands submit: "+result);
                }
                return true;
            }
            catch(Exception ex) { failed=true; Logger.Warning(ex,"Menu controller rendering disabled; flat panel remains available"); return false; }
        }
    }
}
