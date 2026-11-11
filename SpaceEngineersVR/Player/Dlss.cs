using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HarmonyLib;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRageMath;
#if !LINUX
using SharpDX.Direct3D11;
using Device=SharpDX.Direct3D11.Device;
using Resource=SharpDX.Direct3D11.Resource;
#endif

namespace SpaceEngineersVR.Player
{
    // NVIDIA DLAA or DLSS upscaling in place of FXAA, one NGX feature and history per eye; render thread except where noted.
    internal static partial class Dlss
    {
        internal const string RuntimeFile="nvngx_dlss.dll";
        internal static readonly string[] Modes={"FXAA","DLAA","DLSS Quality","DLSS Balanced","DLSS Performance"};
        internal static readonly float[] ModeScales={1,1,2f/3,.58f,.5f};
        internal static int ClampMode(int mode) => MathHelper.Clamp(mode,0,Modes.Length-1);
        // Applied after EyeResolution.Size so its clamp cannot absorb the mode's scale.
        internal static Vector2I Scaled(Vector2I size,float scale) =>
            scale<1 ? new Vector2I(Math.Max(64,(int)Math.Round(size.X*scale)),Math.Max(64,(int)Math.Round(size.Y*scale))) : size;
        // NGX upscales from half to full output size; DLAA only takes full size, so DLAA with dynamic resolution uses an upscaling feature.
        internal static Vector2I Minimum(Vector2I output,float scale,bool dynamic) => scale<1 || dynamic ? (output+Vector2I.One)/2 : output;
        internal static Vector2I Clamp(Vector2I size,Vector2I output,float scale,bool dynamic) =>
            Vector2I.Min(Vector2I.Max(Scaled(size,scale),Minimum(output,scale,dynamic)),output);
        // A scene size NGX accepts for the mode: inside the feature's range and not above the mode's own size.
        internal static bool InRange(Vector2I size,Vector2I output,float scale,bool dynamic)
        {
            Vector2I min=Minimum(output,scale,dynamic),max=Scaled(output,scale);
            return size.X>=min.X && size.Y>=min.Y && size.X<=max.X && size.Y<=max.Y;
        }

        // Camera cuts that invalidate the temporal history; pure so the self-test can drive it.
        internal sealed class Transitions
        {
            private bool first=true,third,spectator,menu,rig;
            private int epoch;
            private Vector2I size;
            internal bool Update(int epoch,bool third,bool spectator,bool menu,bool rig,Vector2I size,bool recenter)
            {
                bool cut=first || recenter || epoch!=this.epoch || third!=this.third || spectator!=this.spectator || menu!=this.menu || rig!=this.rig || size!=this.size;
                first=false; this.epoch=epoch; this.third=third; this.spectator=spectator; this.menu=menu; this.rig=rig; this.size=size;
                return cut;
            }
            // A jump past 40 m or a turn past about 35 degrees in one frame is a cut, not motion.
            internal static bool Jump(Vector3D from,Vector3 fromForward,Vector3D to,Vector3 toForward) =>
                Vector3D.DistanceSquared(from,to)>1600 || Vector3.Dot(fromForward,toForward)<.82f;
        }

        internal static float Halton(int index,int radix)
        {
            float result=0,fraction=1f/radix;
            for(int i=index+1;i>0;i/=radix) { result+=i%radix*fraction; fraction/=radix; }
            return result;
        }
        // The engine's 8x8 ordered dither: each finer 2x2 level is the more significant digit.
        internal static int Bayer(int x,int y)
        {
            int value=0;
            for(int bit=1;bit<8;bit<<=1) value=value*4+((x&bit)!=0 ? ((y&bit)!=0 ? 1:2) : ((y&bit)!=0 ? 3:0));
            return value;
        }
        // Sub-pixel offsets in [-0.5, 0.5) as the NDC shift of the projection; NGX gets the pixel values.
        internal static Vector2 Pixels(int frame) => new Vector2(Halton(frame,2)-.5f,Halton(frame,3)-.5f);
        internal static Vector2 Ndc(Vector2 pixels,Vector2I size) => new Vector2(-2*pixels.X/size.X,2*pixels.Y/size.Y);
        // Where the jitter moved the image, in render pixels with y down; m34 is the projection's w term.
        internal static Vector2 Shift(Vector2 ndc,Vector2I size,double m34) => new Vector2((float)(ndc.X/m34*size.X/2),(float)(-ndc.Y/m34*size.Y/2));
        // Camera-relative matrices change origin every frame; the previous view projection is moved to this frame's origin in double precision.
        internal static Matrix Previous(Matrix previousUnjittered,Vector3D position,Vector3D previousPosition) =>
            Matrix.CreateTranslation((Vector3)(position-previousPosition))*previousUnjittered;
    }

#if LINUX
    internal static partial class Dlss
    {
        internal static bool Available => false;
        internal static bool? NvidiaAdapter => false;
        internal static string Status => "DLSS needs Windows";
        internal static Vector2I Render(Vector2I size,Vector2I output) => size;
        internal static bool SolidPreviews => false;
        internal static SharpDX.Direct3D11.ShaderResourceView OverlayDepth => null;
        internal static SharpDX.Direct3D11.ShaderResourceView HandDepth(SharpDX.Direct3D11.ShaderResourceView raw) => raw;
        internal static void SetRuntimePath(string path) { }
        internal static void NotifyCut() { }
        internal static void BeginFrame(Vector2I size,CameraRig.Frame rig) { }
        internal static Vector2I Output(Vector2I size) => size;
        internal static Vector2 Jitter(int eye) => Vector2.Zero;
        internal static void Capture(int eye,EnvironmentMatrices env,MatrixD projection) { }
        internal static void Dispose() { }
    }
#else
    internal static partial class Dlss
    {
        private sealed class Eye
        {
            public IntPtr Feature;
            public Vector2I MaxRender,Output;
            public int Quality,Failures;
            public uint Preset;
            public bool History,Applied,Evaluated,Objects;
            public Matrix InverseJittered,Unjittered,PreviousUnjittered;
            public MatrixD Projection;
            public Vector3D Position,PreviousPosition;
            public Vector3 Forward,PreviousForward;
        }
        private const int NvidiaVendor=0x10DE;
        private const string FxaaTarget="MyRender11.FXAA.Rgb8";
        private static readonly int[] ModeQuality={Ngx.QualityDlaa,Ngx.QualityDlaa,Ngx.QualityMax,Ngx.QualityBalanced,Ngx.QualityMaxPerf};
        private static readonly System.Reflection.MethodInfo updateFrameConstants=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyCommon"),"UpdateFrameConstants");
        private static readonly System.Reflection.MethodInfo allStages=AccessTools.PropertyGetter(AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext"),"AllShaderStages"),
            pixelStage=AccessTools.PropertyGetter(AccessTools.TypeByName("VRage.Render11.RenderContext.MyRenderContext"),"PixelShader"),
            setConstantBuffer=AccessTools.Method(AccessTools.TypeByName("VRage.Render11.RenderContext.MyAllShaderStages"),"SetConstantBuffer"),
            setSamplers=AccessTools.Method(AccessTools.TypeByName("VRage.Render11.RenderContext.MyPixelStage"),"SetSamplers"),
            frameConstants=AccessTools.PropertyGetter(AccessTools.TypeByName("VRageRender.MyCommon"),"FrameConstants"),
            createDepth=AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Resources.MyDepthStencilManager"),"CreateDepthStencil"),
            disposeDepth=AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Resources.MyDepthStencilManager"),"DisposeTex"),
            mainCullQuery=AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Culling.MyCullManager"),"GetGBufferCullQuery");
        private static readonly System.Reflection.FieldInfo standardSamplers=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MySamplerStateManager"),"StandardSamplers"),
            depthStencils=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"DepthStencils"),
            gbuffer=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main"),
            resolvedDepth=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"m_resolvedDepthStencil"),
            ditherTexture=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGeneratedTextureManager"),"Dithering8x8Tex"),
            ditherResource=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.Internal.MyGeneratedTexture"),"m_resource"),
            ditherView=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.Internal.MyGeneratedTexture"),"m_srv"),
            cullManager=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"Cull"),
            cullGroups=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Culling.MyCullQuery"),"Groups");
        private static readonly (string Name,object Member)[] engineMembers=
        {
            ("UpdateFrameConstants",updateFrameConstants),("AllShaderStages",allStages),("PixelShader",pixelStage),("SetConstantBuffer",setConstantBuffer),
            ("SetSamplers",setSamplers),("FrameConstants",frameConstants),("CreateDepthStencil",createDepth),("DisposeTex",disposeDepth),
            ("StandardSamplers",standardSamplers),("DepthStencils",depthStencils),("MyGBuffer.Main",gbuffer),("m_resolvedDepthStencil",resolvedDepth),
            ("Dithering8x8Tex",ditherTexture),("MyGeneratedTexture.m_resource",ditherResource),("MyGeneratedTexture.m_srv",ditherView)
        };
        private static readonly (string Name,object Member)[] objectMembers=
        {
            ("GetGBufferCullQuery",mainCullQuery),("MyManagers.Cull",cullManager),("MyCullQuery.Groups",cullGroups),("MyDynamicAABBTree.m_extension",DlssObjects.Extension)
        };
        internal static string MissingEngineMember => Array.Find(engineMembers,m=>m.Member==null).Name;
        internal static string MissingObjectMember => Array.Find(objectMembers,m=>m.Member==null).Name;
        private static bool objectsOff=MissingObjectMember!=null;
        private static object outputDepth,sceneResolvedDepth;
        private static Texture2D solidDither;
        private static ShaderResourceView solidDitherView,engineDitherView;
        private static Resource engineDither;
        private static Vector2I outputDepthSize;
        private static readonly Eye[] eyes={new Eye(),new Eye()};
        private static readonly DlssMotion.Resample sceneDepth=new DlssMotion.Resample(),handDepth=new DlssMotion.Resample();
        private static readonly Transitions transitions=new Transitions();
        private static int cutRequests,frame,tick;
        private static Vector2 pixels;
        private static Vector2I render,output;
        private static bool active,announced,runtimeMissing,switched;
        private static volatile string blocked,runtimePath;
        private static bool? nvidia,conflict;
        internal static Device Device { get; private set; }
        // SE-DLSS patches the same frame; the dedicated plugin keeps the renderer.
        private static bool Conflict => conflict ?? (conflict=AccessTools.TypeByName("ClientPlugin.Dlss.DlssRuntime")!=null).Value;

        private static int Mode => ClampMode(Common.Config?.Dlss ?? 0);
        // Settings threads read these; a non-NVIDIA adapter is the only answer known before the headset renders.
        internal static bool Available => blocked==null && Nvidia()!=false && !Conflict;
        internal static bool? NvidiaAdapter => Nvidia();
        private static bool Selected => Mode>0 && Available && !runtimeMissing;
        // Unchanged when DLSS cannot run, so FXAA keeps the full eye.
        internal static Vector2I Render(Vector2I size,Vector2I output) => Selected ? Clamp(size,output,ModeScales[Mode],Common.Config.DynamicResolution) : size;
        internal static bool SolidPreviews => Selected && Common.Config.DlssSolidPreviews;
        internal static string Status
        {
            get
            {
                if(blocked!=null) return blocked;
                if(Nvidia()==false) return "DLSS needs an NVIDIA RTX GPU";
                if(Conflict) return "DLSS off: the SE-DLSS plugin is loaded";
                if(Mode==0) return "";
                return active ? Modes[Mode]+" on" : runtimeMissing ? "DLSS needs "+RuntimeFile+" next to the plugin" : "";
            }
        }
        private static bool? Nvidia()
        {
            if(nvidia.HasValue) return nvidia;
            try
            {
                var device=MyRender11.DeviceInstance;
                if(device==null) return null;
                using(var dxgi=device.QueryInterface<SharpDX.DXGI.Device>())
                using(var adapter=dxgi.Adapter) nvidia=adapter.Description.VendorId==NvidiaVendor;
            }
            catch { return null; }
            return nvidia;
        }

        // Pulsar hands over the downloaded asset path; a local install keeps the file beside the plugin or in its assets.
        internal static void SetRuntimePath(string path) { runtimePath=path; runtimeMissing=false; }
        internal static string RuntimeDirectory()
        {
            string asset=runtimePath;
            foreach(string candidate in new[] {asset==null ? null:File.Exists(asset) ? Path.GetDirectoryName(asset):asset,Common.AssetFolder,Path.GetDirectoryName(typeof(Dlss).Assembly.Location)})
                if(!string.IsNullOrEmpty(candidate) && File.Exists(Path.Combine(candidate,RuntimeFile))) return candidate;
            return null;
        }

        internal static void NotifyCut() => Interlocked.Exchange(ref cutRequests,1);
        internal static bool Live => active && StereoRenderState.PhysicalEye && eyes[StereoRenderState.View].Applied;
        internal static Vector2I Output(Vector2I size) => active ? output : size;

        // size is the scene size this frame; the eye target stays at the configured size and dynamic resolution only moves the render subrect.
        internal static void BeginFrame(Vector2I size,CameraRig.Frame rig)
        {
            active=false; switched=false; tick++;
            try { SetSolidDither(SolidPreviews); }
            catch(Exception ex) { Block("DLSS off: "+ex.GetType().Name+": "+ex.Message); }
            // An eye that skipped its evaluate holds a stale previous view.
            foreach(var eye in eyes) { if(!eye.Evaluated) eye.History=false; eye.Applied=eye.Evaluated=eye.Objects=false; }
            bool recenter=Interlocked.Exchange(ref cutRequests,0)!=0;
            int mode=Mode;
            render=size;
            output=mode>0 ? Vector2I.Max(EyeResolution.Hud,size) : size;
            bool cut=transitions.Update(rig?.Epoch ?? -1,rig?.ThirdPerson==true,SpectatorView.Active,Main.MenuOpen,rig!=null,output,recenter);
            if(mode==0) { Release(); return; }
            if(blocked!=null || runtimeMissing) return;
            try
            {
                var current=MyRender11.DeviceInstance;
                if(current==null) return;
                if(Device==null || Device.NativePointer!=current.NativePointer) { Forget(); Device=current; }
                if(!Ngx.Initialized && !Initialize()) return;
                // The eye size settles half a second after a mode change; FXAA covers those frames instead of a feature that is replaced at once.
                bool dynamic=Common.Config.DynamicResolution;
                var maxRender=Scaled(output,ModeScales[mode]);
                if(!InRange(size,output,ModeScales[mode],dynamic)) return;
                uint preset=Common.Config.DlssFastModel ? Ngx.PresetF : Ngx.PresetK;
                int quality=mode==1 && dynamic ? Ngx.QualityMax : ModeQuality[mode];
                foreach(var eye in eyes)
                {
                    if(eye.Feature!=IntPtr.Zero && eye.MaxRender==maxRender && eye.Output==output && eye.Quality==quality && eye.Preset==preset) continue;
                    Ngx.ReleaseFeature(ref eye.Feature);
                    eye.Feature=Ngx.CreateFeature(maxRender.X,maxRender.Y,output.X,output.Y,quality,preset);
                    if(eye.Feature==IntPtr.Zero) { Block("DLSS off: "+Ngx.LastError); return; }
                    eye.MaxRender=maxRender; eye.Output=output; eye.Quality=quality; eye.Preset=preset; eye.History=false;
                    announced=false;
                }
            }
            catch(Exception ex) { Block("DLSS off: "+ex.Message); return; }
            if(cut) foreach(var eye in eyes) eye.History=false;
            pixels=Pixels(frame++);
            if(!announced) { announced=true; Logger.Info(Modes[mode]+" on: "+size.X+"x"+size.Y+" per eye"+(output!=size ? " to "+output.X+"x"+output.Y : "")+(eyes[0].Preset==Ngx.PresetF ? ", older model" : "")+"; NGX "+Ngx.DriverPath); }
            active=true;
        }

        private static bool Initialize()
        {
            if(Nvidia()==false) { Block("DLSS needs an NVIDIA RTX GPU"); return false; }
            if(Conflict) { Block("DLSS off: the SE-DLSS plugin is loaded"); return false; }
            if(MissingEngineMember!=null) { Block("DLSS off: renderer changed ("+MissingEngineMember+")"); return false; }
            string directory=RuntimeDirectory();
            if(directory==null)
            {
                runtimeMissing=true;
                Logger.Warning("DLSS off: "+RuntimeFile+" not found beside the plugin, in "+Common.AssetFolder+" or as a Pulsar asset");
                return false;
            }
            string logs=Path.Combine(VRage.FileSystem.MyFileSystem.UserDataPath,"SEVR","NGX");
            Directory.CreateDirectory(logs);
            var verdict=Ngx.Init(Device,directory,logs);
            if(verdict==Ngx.Verdict.Supported) { Logger.Info("DLSS: NGX ready from "+Ngx.DriverPath+"; runtime "+directory); return true; }
            Block(verdict==Ngx.Verdict.NeedsDriver ? "DLSS needs a newer NVIDIA driver" : verdict==Ngx.Verdict.Unavailable ? "DLSS is not available on this GPU" : "DLSS off: NGX failed");
            return false;
        }

        // Session-wide stop; the setting returns to FXAA.
        private static void Block(string reason)
        {
            blocked=reason;
            Logger.Warning(reason+(Ngx.LastError.Length>0 ? ": "+Ngx.LastError:""));
            Release();
            if(Common.Config!=null) Common.Config.Dlss=0;
        }
        private static void Release()
        {
            active=false;
            foreach(var eye in eyes) { Ngx.ReleaseFeature(ref eye.Feature); eye.History=false; }
            runtimeMissing=false;
        }
        private static void Forget()
        {
            foreach(var eye in eyes) { eye.Feature=IntPtr.Zero; eye.History=false; }
            Ngx.Forget();
            sceneDepth.Dispose(); handDepth.Dispose(); DlssMotion.Dispose(); DlssObjects.Forget();
            announced=false;
        }

        internal static Vector2 Jitter(int eye)
        {
            if(!active) return Vector2.Zero;
            eyes[eye].Applied=true;
            return Ndc(pixels,render);
        }

        // After SetCamera: the jittered inverse for unprojection, the unjittered pair for reprojection.
        internal static void Capture(int eye,EnvironmentMatrices env,MatrixD projection)
        {
            if(!active) return;
            var state=eyes[eye];
            state.InverseJittered=env.InvViewProjectionAt0;
            state.Projection=projection;
            state.Unjittered=(MatrixD)env.ViewAt0*projection;
            state.Position=env.CameraPosition;
            state.Forward=env.InvViewAt0.Forward;
            if(state.History && Transitions.Jump(state.PreviousPosition,state.PreviousForward,state.Position,state.Forward)) state.History=false;
        }

        // Before the cull results of this eye's scene are released.
        internal static void CollectObjects()
        {
            if(objectsOff || !Live || !Common.Config.DlssObjectMotion) return;
            var state=eyes[StereoRenderState.View];
            try
            {
                var query=mainCullQuery.Invoke(cullManager.GetValue(null),null);
                if(query==null) return;
                DlssObjects.Collect((List<VRage.Render.Scene.MyManualCullTreeData>)cullGroups.GetValue(query),state.Position,state.InverseJittered,tick);
                state.Objects=true;
            }
            catch(Exception ex)
            {
                objectsOff=true; DlssObjects.Clear();
                Logger.Warning("DLSS object motion off: "+ex.GetType().Name+": "+ex.Message);
            }
        }

        // SE borrows the FXAA target at the renderer resolution; from there to the end of DrawGameScene the eye works at the output size.
        internal static void BeforeBorrow(string name)
        {
            if(name!=FxaaTarget || output==render || !Live) return;
            EyeResolution.Scene.Apply(output);
            switched=true;
        }
        internal static void AfterScene()
        {
            if(!switched) return;
            switched=false;
            EyeResolution.Scene.Apply(render);
        }

        // Replaces MyFXAA.Run for the current eye; false leaves native FXAA to run.
        internal static bool Replace(object rc,object destination,object source)
        {
            if(!Live) return false;
            var state=eyes[StereoRenderState.View];
            state.Applied=false;
            try
            {
                var color=(Resource)CockpitRender.Member(source,"Resource");
                var target=(Resource)CockpitRender.Member(destination,"Resource");
                var resolvedDepth=PhysicalSurface.ResolvedDepth();
                var depth=(Resource)CockpitRender.Member(resolvedDepth,"Resource");
                var colorTexture=color as Texture2D;
                var targetTexture=target as Texture2D;
                if(colorTexture==null || colorTexture.Description.Width!=render.X || colorTexture.Description.Height!=render.Y)
                    throw new InvalidOperationException("DLSS color size does not match the eye");
                if(targetTexture==null || targetTexture.Description.Width!=output.X || targetTexture.Description.Height!=output.Y)
                    throw new InvalidOperationException("DLSS output size does not match the eye target");
                var depthView=PhysicalSurface.SceneDepth(resolvedDepth);
                var previous=state.History ? Previous(state.PreviousUnjittered,state.Position,state.PreviousPosition) : state.Unjittered;
                var motion=DlssMotion.Generate(Device,depthView,render.X,render.Y,state.InverseJittered,state.Unjittered,previous,state.Objects && state.History);
                bool ok=Ngx.Evaluate(state.Feature,color,depth,motion,target,pixels.X,pixels.Y,!state.History,render.X,render.Y);
                if(ok)
                {
                    state.History=state.Evaluated=true; state.Failures=0;
                    state.PreviousUnjittered=state.Unjittered; state.PreviousPosition=state.Position; state.PreviousForward=state.Forward;
                    if(output!=render)
                    {
                        var shift=Shift(Ndc(pixels,render),render,state.Projection.M34);
                        sceneDepth.Run(Device,depthView,output.X,output.Y,render,shift);
                        var hand=NativeHandLayer.Raw;
                        if(hand!=null) handDepth.Run(Device,hand,output.X,output.Y,render,shift);
                    }
                }
                else
                {
                    state.History=false;
                    if(++state.Failures>=3) Block("DLSS off after repeated NGX errors");
                    else Logger.Warning("DLSS evaluate failed: "+Ngx.LastError);
                }
                return ok;
            }
            catch(Exception ex)
            {
                Block("DLSS off: "+ex.GetType().Name+": "+ex.Message);
                return false;
            }
            finally { ResetEngineState(rc); }
        }

        // ClearState also unbinds the device; later passes expect the frame constants and samplers bound earlier.
        private static void ResetEngineState(object rc)
        {
            new MyRenderContext(rc).ClearState();
            setConstantBuffer.Invoke(allStages.Invoke(rc,null),new[] {(object)0,frameConstants.Invoke(null,null)});
            setSamplers.Invoke(pixelStage.Invoke(rc,null),new[] {(object)0,standardSamplers.GetValue(null)});
        }

        // After an upscale, overlays depth-test against scene and hand depth resampled the same way.
        private static bool Upscaled => active && output!=render && StereoRenderState.PhysicalEye && eyes[StereoRenderState.View].Evaluated;
        internal static ShaderResourceView OverlayDepth => Upscaled ? sceneDepth.View : null;
        internal static ShaderResourceView HandDepth(ShaderResourceView raw) => raw!=null && Upscaled ? handDepth.View : raw;

        // Billboards after anti-aliasing need unjittered frame constants and, after an upscale, depth at the eye target size.
        internal static void BeforePostPp(object[] args)
        {
            if(!active || !StereoRenderState.PhysicalEye || !eyes[StereoRenderState.View].Evaluated) return;
            MyRender11.Environment_Matrices.SetProjection(eyes[StereoRenderState.View].Projection);
            updateFrameConstants.Invoke(null,null);
            if(!Upscaled || sceneDepth.View==null) return;
            try
            {
                if(outputDepth==null || outputDepthSize!=output)
                {
                    ReleaseOutputDepth();
                    var current=(Texture2D)CockpitRender.Member(PhysicalSurface.ResolvedDepth(),"Resource");
                    bool hq=current.Description.Format==SharpDX.DXGI.Format.R32G8X24_Typeless;
                    outputDepth=createDepth.Invoke(depthStencils.GetValue(null),new object[] {"SEVR.DlssOutputDepth",output.X,output.Y,hq,1,0});
                    outputDepthSize=output;
                }
                var dsv=(DepthStencilView)CockpitRender.Member(CockpitRender.Member(outputDepth,"Dsv"),"Dsv");
                DlssMotion.WriteDepth(Device,sceneDepth.View,dsv,output.X,output.Y);
                ResetEngineState(args[0]);
                var main=gbuffer.GetValue(null);
                sceneResolvedDepth=resolvedDepth.GetValue(main);
                resolvedDepth.SetValue(main,outputDepth);
                args[1]=CockpitRender.Member(outputDepth,"SrvDepth");
            }
            catch(Exception ex) { RestoreDepth(); ResetEngineState(args[0]); Block("DLSS off: "+ex.GetType().Name+": "+ex.Message); }
        }
        internal static void RestoreDepth()
        {
            if(sceneResolvedDepth==null) return;
            resolvedDepth.SetValue(gbuffer.GetValue(null),sceneResolvedDepth);
            sceneResolvedDepth=null;
        }
        private static void ReleaseOutputDepth()
        {
            if(outputDepth==null) return;
            disposeDepth.Invoke(depthStencils.GetValue(null),new[] {outputDepth});
            outputDepth=null;
        }

        // Holograms always clip dither texel 0; raising it to 1/255 lets them draw without holes.
        private static void SetSolidDither(bool solid)
        {
            var current=MyRender11.DeviceInstance;
            var texture=current==null ? null : ditherTexture.GetValue(null);
            if(texture==null) return;
            var view=(ShaderResourceView)ditherView.GetValue(texture);
            if(!solid || (solidDither!=null && solidDither.Device.NativePointer!=current.NativePointer))
            {
                if(view==solidDitherView && view!=null) { ditherResource.SetValue(texture,engineDither); ditherView.SetValue(texture,engineDitherView); }
                solidDitherView?.Dispose(); solidDither?.Dispose(); solidDitherView=null; solidDither=null;
                if(!solid) return;
            }
            if(view==solidDitherView) return;
            if(solidDither==null)
            {
                var values=new byte[64];
                for(int y=0;y<8;y++) for(int x=0;x<8;x++) values[y*8+x]=(byte)Math.Max(1,Bayer(x,y)*4);
                var handle=System.Runtime.InteropServices.GCHandle.Alloc(values,System.Runtime.InteropServices.GCHandleType.Pinned);
                try
                {
                    solidDither=new Texture2D(current,new Texture2DDescription
                    {
                        Width=8,Height=8,MipLevels=1,ArraySize=1,Format=SharpDX.DXGI.Format.R8_UNorm,SampleDescription=new SharpDX.DXGI.SampleDescription(1,0),
                        Usage=ResourceUsage.Immutable,BindFlags=BindFlags.ShaderResource
                    },new SharpDX.DataRectangle(handle.AddrOfPinnedObject(),8));
                }
                finally { handle.Free(); }
                solidDitherView=new ShaderResourceView(current,solidDither);
            }
            engineDither=(Resource)ditherResource.GetValue(texture); engineDitherView=view;
            ditherResource.SetValue(texture,solidDither); ditherView.SetValue(texture,solidDitherView);
        }

        // Plugin unload only; NGX itself is left to the process. Shutting it down during engine teardown faults.
        internal static void Dispose()
        {
            active=false;
            RestoreDepth(); ReleaseOutputDepth(); SetSolidDither(false);
            sceneDepth.Dispose(); handDepth.Dispose(); DlssMotion.Dispose();
        }
    }
#endif
}
