using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using Sandbox.Game.World;
using VRage.FileSystem;
using Vertex=SpaceEngineersVR.Player.GloveGeometry.Vertex;
using GameImage=SharpDX.Toolkit.Graphics.Image;
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
    // Menus and miniature views keep gloves at physical size in predicted tracking space.
    internal static class MenuHands
    {
        [StructLayout(LayoutKind.Sequential)] private struct Constants { public Matrix Transform; public Vector4 Color,Material; }
        private sealed class Mesh : IDisposable
        {
            public Buffer Vertices,Indices; public int Count;
            public ShaderResourceView Color,Extra; public Matrix WristMount,PointFrame; public Vector3 Center,PinchPoint;
            public void Dispose() { Vertices?.Dispose(); Indices?.Dispose(); Color?.Dispose(); Extra?.Dispose(); }
        }
        private sealed class Model { public Mesh Mesh; public bool Failed; }
        private static readonly Dictionary<string,Model> models=new Dictionary<string,Model>();
        private sealed class Wrist { public string Model; public Matrix Mount,RightPoint,LeftPoint; public Vector3 PinchPoint; }
        private static volatile Wrist wrist;
        private static SharpDX.Direct3D11.Device gpu;
        private static DeviceContext context;
        private static SamplerState sampler;
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
        private static readonly RenderRecovery recovery=new RenderRecovery("Menu controllers");
        private static bool failed => recovery.Failed;
        public static bool Available => !failed;
        private const string Shader=@"
cbuffer Params : register(b0) { row_major float4x4 Transform; float4 Color; float4 Material; };
Texture2D Albedo : register(t0); Texture2D Extra : register(t1); SamplerState Filter : register(s0);
struct V { float3 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; float3 closed:POSITION1; float3 closedNormal:NORMAL1; };
struct P { float4 p:SV_POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; };
P VS(V v) { P o; o.p=mul(float4(lerp(v.p,v.closed,Material.y),1),Transform); o.n=normalize(lerp(v.n,v.closedNormal,Material.y)); o.uv=v.uv; return o; }
float3 Hue(float h) { return saturate(abs(frac(h+float3(0,2.0/3.0,1.0/3.0))*6-3)-1); }
float3 Gamma(float3 c) { return lerp(c*12.92,1.055*pow(max(c,0),1.0/2.4)-.055,step(.0031308,c)); }
float3 Linear(float3 c) { return lerp(c/12.92,pow((c+.055)/1.055,2.4),step(.04045,c)); }
float4 PS(P p):SV_TARGET {
    float3 color=Color.rgb;
    if(Material.x>0) {
        float4 sample=Albedo.Sample(Filter,p.uv); float4 extra=Extra.Sample(Filter,p.uv);
        float3 baseColor=Gamma(sample.rgb);
        float value=max(baseColor.r,max(baseColor.g,baseColor.b));
        float3 painted=lerp(1,Hue(Color.x),saturate(Color.y))*saturate(value+Color.z);
        float mask=extra.a*saturate((.7-sample.a)/.3);
        color=lerp(sample.rgb,Linear(painted),mask)*(.6+.4*extra.r);
    }
    float light=.55+.45*abs(dot(normalize(p.n),normalize(float3(.3,.8,.5))));
    return float4(Material.x>0 ? Gamma(color*light):color*light,1);
}";
        private static Mesh CreateMesh(Vertex[] vertices,ushort[] indices)
        {
            var device=gpu;
            return new Mesh { Vertices=Buffer.Create(device,BindFlags.VertexBuffer,vertices),
                Indices=Buffer.Create(device,BindFlags.IndexBuffer,indices),Count=indices.Length };
        }
        private static void Init(SharpDX.Direct3D11.Device owner=null)
        {
            if(context!=null) return;
            gpu=owner ?? MyRender11.DeviceInstance; var device=gpu;
            using(var vs=ShaderBytecode.Compile(Shader,"VS","vs_4_0"))
            using(var ps=ShaderBytecode.Compile(Shader,"PS","ps_4_0"))
            {
                vertexShader=new VertexShader(device,vs);
                pixelShader=new PixelShader(device,ps);
                layout=new InputLayout(device,vs,new[] {
                    new InputElement("POSITION",0,Format.R32G32B32_Float,0,0),
                    new InputElement("NORMAL",0,Format.R32G32B32_Float,12,0),
                    new InputElement("TEXCOORD",0,Format.R32G32_Float,24,0),
                    new InputElement("POSITION",1,Format.R32G32B32_Float,32,0),
                    new InputElement("NORMAL",1,Format.R32G32B32_Float,44,0) });
            }
            constants=new Buffer(device,96,ResourceUsage.Default,BindFlags.ConstantBuffer,CpuAccessFlags.None,ResourceOptionFlags.None,0);
            rasterizer=new RasterizerState(device,new RasterizerStateDescription {
                FillMode=FillMode.Solid,CullMode=CullMode.None,IsDepthClipEnabled=true });
            depthState=new DepthStencilState(device,new DepthStencilStateDescription {
                IsDepthEnabled=true,DepthWriteMask=DepthWriteMask.All,DepthComparison=Comparison.GreaterEqual });
            var vertices=new Vertex[8];
            for(int i=0;i<8;i++) vertices[i]=new Vertex { Position=new Vector3((i&1)==0 ? -0.5f:0.5f,(i&2)==0 ? -0.5f:0.5f,(i&4)==0 ? -0.5f:0.5f),Normal=Vector3.Up };
            box=CreateMesh(vertices,new ushort[] {0,1,3,0,3,2,4,6,7,4,7,5,0,4,5,0,5,1,2,3,7,2,7,6,0,2,6,0,6,4,1,5,7,1,7,3});
            sampler=new SamplerState(device,new SamplerStateDescription { Filter=SharpDX.Direct3D11.Filter.MinMagMipLinear,AddressU=TextureAddressMode.Wrap,AddressV=TextureAddressMode.Wrap,AddressW=TextureAddressMode.Wrap,MaximumLod=float.MaxValue });
            context=new DeviceContext(device);
        }
        private static string CharacterModel => Main.WorldAvailable ? MySession.Static?.LocalCharacter?.Definition?.Model ?? GloveGeometry.DefaultModel:GloveGeometry.DefaultModel;
        private static Vector4 CharacterColor => new Vector4(Main.WorldAvailable ? MySession.Static?.LocalCharacter?.ColorMask ?? Vector3.Zero:new Vector3(0,-1,0),1);
        private static ShaderResourceView Texture(string path,bool color)
        {
            using(var stream=MyFileSystem.OpenRead(Path.Combine(MyFileSystem.ContentPath,path)))
            using(var image=GameImage.Load(stream,path))
            {
                var d=image.Description;
                if(!color && d.Format==Format.BC7_UNorm_SRgb) d.Format=Format.BC7_UNorm;
                using(var texture=new Texture2D(gpu,new Texture2DDescription { Width=d.Width,Height=d.Height,MipLevels=d.MipLevels,ArraySize=1,Format=d.Format,SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource },image.ToDataBox()))
                    return new ShaderResourceView(gpu,texture);
            }
        }
        private static Mesh Glove(string path,bool left,bool throwOnError=false)
        {
            string key=path+(left ? ":left":":right");
            if(!models.TryGetValue(key,out var model))
            {
                if(models.Count>=4) { foreach(var old in models.Values) old.Mesh?.Dispose(); models.Clear(); }
                models[key]=model=new Model();
            }
            if(model.Mesh!=null || model.Failed) return model.Mesh;
            try
            {
                var geometry=GloveGeometry.Load(MyFileSystem.ContentPath,path,left);
                var mesh=CreateMesh(geometry.Vertices,geometry.Indices);
                try { mesh.Color=Texture(geometry.ColorTexture,true); mesh.Extra=Texture(geometry.ExtraTexture,false); }
                catch { mesh.Dispose(); throw; }
                mesh.PinchPoint=geometry.PinchPoint; mesh.WristMount=geometry.WristMount; mesh.PointFrame=geometry.PointFrame;
                var bounds=BoundingBox.CreateInvalid(); foreach(var vertex in geometry.Vertices) bounds.Include(vertex.Position);
                mesh.Center=bounds.Center; model.Mesh=mesh;
                PublishGeometry(path,left,geometry);
                if(!throwOnError) Logger.Info("Character glove loaded: "+key);
            }
            catch(Exception ex) { if(throwOnError) throw; model.Failed=true; Logger.Warning(ex,"Character glove unavailable: "+key); }
            return model.Mesh;
        }
        private static Mesh GetModel(Controller hand)
        {
            string path=CharacterModel; var mesh=Glove(path,hand==Player.HandL);

            return mesh;
        }
        internal static void PublishGeometry(string model,bool left,GloveGeometry geometry)
        {
            var previous=wrist;
            wrist=new Wrist { Model=model,
                Mount=left ? geometry.WristMount : previous?.Model==model ? previous.Mount : Matrix.Identity,
                PinchPoint=!left ? geometry.PinchPoint:previous?.PinchPoint ?? Vector3.Zero,
                RightPoint=!left ? geometry.PointFrame : previous?.Model==model ? previous.RightPoint : Matrix.Identity,
                LeftPoint=left ? geometry.PointFrame : previous?.Model==model ? previous.LeftPoint : Matrix.Identity };
        }
        internal static bool TryPointPose(Matrix grip,out MatrixD point,Controller hand=null)
        {
            hand=hand ?? Player.HandR;
            var pose=wrist; point=MatrixD.Identity;
            var frame=pose==null ? Matrix.Identity : hand==Player.HandL ? pose.LeftPoint:pose.RightPoint;
            if(pose==null || pose.Model!=CharacterModel || frame==Matrix.Identity) return false;
            point=(MatrixD)frame*Alignment.Apply(Alignment.HandKey(hand),CockpitHandPose.GripWrist(grip));
            return true;
        }
        internal static bool TryWristMount(Matrix grip,out MatrixD mount)
        {
            mount=MatrixD.Identity;
            var pose=wrist;
            if(pose==null || pose.Model!=CharacterModel || pose.Mount==Matrix.Identity) return false;
            mount=Alignment.Apply(Alignment.WristKey,(MatrixD)pose.Mount*Alignment.Apply(Alignment.HandKey(Player.HandL),CockpitHandPose.GripWrist(grip)));
            return true;
        }
        private static void Resize(Vector2I size)
        {
            if(eyes[0]!=null && eyes[0].Description.Width==size.X && eyes[0].Description.Height==size.Y) return;
            for(int i=0;i<2;i++) { targets[i]?.Dispose(); eyes[i]?.Dispose(); }
            Depth?.Dispose(); depthView?.Dispose(); depth?.Dispose();
            var desc=new Texture2DDescription { Width=size.X,Height=size.Y,MipLevels=1,ArraySize=1,
                Format=Format.R8G8B8A8_UNorm,SampleDescription=new SampleDescription(1,0),BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource };
            for(int i=0;i<2;i++) { eyes[i]=new Texture2D(gpu,desc); targets[i]=new RenderTargetView(gpu,eyes[i]); }
            depth=CreateDepth(gpu,size,out depthView,out var readable);
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
        private static void DrawMesh(Mesh mesh,Matrix world,Matrix viewProjection,Vector4 color,float curl=0)
        {
            if(mesh.Color!=null) color+=VRageRender.MyTextureDebugMultipliers.Defaults.ColorizeHSV;
            var data=new Constants { Transform=world*viewProjection,Color=color,Material=new Vector4(mesh.Color==null ? 0:1,curl,0,0) };
            context.UpdateSubresource(ref data,constants);
            context.InputAssembler.SetVertexBuffers(0,new VertexBufferBinding(mesh.Vertices,56,0));
            context.InputAssembler.SetIndexBuffer(mesh.Indices,Format.R16_UInt,0);
            context.PixelShader.SetShaderResource(0,mesh.Color); context.PixelShader.SetShaderResource(1,mesh.Extra);
            context.DrawIndexed(mesh.Count,0,0);
        }
        public static void DrawInWorld(Texture2D texture, EVREye eye,bool menu=true)
        {
            if (failed) return;
            try
            {
                Init(); var size = MyRender11.Resolution; Resize(size);
                var hands = new[] { Player.HandL, Player.HandR };
                var meshes = new Mesh[2];
                for (int h = 0; h < 2; h++) if (hands[h].renderPose.isTracked) meshes[h] = GetModel(hands[h]);
                if(menu && !MenuKeyboard.Standalone) Components.VRGUIManager.DrawStereo(texture,eye,ThirdPersonView.Active ? null:NativeHandLayer.Depth);
                using (var target = new RenderTargetView(gpu, texture))
                    DrawEye(target, eye, size, hands, meshes, false,menu);
                if(ThirdPersonView.Active && !NativeGloves.Visible)
                {
                    Matrix view=Matrix.Invert(OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix()*Player.Headset.renderPose.deviceToAbsolute.matrix);
                    float l=0,r=0,t=0,b=0; OpenVR.System.GetProjectionRaw(eye,ref l,ref r,ref t,ref b);
                    SpatialUi.Draw(texture,view,VrMath.Projection(l,r,t,b,.03),tracking:true);
                }
            }
            catch (Exception ex) { recovery.Fail(ex, "World-menu controller rendering disabled"); }
        }

        private static void Setup(RenderTargetView target,Vector2I size)
        {
            context.ClearState();
            context.ClearDepthStencilView(depthView,DepthStencilClearFlags.Depth,0,0);
            context.OutputMerger.SetRenderTargets(depthView,target);
            context.OutputMerger.SetDepthStencilState(depthState);
            context.Rasterizer.State=rasterizer;
            context.Rasterizer.SetViewport(0,0,size.X,size.Y);
            context.InputAssembler.InputLayout=layout;
            context.InputAssembler.PrimitiveTopology=SharpDX.Direct3D.PrimitiveTopology.TriangleList;
            context.VertexShader.Set(vertexShader); context.PixelShader.Set(pixelShader);
            context.VertexShader.SetConstantBuffer(0,constants); context.PixelShader.SetConstantBuffer(0,constants); context.PixelShader.SetSampler(0,sampler);
        }
        internal static Texture2D PreviewGlove(SharpDX.Direct3D11.Device device,bool left,float curl,Vector3 color,float tablet=-1,bool palm=false,Action<SurfaceView[],MatrixD> preview=null,float pointer=0,float zoom=1,Vector3? tipView=null)
        {
            Init(device); var size=new Vector2I(960,720); Resize(size);
            var mesh=Glove(GloveGeometry.DefaultModel,left,true);
            if(mesh==null) throw new InvalidOperationException("Glove preview could not load the installed model");
            Setup(targets[0],size); context.ClearRenderTargetView(targets[0],new RawColor4(.035f,.055f,.075f,1));
            Vector3 center=Vector3.Transform(mesh.Center,CockpitHandPose.GripWrist(Matrix.Identity));
            Vector3 eye=center+new Vector3(left ? -.32f:.32f,.23f,palm ? -.30f:.30f)*(tablet<0 ? 1:1.9f)*zoom;
            Vector3 up=Vector3.Up;
            if(tipView.HasValue)
            {
                // Inspection view centred on the pointer origin, mirrored for the left hand.
                center=(mesh.PointFrame*CockpitHandPose.GripWrist(Matrix.Identity)).Translation;
                var offset=tipView.Value; if(left) offset.X=-offset.X;
                eye=center+offset;
            }
            if(tablet>=0)
            {
                var mount=(MatrixD)mesh.WristMount*CockpitHandPose.GripWrist(Matrix.Identity);
                var open=SpatialUi.WristPose(mount,1,.225f,-1);
                center=(center+(Vector3)open.Translation)*.5f;
                eye=center+(Vector3)mount.Right*.65f+(Vector3)mount.Backward*.22f;
                up=(Vector3)mount.Backward;
            }
            Matrix view=Matrix.CreateLookAt(eye,center,up);
            float slope=(float)Math.Tan(.65f/2);
            Matrix projection=(Matrix)VrMath.Projection(-slope*960/720,slope*960/720,-slope,slope,.01);
            DrawMesh(mesh,CockpitHandPose.GripWrist(Matrix.Identity),view*projection,new Vector4(color,1),curl);
            if(pointer>0)
            {
                Matrix tip=mesh.PointFrame*CockpitHandPose.GripWrist(Matrix.Identity);
                DrawMesh(box,Matrix.CreateScale(0.002f,0.002f,pointer)*Matrix.CreateTranslation(0,0,-pointer*0.5f)*tip,view*projection,left ? PhysicalSurface.LeftLaser : new Vector4(0.2f,0.9f,1,1));
            }
            using(var commands=context.FinishCommandList(false)) gpu.ImmediateContext.ExecuteCommandList(commands,true);
            if(tablet>=0)
            {
                MatrixD mount=mesh.WristMount*CockpitHandPose.GripWrist(Matrix.Identity);
                var status=new EssentialHud.View { Levels=new[] {.8f,.7f,.6f,.5f},Values=new[] {"80","70","60","50"},Speed="24.6",Dampeners=true };
                var panels=SpatialUi.WristViews(mount,tablet,1,status,SpatialUi.WristKeys());
                preview?.Invoke(panels,MatrixD.Invert(view));
                PhysicalSurface.Draw(eyes[0],panels,view,projection,Depth);
            }
            return eyes[0];
        }
        internal static Texture2D PreviewSurface(SharpDX.Direct3D11.Device device,SurfaceView surface)
        {
            Init(device); var size=new Vector2I(960,720); Resize(size);
            Setup(targets[0],size); context.ClearRenderTargetView(targets[0],new RawColor4(.035f,.055f,.075f,1));
            using(var commands=context.FinishCommandList(false)) gpu.ImmediateContext.ExecuteCommandList(commands,true);
            Matrix view=Matrix.CreateLookAt(new Vector3(0,0,surface.Width*1.25f),Vector3.Zero,Vector3.Up);
            float slope=(float)Math.Tan(.65f/2);
            Matrix projection=(Matrix)VrMath.Projection(-slope*960/720,slope*960/720,-slope,slope,.01);
            PhysicalSurface.Draw(eyes[0],new[] { surface },view,projection,Depth);
            return eyes[0];
        }
        internal static Texture2D PreviewKnob(SharpDX.Direct3D11.Device device,float value,bool hand)
        {
            Init(device); var size=new Vector2I(960,720); Resize(size);
            var panel=new SurfaceView { Id="Knob inspection",Style=SurfaceStyle.WristMenu,Width=.4f,Height=.225f,Pose=MatrixD.Identity,SignalWindow=true };
            WristPanel.Show(3); panel.Keys=WristPanel.Keys(null,false,false,false,true,null); WristPanel.Show(0);
            foreach(var key in panel.Keys) if(key.Knob.HasValue) key.Knob=value;
            var center=WristKnob.Center(panel);
            Matrix view=Matrix.CreateLookAt(center+(hand ? new Vector3(-.20f,.12f,.26f):new Vector3(.11f,.07f,.17f)),center,Vector3.Up);
            Matrix projection=(Matrix)VrMath.Projection(-.55f,.55f,-.4125f,.4125f,.005);
            Setup(targets[0],size); context.ClearRenderTargetView(targets[0],new RawColor4(.035f,.055f,.075f,1));
            if(hand)
            {
                var mesh=Glove(GloveGeometry.DefaultModel,false,true);
                var start=Matrix.Invert(mesh.PointFrame.GetOrientation());
                var turn=new WristKnob.Turn(); turn.Begin(start,.5f); turn.Move(start*Matrix.CreateRotationZ((.5f-value)*WristKnob.Travel));
                var pose=CockpitHandPose.Attach(turn.Wrist,Matrix.Identity,mesh.PinchPoint,center);
                DrawMesh(mesh,(Matrix)pose,view*projection,new Vector4(0,-1,0,1),1);
            }
            using(var commands=context.FinishCommandList(false)) gpu.ImmediateContext.ExecuteCommandList(commands,true);
            PhysicalSurface.Draw(eyes[0],new[] {panel},view,projection,Depth);
            return eyes[0];
        }
        internal static Matrix PointerTracking(bool render=false,Controller hand=null)
        {
            hand=hand ?? Player.HandR;
            if(TryPointPose(render ? hand.RenderGripTracking:hand.GripTracking,out var point,hand)) return (Matrix)point;
            return render ? hand.RenderAimTracking:hand.AimTracking;
        }
        internal static MatrixD AttachWrist(MatrixD pose,Controller hand)
        {
            var state=wrist;
            if(state==null) return pose;
            var tip=(hand==Player.HandL ? state.LeftPoint:state.RightPoint).Translation;
            if(FloatingKeyboard.TryAttachment(hand,out var keyboardWrist,out var keyboardPoint,out float keyboardBlend,tracking:true))
                return CockpitHandPose.Blend(pose,CockpitHandPose.Attach(keyboardWrist,Matrix.Identity,tip,keyboardPoint),keyboardBlend);
            if(ThirdPersonView.Active && FloatingWindows.TryAttachment(hand,out var remoteWrist,out var remotePoint,out float remoteBlend,tracking:true))
                return CockpitHandPose.Blend(pose,CockpitHandPose.Attach(remoteWrist,Matrix.Identity,tip,remotePoint),remoteBlend);
            if(hand==Player.HandL) return pose;
            if(!ThirdPersonView.Active || !SpatialUi.TryWristAttachment(out var captured,out var contact,out float blend,render:true)) return pose;
            var attached=CockpitHandPose.Attach(captured,Matrix.Identity,SpatialUi.PinchingKnob ? state.PinchPoint:state.RightPoint.Translation,contact);
            return CockpitHandPose.Blend(pose,attached,blend);
        }
        private static void DrawEye(RenderTargetView target, EVREye eye, Vector2I size, Controller[] hands, Mesh[] meshes, bool clear,bool pointer=true)
        {
            Setup(target,size);
            if(clear) context.ClearRenderTargetView(target,new RawColor4(0,0,0,1));
            var which=eye;
            Matrix view=Matrix.Invert(OpenVR.System.GetEyeToHeadTransform(which).ToMatrix()*Player.Headset.renderPose.deviceToAbsolute.matrix);
            float l=0,r=0,t=0,b=0; OpenVR.System.GetProjectionRaw(which,ref l,ref r,ref t,ref b);
            Matrix vp=view*(Matrix)VrMath.Projection(l,r,t,b,0.03);
            for(int h=0;h<2;h++)
            {
                var hand=hands[h]; if(!hand.renderPose.isTracked) continue;
                Matrix raw=hand.renderPose.deviceToAbsolute.matrix;
                if(!NativeGloves.Visible && (!Main.WorldAvailable || !Common.Config.TrackedArms || !NativeHandLayer.Drawn(h==0) || !pointer))
                {
                    if(meshes[h]!=null)
                    {
                        var grip=hand.RenderGripTracking;
                        var pose=(Matrix)Alignment.Apply(Alignment.HandKey(hand),CockpitHandPose.GripWrist(grip));
                        var c=Controls.Static;
                        if(Main.WorldAvailable) pose=(Matrix)AttachWrist(pose,hand);
                        float curl=h==0 ? Math.Max(c.LeftTriggerPressure.RawPosition.X,c.LeftGripPressure.RawPosition.X):Math.Max(c.PointerPressure.RawPosition.X,c.RightGripPressure.RawPosition.X);
                        DrawMesh(meshes[h],pose,vp,CharacterColor,h==1 && SpatialUi.PinchingKnob ? 1:pointer ? 0:MathHelper.Clamp(curl,0,1));
                    }
                    else DrawMesh(box,Matrix.CreateScale(0.035f,0.075f,0.04f)*raw,vp,new Vector4(0.6f,0.7f,0.8f,1));
                }
                if(pointer && (MenuKeyboard.IsOpen ? FloatingKeyboard.Hits(PointerTracking(true,hand)) : hand==MenuPointer.Hand) && Common.Config.ControllerMenuPointer && (h==0 || !SpatialUi.OwnsRight && !SpatialUi.RayTargeted))
                {
                    Matrix tip=PointerTracking(true,hand);
                    float distance=MenuKeyboard.IsOpen ? FloatingKeyboard.PointerDistance(tip) : Components.VRGUIManager.PointerDistance(tip);
                    Matrix ray=Matrix.CreateScale(0.002f,0.002f,distance)*Matrix.CreateTranslation(0,0,-distance*0.5f)*tip;
                    DrawMesh(box,ray,vp,h==0 ? PhysicalSurface.LeftLaser : new Vector4(0.2f,0.9f,1,1));
                }
            }
            // Restore the engine's immediate-context state, including its cached bindings.
            using(var commands=context.FinishCommandList(false)) gpu.ImmediateContext.ExecuteCommandList(commands,true);
        }

        public static bool Submit()
        {
            if(failed || !Player.Headset.renderPose.isTracked) return false;
            try
            {
                Init(); var size=EyeResolution.Update(); Resize(size);
                var hands=new[] { Player.HandL,Player.HandR };
                var meshes=new Mesh[2];
                for(int h=0;h<2;h++) if(hands[h].renderPose.isTracked) meshes[h]=GetModel(hands[h]);
                for(int eye=0;eye<2;eye++)
                {
                    var which=(EVREye)eye;
                    gpu.ImmediateContext.ClearRenderTargetView(targets[eye],new RawColor4(0,0,0,1));
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
            catch(Exception ex) { recovery.Fail(ex,"Menu controller rendering disabled; flat panel remains available"); return false; }
        }
    }
}
