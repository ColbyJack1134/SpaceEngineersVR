using System;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using HarmonyLib;
using SharpDX.Direct3D11;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Diagnostics
{
    // An isolated main-menu scene in an explicitly launched diagnostic process. No world/save is loaded.
    internal static class PhysicalRendererProbe
    {
        private static uint native=uint.MaxValue;
        private static int phase;
        private static AssignmentPreview assignment;
        private static bool rotationPreviewsSaved;
        private static readonly Vector3 neutralPaint=new Vector3(0,-.8f,-.13f);
        private static DateTime next,deadline;
        private static volatile string pending;
        private static volatile string captured;
        private static volatile string renderError;
        private static MyRenderMessageSetCameraViewMatrix camera;
        private static readonly string output=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SEVRPrototype","Reports","physical-renderer");
        public static bool Active { get; private set; }
        public static void Start(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"DrawScene"),new HarmonyMethod(typeof(PhysicalRendererProbe),nameof(Render)));
            Active=true; phase=0; next=DateTime.UtcNow.AddSeconds(10); deadline=DateTime.UtcNow.AddSeconds(80);
            Directory.CreateDirectory(output);
            foreach (string file in new[] {"native-left.png","rest-left.png","rest-right.png","articulated-left.png","articulated-right.png","restored-left.png","regrab-left.png"})
                if (File.Exists(Path.Combine(output,file))) File.Delete(Path.Combine(output,file));
        }
        public static void Update()
        {
            if (!Active) return;
            try
            {
                if (MySession.Static!=null) throw new InvalidOperationException("Renderer probe requires the main menu, without a loaded world.");
                if (renderError!=null) throw new InvalidOperationException(renderError);
                if (DateTime.UtcNow>deadline) throw new TimeoutException("Native renderer probe timed out in phase "+phase);
                if (phase==0)
                {
                    if (DateTime.UtcNow<next) return;
                    MenuTests.Run(line=>Logger.Info(line));
                    PlacementTests.RunNativeFixture(line=>Logger.Info(line));
                    NativeIntegrationTests.Run(line=>Logger.Info(line));
                    BuildOrientationTests.RunNative(line=>Logger.Info(line));
                    native=MyRenderProxy.CreateRenderEntity("SEVR probe interior",FighterProfile.Model,MatrixD.Identity,MyMeshDrawTechnique.MESH,
                        RenderFlags.Visible|RenderFlags.CastShadows,(CullingOptions)0,Color.White,neutralPaint);
                    assignment=new AssignmentPreview(); Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(assignment);
                    phase=1; next=DateTime.UtcNow.AddSeconds(6);
                }
                bool right=phase==4 || phase==6 || phase==11 || phase==13;
                Vector3D eye=new Vector3D(0.00577+(right ? 0.032 : -0.032),0.43444,0.73861);
                MatrixD view=MatrixD.CreateLookAt(eye,eye+new Vector3D(0,-1.3,-1),Vector3D.Up);
                var size=Wrappers.MyRender11.Resolution;
                float aspect=size.Y>0 ? (float)size.X/size.Y : 1.77f;
                Matrix projection=(Matrix)VrMath.Projection(-aspect*0.72f,aspect*0.72f,-0.72f,0.72f,0.03,100);
                MyRenderProxy.SetCameraViewMatrix(view,projection,projection,1.3f,1.3f,0.03f,100,100,eye,smooth:false);
                camera=new MyRenderMessageSetCameraViewMatrix { ViewMatrix=view,ProjectionMatrix=projection,ProjectionFarMatrix=projection,
                    FOV=1.3f,FOVForSkybox=1.3f,NearPlane=0.03f,FarPlane=100,FarFarPlane=100,CameraPosition=eye,Smooth=false };
                if (phase>=2 && phase!=7 && phase!=15)
                {
                    bool moved=phase==5 || phase==6 || phase==12 || phase==13;
                    Matrix l=moved ? CockpitStickMath.LeftVisual(new Vector3(0.5f,0.4f,-0.5f)) : Matrix.Identity;
                    Matrix r=moved ? CockpitStickMath.RightVisual(new Vector3(0.5f,0.4f,0.6f)) : Matrix.Identity;
                    Vector3 lo=phase>=10 && phase<14 ? new Vector3(.07f,.10f,.10f) : Vector3.Zero;
                    Vector3 ro=phase>=10 && phase<14 ? new Vector3(-.07f,.12f,.08f) : Vector3.Zero;
                    CockpitRender.UpdateScene(native,MatrixD.Identity,StickPlacement.Visual(l,lo),StickPlacement.Visual(r,ro),moved,moved,lo,ro,moved ? 1f : 0f,moved ? 1f : (float?)null,colorMask:phase>=14 ? new Vector3(.58f,0,.02f) : neutralPaint);
                }
                if(phase>=14) MyRenderProxy.UpdateRenderEntity(native,null,new Vector3(.58f,0,.02f));
                MyRenderProxy.UpdateRenderObject(native,MatrixD.Identity);
                MyRenderProxy.Draw3DScene();
                if (DateTime.UtcNow<next) return;
                if (pending!=null)
                {
                    if (captured!=pending) return;
                    pending=null;
                    phase++;
                    next=DateTime.UtcNow.AddSeconds(2);
                    if(phase==2) assignment.VerifyAndPage();
                    if(phase==4) { assignment.Finish(); assignment=null; }
                    if (phase==7 || phase==15) CockpitRender.Reset();
                    if (phase==16)
                    {
                        if(!rotationPreviewsSaved) throw new InvalidOperationException("Native rotation previews not rendered");
                        ValidateImages();
                        Stop(); Logger.Info("PHYSICAL RENDER SMOKE PASSED: native material suppression, six stick actors plus preserved Chrome/interior and thirteen animated levers/covers, articulation, independently relocated bases/handles, 64 mm left/right scene views, removal/restoration and recreation. Images: "+output);
                    }
                    return;
                }
                if (phase==2 || phase==8)
                {
                    if (!CockpitRender.Ready) return;
                    phase++; next=DateTime.UtcNow.AddSeconds(2); return;
                }
                string name=phase==1 ? "native-left" : phase==3 ? "rest-left" : phase==4 ? "rest-right" : phase==5 ? "articulated-left" :
                    phase==6 ? "articulated-right" : phase==7 ? "restored-left" : phase==9 ? "regrab-left" :
                    phase==10 ? "relocated-left" : phase==11 ? "relocated-right" : phase==12 ? "relocated-articulated-left" : phase==13 ? "relocated-articulated-right" : phase==14 ? "repainted-left" : "repainted-native-left";
                if(phase==1 || phase==3) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"assignment-page-"+(phase==1 ? "1" : "2")+".png"),false,false,false);
                pending=Path.Combine(output,name+".png");
                next=DateTime.UtcNow.AddSeconds(1);
            }
            catch(Exception ex) { Stop(); Logger.Warning(ex,"PHYSICAL RENDER SMOKE FAILED"); }
        }
        public static void Render()
        {
            if(Active && !rotationPreviewsSaved && BuildOrientationTests.Previews.Count==3) RenderRotationPreviews();
            string path=pending;
            if (!Active || path==null || captured==path || renderError!=null) return;
            try
            {
                var size=Wrappers.MyRender11.Resolution;
                AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"SetupCameraMatrices").Invoke(null,new object[] {camera});
                // Main-menu fixtures have no world environment to initialize material multipliers.
                var environment=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyRender11"),"Environment").GetValue(null);
                var dataField=AccessTools.Field(environment.GetType(),"Data");
                object data=dataField.GetValue(environment);
                AccessTools.Field(data.GetType(),"TextureMultipliers").SetValue(data,MyTextureDebugMultipliers.Defaults);
                dataField.SetValue(environment,data);
                var target=Wrappers.MyManagers.RwTexturesPool.BorrowRtv("SEVR physical probe",size.X,size.Y,SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);
                try
                {
                    object ao=null;
                    try { Wrappers.MyRender11.DrawGameScene(target,out ao); }
                    finally { if (ao!=null) new Wrappers.BorrowedRtvTexture(ao).Release(); }
                    // Exercise the installed engine's depth SRV and our spatial shader in a real scene.
                    var physicalTarget=(Texture2D)target.GetResource();
                    MatrixD surfacePose=MatrixD.CreateTranslation(0,-.10,-.5)*MatrixD.Invert(camera.ViewMatrix);
                    PhysicalSurface.Draw(physicalTarget,new[] { new SurfaceView { Id="Physical probe",Title="SEAT FIT",Text="Scene depth probe",Width=.24f,Height=.18f,Pose=surfacePose,
                        Keys=new[] { new SurfaceKey("UP",.08f,.40f,.38f,.35f),new SurfaceKey("DOWN",.54f,.40f,.38f,.35f) } } },camera.ViewMatrix,camera.ProjectionMatrix,PhysicalSurface.SceneDepth());
                    // The main menu has no world lighting. Inspect actual rasterized albedo/depth occlusion.
                    var buffer=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main").GetValue(null);
                    var texture=(Texture2D)CockpitRender.Member(CockpitRender.Member(buffer,"GBuffer0"),"Resource");
                    var description=texture.Description;
                    description.BindFlags=BindFlags.None; description.Usage=ResourceUsage.Staging;
                    description.CpuAccessFlags=CpuAccessFlags.Read; description.OptionFlags=ResourceOptionFlags.None;
                    using (var staging=new Texture2D(texture.Device,description))
                    {
                        var context=texture.Device.ImmediateContext;
                        context.CopyResource(texture,staging);
                        var mapped=context.MapSubresource(staging,0,MapMode.Read,MapFlags.None);
                        try
                        {
                            using(var bitmap=new System.Drawing.Bitmap(description.Width,description.Height,PixelFormat.Format32bppArgb))
                            {
                                var pixels=bitmap.LockBits(new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                                try
                                {
                                    var row=new byte[bitmap.Width*4];
                                    for(int y=0;y<bitmap.Height;y++)
                                    {
                                        Marshal.Copy(IntPtr.Add(mapped.DataPointer,y*mapped.RowPitch),row,0,row.Length);
                                        for(int x=0;x<row.Length;x+=4)
                                        {
                                            byte red=row[x]; row[x]=row[x+2]; row[x+2]=red; row[x+3]=255;
                                        }
                                        Marshal.Copy(row,0,IntPtr.Add(pixels.Scan0,y*pixels.Stride),row.Length);
                                    }
                                }
                                finally { bitmap.UnlockBits(pixels); }
                                int lit=0;
                                for(int y=0;y<bitmap.Height;y+=8) for(int x=0;x<bitmap.Width;x+=8)
                                { var pixel=bitmap.GetPixel(x,y); if(pixel.R+pixel.G+pixel.B>30) lit++; }
                                if(lit<100)
                                {
                                    throw new InvalidOperationException("Native probe scene is blank; proxy validation alone is not a render test.");
                                }
                                bitmap.Save(path,ImageFormat.Png);
                                captured=path;
                            }
                        }
                        finally { context.UnmapSubresource(staging,0); }
                    }
                }
                finally { target.Release(); }
            }
            catch(Exception ex) { renderError=ex.ToString(); }
        }
        private static void RenderRotationPreviews()
        {
            try
            {
                NativeSprites.Poll();
                using(var canvas=new OverlayCanvas("native rotation preview",384,384,1,false))
                {
                    for(int i=0;i<BuildOrientationTests.Previews.Count;i++)
                    {
                        BuildOrientationHud.Paint(canvas,BuildOrientationTests.Previews[i]); canvas.Upload();
                        if(NativeSprites.Pending) return;
                        UiTests.Save(canvas.Texture,Path.Combine(output,"native-rotation-"+i+".png"));
                    }
                    using(var scene=new OverlayCanvas("rotation eye preview",1024,768,1,false))
                    using(var texture=new ShaderResourceView(canvas.Texture.Device,canvas.Texture))
                    {
                        foreach(int eye in new[] {-1,1})
                        {
                            scene.Clear(System.Drawing.Color.FromArgb(255,12,20,28)); scene.Upload();
                            float half=BuildOrientationHud.Width/2;
                            var projection=VrMath.Projection(eye<0 ? -1.1f : -.9f,eye<0 ? .9f : 1.1f,-1,1,.03);
                            NativeSprites.Draw(scene.Texture,new[] { PhysicalSurface.Quad(texture,BuildOrientationHud.Mount,
                                new RectangleF(-half,half,2*half,2*half),new Vector4(0,0,1,1),Vector4.One,MatrixD.CreateTranslation(-eye*.032,0,0),projection) });
                            UiTests.Save(scene.Texture,Path.Combine(output,"rotation-eye-"+eye+".png"));
                        }
                    }
                }
                rotationPreviewsSaved=true;
            }
            catch(Exception ex) { renderError=ex.ToString(); }
        }
        private static void ValidateImages()
        {
            if(Difference("repainted-left","repainted-native-left")>10 || Difference("native-left","repainted-native-left")<100)
                throw new InvalidOperationException("Replacement paint does not match live native repaint");
            foreach(string restored in new[] {"rest-left","restored-left","regrab-left"})
                if(Difference("native-left",restored)>10) throw new InvalidOperationException(restored+" differs from original geometry at rest.");
            foreach(string eye in new[] {"left","right"})
            {
                if(Difference("rest-"+eye,"articulated-"+eye)<100) throw new InvalidOperationException("Articulation did not appear in "+eye+" view.");
                if(Difference("rest-"+eye,"relocated-"+eye)<100) throw new InvalidOperationException("Stick relocation did not appear in "+eye+" view.");
                if(Difference("relocated-"+eye,"relocated-articulated-"+eye)<100) throw new InvalidOperationException("Moved stick did not articulate in "+eye+" view.");
            }
            if(Difference("rest-left","rest-right")<100) throw new InvalidOperationException("Scene views have no stereo disparity.");
        }
        private static int Difference(string first,string second)
        {
            int changed=0;
            using(var a=new System.Drawing.Bitmap(Path.Combine(output,first+".png")))
            using(var b=new System.Drawing.Bitmap(Path.Combine(output,second+".png")))
            {
                for(int y=0;y<a.Height;y+=4) for(int x=0;x<a.Width;x+=4)
                {
                    var p=a.GetPixel(x,y); var q=b.GetPixel(x,y);
                    if(Math.Abs(p.R-q.R)+Math.Abs(p.G-q.G)+Math.Abs(p.B-q.B)>12) changed++;
                }
            }
            Logger.Info("Physical image difference "+first+" / "+second+": "+changed+" sampled pixels");
            return changed;
        }
        private static void Stop()
        {
            if(assignment!=null) { assignment.Finish(); assignment=null; }
            CockpitRender.Reset();
            if (native!=uint.MaxValue) MyRenderProxy.RemoveRenderObject(native,MyRenderProxy.ObjectType.Entity);
            native=uint.MaxValue; Active=false;
        }
    }
}
