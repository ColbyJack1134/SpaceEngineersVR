using System;
using System.IO;
using System.Linq;
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
    internal static partial class PhysicalRendererProbe
    {
        private sealed class CameraHudPreview : GUI.MyPluginConfigDialog
        {
            private Sandbox.Game.GUI.MyHudCameraOverlay overlay;
            private Action drawCrosshair;
            private Sandbox.Game.Gui.MyGuiScreenHudBase markerOwner;
            private Sandbox.Game.Gui.MyHud previewHud;
            private readonly Sandbox.Game.Screens.MyHudWeaponHitIndicator hit=new Sandbox.Game.Screens.MyHudWeaponHitIndicator();
            public override bool Draw()
            {
                bool drawn=base.Draw();
                if(overlay==null)
                {
                    overlay=new Sandbox.Game.GUI.MyHudCameraOverlay();
                    Sandbox.Game.Gui.MyGuiScreenHudBase.LoadTextureAtlas(out var atlas,out var coordinates);
                    var crosshair=new Sandbox.Game.Gui.MyHudCrosshair(); crosshair.Recenter();
                    drawCrosshair=()=>crosshair.Draw(atlas,coordinates);
                }
                Sandbox.Game.GUI.MyHudCameraOverlay.Enabled=true;
                Sandbox.Game.GUI.MyHudCameraOverlay.TextureName=@"Textures\GUI\Screens\camera_overlay.dds";
                overlay.Draw(1,1); drawCrosshair();
                var hudField=HarmonyLib.AccessTools.Field(typeof(Sandbox.Game.Gui.MyHud),"m_Static");
                object previousHud=hudField.GetValue(null);
                var cameraProperty=HarmonyLib.AccessTools.Property(typeof(MySector),nameof(MySector.MainCamera));
                var previousCamera=MySector.MainCamera;
                try
                {
                    if(previousHud==null) { if(previewHud==null) previewHud=new Sandbox.Game.Gui.MyHud(); hudField.SetValue(null,previewHud); }
                    var camera=new VRage.Game.Utils.MyCamera(1f,new VRageRender.MyViewport(0,0,1920,1080));
                    camera.SetViewMatrix(MatrixD.Identity,false); cameraProperty.SetValue(null,camera,null);
                    hit.Hit(MySession.MyHitIndicatorTarget.Grid); hit.Update(); hit.GuiControlImage.Draw(1,1);
                    if(markerOwner==null) { markerOwner=new Sandbox.Game.Gui.MyGuiScreenHudBase(); markerOwner.LoadContent(); }
                    var markers=new Sandbox.Game.GUI.HudViewers.MyHudMarkerRender(markerOwner);
                    RemoteHud.BeginCollection(out bool collection);
                    try { markers.AddPOI(new Vector3D(180,0,-1000),new System.Text.StringBuilder("Camera marker"),VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner); }
                    finally { RemoteHud.EndCollection(collection); }
                    markers.Draw(); markerOwner.DrawTexts();
                }
                finally { hudField.SetValue(null,previousHud); cameraProperty.SetValue(null,previousCamera,null); }
                return drawn;
            }
        }
        private sealed class PausePreview : SpaceEngineers.Game.GUI.MyGuiScreenMainMenu
        {
            internal PausePreview() : base(true) { }
            public override void RecreateControls(bool constructor)
            {
                base.RecreateControls(constructor); Controls.Clear();
                AccessTools.Field(typeof(SpaceEngineers.Game.GUI.MyGuiScreenMainMenu),"m_elementGroup").SetValue(this,new Sandbox.Graphics.GUI.MyGuiControlElementGroup());
                var size=Sandbox.Graphics.GUI.MyGuiControlButton.GetVisualStyle(VRage.Game.MyGuiControlButtonStyleEnum.Default).NormalTexture.MinSizeGui;
                var origin=Sandbox.Graphics.MyGuiManager.ComputeFullscreenGuiCoordinate(VRage.Utils.MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_BOTTOM)
                    +new Vector2(size.X/2,0)+new Vector2(15,0)/Sandbox.Graphics.GUI.MyGuiConstants.GUI_OPTIMAL_SIZE;
                origin.Y+=.043f;
                AccessTools.Method(typeof(SpaceEngineers.Game.GUI.MyGuiScreenMainMenu),"CreateInGameMenu").Invoke(this,new object[] {origin,null});
                Patches.PauseOptionsPatch.Add(this);
            }
        }
        private sealed class BlockPreview : Sandbox.Graphics.GUI.MyGuiScreenBase
        {
            private readonly Sandbox.Game.GUI.MyGuiProgressCompositeTextureAdvanced[] bars=new Sandbox.Game.GUI.MyGuiProgressCompositeTextureAdvanced[2];
            private readonly Sandbox.Graphics.GUI.MyGuiControlPanel[] backgrounds=new Sandbox.Graphics.GUI.MyGuiControlPanel[2];
            internal BlockPreview() : base(new Vector2(.5f),new Vector4(.03f,.05f,.07f,1),new Vector2(.85f,.8f)) { }
            public override string GetFriendlyName() => "Native block-bar reference";
            public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
            public override void RecreateControls(bool constructor)
            {
                base.RecreateControls(constructor); AddCaption("Native block integrity bar");
                for(int i=0;i<2;i++)
                {
                    backgrounds[i]=new Sandbox.Graphics.GUI.MyGuiControlPanel(new Vector2(i==0 ? -.16f:.16f,0),new Vector2(.05f,.185f),new Vector4(68/255f,77/255f,86/255f,.9f))
                        { BackgroundTexture=Sandbox.Graphics.GUI.MyGuiConstants.TEXTURE_COMPOSITE_BLOCKINFO_PROGRESSBAR };
                    Controls.Add(backgrounds[i]);
                    bars[i]=new Sandbox.Game.GUI.MyGuiProgressCompositeTextureAdvanced(Sandbox.Graphics.GUI.MyGuiConstants.TEXTURE_COMPOSITE_BLOCKINFO_PROGRESSBAR)
                        {IsInverted=true,Orientation=Sandbox.Game.GUI.MyGuiProgressCompositeTexture.BarOrientation.VERTICAL};
                    Controls.Add(new Sandbox.Graphics.GUI.MyGuiControlLabel(new Vector2(i==0 ? -.16f:.16f,-.14f),text:i==0 ? "48%":"80%",textScale:.8f,
                        originAlign:VRage.Utils.MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
                    Controls.Add(new Sandbox.Graphics.GUI.MyGuiControlLabel(new Vector2(i==0 ? -.16f:.16f,.14f),text:i==0 ? "Below functional":"Above functional",textScale:.6f,
                        originAlign:VRage.Utils.MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
                }
            }
            public override bool Draw()
            {
                bool result=base.Draw();
                for(int i=0;i<2;i++)
                {
                    bars[i].Position=new Vector2I(Sandbox.Graphics.MyGuiManager.GetScreenCoordinateFromNormalizedCoordinate(backgrounds[i].GetPositionAbsoluteTopLeft()));
                    bars[i].Size=new Vector2I(Sandbox.Graphics.MyGuiManager.GetScreenSizeFromNormalizedSize(backgrounds[i].Size));
                    bars[i].Draw(i==0 ? .48f:.8f,i==0 ? new Vector4(115/255f,69/255f,80/255f,1):new Vector4(122/255f,140/255f,154/255f,1));
                }
                return result;
            }
        }
        private static uint native=uint.MaxValue;
        private static int phase;
        private static CockpitRig[] rigs;
        private static int rigIndex,rigStep;
        private static readonly System.Collections.Generic.List<string> rigFailures=new System.Collections.Generic.List<string>();
        private static AssignmentPreview assignment;
        private static Sandbox.Graphics.GUI.MyGuiScreenBase options;
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
            RemoteHud.Install(harmony);
            if(LeadIndicatorTests.Enabled) LeadIndicatorTests.Install(harmony);
            harmony.Patch(AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),LeadIndicatorTests.Enabled ? "Present":"DrawScene"),new HarmonyMethod(typeof(PhysicalRendererProbe),nameof(Render)));
            Active=true; phase=0; next=DateTime.UtcNow.AddSeconds(10); deadline=DateTime.UtcNow.AddSeconds(520);
            Directory.CreateDirectory(output);
            foreach (string file in new[] {"native-left.png","rest-left.png","rest-right.png","articulated-left.png","articulated-right.png","restored-left.png","regrab-left.png"})
                if (File.Exists(Path.Combine(output,file))) File.Delete(Path.Combine(output,file));
        }
        private static CockpitRig[] SelectedRigs()
        {
            string subtype=Environment.GetEnvironmentVariable("SEVR_PHYSICAL_COCKPIT");
            var selected=CockpitRig.All.Where(r=>r.ActorCount>0 && (string.IsNullOrEmpty(subtype) || r.Subtype==subtype)).ToArray();
            if(selected.Length==0) throw new InvalidOperationException("Unknown cockpit probe subtype: "+subtype);
            return selected;
        }
        public static void Update()
        {
            if (!Active) return;
            try
            {
                if(Environment.GetEnvironmentVariable("SEVR_NATIVE_SIGNALS")=="1")
                {
                    if(renderError!=null) throw new InvalidOperationException(renderError);
                    NativeSignalProbe.Update(); return;
                }
                if (MySession.Static!=null) throw new InvalidOperationException("Renderer probe requires the main menu, without a loaded world.");
                if (renderError!=null) throw new InvalidOperationException(renderError);
                if (DateTime.UtcNow>deadline) throw new TimeoutException("Native renderer probe timed out in phase "+phase);
                if(Environment.GetEnvironmentVariable("SEVR_ITEM_GRABS")=="1") { UpdateItemGrabs(); return; }
                if(phase>=50) { UpdateRig(); return; }
                if (phase==0)
                {
                    if (DateTime.UtcNow<next) return;
                    if(Environment.GetEnvironmentVariable("SEVR_PHYSICAL_CAMERA_HUD_ONLY")=="1")
                    {
                        native=MyRenderProxy.CreateRenderEntity("SEVR camera probe",CockpitRig.Find(CockpitLayout.Fighter).Model,MatrixD.Identity,MyMeshDrawTechnique.MESH,
                            RenderFlags.Visible|RenderFlags.CastShadows,(CullingOptions)0,Color.White,neutralPaint);
                        phase=24; BeginCameraHud(); next=DateTime.UtcNow.AddSeconds(6); return;
                    }
                    if(Environment.GetEnvironmentVariable("SEVR_PHYSICAL_COCKPITS_ONLY")=="1")
                    {
                        rigs=SelectedRigs();
                        rigIndex=rigStep=0; phase=50; BeginRig(); return;
                    }
                    MenuTests.Run(line=>Logger.Info(line));
                    PlacementTests.RunNativeFixture(line=>Logger.Info(line));
                    NativeIntegrationTests.Run(line=>Logger.Info(line));
                    AnalogControlTests.RunNative(line=>Logger.Info(line));
                    BuildOrientationTests.RunNative(line=>Logger.Info(line));
                    CockpitHandTests.NativeContacts(line=>Logger.Info(line));
                    native=MyRenderProxy.CreateRenderEntity("SEVR probe interior",CockpitRig.Find(CockpitLayout.Fighter).Model,MatrixD.Identity,MyMeshDrawTechnique.MESH,
                        RenderFlags.Visible|RenderFlags.CastShadows,(CullingOptions)0,Color.White,neutralPaint);
                    assignment=new AssignmentPreview(); Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(assignment);
                    phase=1; next=DateTime.UtcNow.AddSeconds(6);
                }
                bool right=phase==4 || phase==6 || phase==11 || phase==13;
                Vector3D eye=new Vector3D(0.00577+(right ? 0.032 : -0.032),0.43444,0.73861);
                MatrixD view=MatrixD.CreateLookAt(eye,eye+new Vector3D(0,-1.3,-1),Vector3D.Up);
                if(phase>=16)
                {
                    eye=new Vector3D(-.2,.04,.26);
                    view=MatrixD.CreateLookAt(eye,new Vector3D(-.374,-.393,.12),Vector3D.Up);
                }
                var size=Wrappers.MyRender11.Resolution;
                float aspect=size.Y>0 ? (float)size.X/size.Y : 1.77f;
                float fov=phase>=16 ? .23f : .72f;
                float near=.03f;
                MatrixD modelWorld=MatrixD.Identity;
                if(phase>=20 && phase<29)
                {
                    var model=VRage.Game.Models.MyModels.GetModelOnlyData(CockpitRig.Find(CockpitLayout.Fighter).Model);
                    var miniature=new Player.Control.Diorama();
                    miniature.Fit(model.BoundingBox.Size.Length(),MatrixD.CreateRotationY(.3),new Vector3D(0,-.15,-1.15));
                    if(phase>=22)
                    {
                        var l=new Vector3D(-.15,-.2,-.6); var r=new Vector3D(.15,-.2,-.6);
                        miniature.Input(true,0,0); miniature.Input(true,1,1);
                        miniature.Move(MatrixD.CreateTranslation(l),MatrixD.CreateTranslation(r),0);
                        miniature.Move(MatrixD.CreateTranslation(l-new Vector3D(.1,0,0)),MatrixD.CreateTranslation(r+new Vector3D(.1,0,0)),0);
                    }
                    MatrixD reference=MatrixD.Identity;
                    if(phase>=26)
                    {
                        var follow=new Player.Control.ObserverFollow(); follow.Reset(MatrixD.Identity,Vector3D.Up);
                        var ship=MatrixD.CreateFromYawPitchRoll(.4,.3,-.25); follow.Advance(ship);
                        reference=follow.Reference((Player.Control.ObserverMode)(phase-26));
                        modelWorld=MatrixD.CreateTranslation(-model.BoundingBox.Center)*ship*MatrixD.CreateTranslation(model.BoundingBox.Center);
                        var l=MatrixD.CreateTranslation(-.2,-.1,-.5); var r=MatrixD.CreateTranslation(.2,-.1,-.5);
                        miniature.Input(true,0,0); miniature.Input(true,1,1); miniature.Move(l,r,0);
                        var turn=MatrixD.CreateRotationX(.2)*MatrixD.CreateRotationZ(.15);
                        var pivot=MatrixD.CreateTranslation(0,-.1,-.5);
                        miniature.Move(l*MatrixD.Invert(pivot)*turn*pivot,r*MatrixD.Invert(pivot)*turn*pivot,0);
                    }
                    view=VrMath.EyeView(MatrixD.Invert(miniature.Anchor(model.BoundingBox.Center,reference)),Matrix.Identity,Matrix.Identity,
                        Matrix.CreateTranslation(phase>=26 || phase%2==0 ? -.032f : .032f,0,0),miniature.UnitsPerMeter);
                    eye=MatrixD.Invert(view).Translation; near=(float)(.005*miniature.UnitsPerMeter); fov=.7f;
                }
                if(phase>=29)
                {
                    eye=new Vector3D(.29,-.29,.66);
                    view=MatrixD.CreateLookAt(eye,new Vector3D(.40,-.44,.60),Vector3D.Up); fov=.22f;
                }
                if(phase>=31)
                {
                    eye=phase<33 ? new Vector3D(.02,.02,.28) : new Vector3D(.11,-.12,.04);
                    view=MatrixD.CreateLookAt(eye,phase<33 ? new Vector3D(0,-.4,-.26) : new Vector3D(.32,-.355,-.23),Vector3D.Up);
                    fov=phase<33 ? .35f : .30f;
                }
                if(phase>=35)
                {
                    eye=new Vector3D(.16,-.07,.16);
                    view=MatrixD.CreateLookAt(eye,CockpitRig.Find(CockpitLayout.Fighter).Bars[0].Front,Vector3D.Up); fov=.25f;
                }
                if(phase>=45 && phase<=46)
                {
                    eye=new Vector3D(phase==45 ? -.35:.35,.23,.42);
                    view=MatrixD.CreateLookAt(eye,new Vector3D(0,0,-.04),Vector3D.Up); fov=.30f;
                }
                if(phase==46)
                {
                    var glove=GloveGeometry.Load(VRage.FileSystem.MyFileSystem.ContentPath,GloveGeometry.DefaultModel,true);
                    var mount=glove.WristMount*CockpitHandPose.GripWrist(Matrix.CreateTranslation(-.11f,0,0));
                    var panel=SpatialUi.WristPose(mount,1,.225f,-1);
                    var center=(mount.Translation+panel.Translation)*.5;
                    eye=center+mount.Right*.75f+mount.Backward*.20f;
                    view=MatrixD.CreateLookAt(eye,center,mount.Backward); fov=.4f;
                }
                if(phase==48 || phase==49)
                {
                    var model=VRage.Game.Models.MyModels.GetModelOnlyData(@"Models\Characters\Astronaut\SE_astronaut.mwm");
                    var miniature=new Player.Control.Diorama();
                    miniature.Fit(model.BoundingBox.Size.Length(),MatrixD.Identity,new Vector3D(0,-.2,-1.15));
                    view=VrMath.EyeView(MatrixD.Invert(miniature.Anchor(model.BoundingBox.Center)),Matrix.Identity,Matrix.Identity,Matrix.Identity,miniature.UnitsPerMeter);
                    eye=MatrixD.Invert(view).Translation; near=(float)(.005*miniature.UnitsPerMeter); fov=.7f;
                }
                Matrix projection=(Matrix)VrMath.Projection(-aspect*fov,aspect*fov,-fov,fov,near,100);
                MyRenderProxy.SetCameraViewMatrix(view,projection,projection,1.3f,1.3f,near,100,100,eye,smooth:false);
                camera=new MyRenderMessageSetCameraViewMatrix { ViewMatrix=view,ProjectionMatrix=projection,ProjectionFarMatrix=projection,
                    FOV=1.3f,FOVForSkybox=1.3f,NearPlane=near,FarPlane=100,FarFarPlane=100,CameraPosition=eye,Smooth=false };
                if (phase>=2 && phase<45 && phase!=7 && phase!=15)
                {
                    bool moved=phase==5 || phase==6 || phase==12 || phase==13;
                    Matrix l=moved ? CockpitRig.Find(CockpitLayout.Fighter).Left.Visual(new Vector3(0.5f,0.4f,0.5f)) : Matrix.Identity;
                    Matrix r=moved ? CockpitRig.Find(CockpitLayout.Fighter).Right.Visual(new Vector3(0.5f,0.4f,0.6f)) : Matrix.Identity;
                    Vector3 lo=phase>=10 && phase<14 ? new Vector3(.07f,.10f,.10f) : Vector3.Zero;
                    Vector3 ro=phase>=10 && phase<14 ? new Vector3(-.07f,.12f,.08f) : Vector3.Zero;
                    CockpitRender.UpdateScene(CockpitRig.Find(CockpitLayout.Fighter),native,modelWorld,StickPlacement.Visual(l,lo),StickPlacement.Visual(r,ro),moved,moved,lo,ro,moved || phase==30 || phase==32 || phase==34 ? 1f : 0f,phase>=31 ? phase==32 ? 1f : 0f : moved || phase>=16 && phase<=19 ? 1f : (float?)null,colorMask:phase>=14 ? new Vector3(.58f,0,.02f) : neutralPaint,
                        previewHover:phase==17 ? 0 : phase==18 ? 9 : -1,previewHeld:phase==17 ? 1 : phase==18 ? 10 : phase==30 ? 19 : -1,previewCover:phase==18,nativeRest:phase<16 && !moved,barPreview:phase==36 ? 1f : 0f);
                }
                if(phase>=14 && native!=uint.MaxValue) MyRenderProxy.UpdateRenderEntity(native,null,new Vector3(.58f,0,.02f));
                if(native!=uint.MaxValue) MyRenderProxy.UpdateRenderObject(native,modelWorld);
                MyRenderProxy.Draw3DScene();
                if (DateTime.UtcNow<next) return;
                if (pending!=null)
                {
                    if (captured!=pending) return;
                    pending=null;
                    phase++;
                    next=DateTime.UtcNow.AddSeconds(2);
                    if(phase==2) assignment.VerifyAndPage();
                    if(phase==4) { assignment.Finish(); assignment=new AssignmentPreview(ordinary:true); Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(assignment); }
                    if(phase==6) assignment.VerifyAndPage();
                    if(phase==7) { assignment.Finish(); assignment=null; }
                    if (phase==7 || phase==15) CockpitRender.Reset();
                    if(phase==24)
                    {
                        BeginCameraHud();
                    }
                    if(phase==25)
                    {
                        RemoteHud.Fixture=null;
                        Sandbox.Game.GUI.MyHudCameraOverlay.Enabled=false;
                        Sandbox.Game.GUI.MyHudCameraOverlay.TextureName=null;
                        if(Environment.GetEnvironmentVariable("SEVR_PHYSICAL_CAMERA_HUD_ONLY")=="1")
                        { Stop(); Logger.Info("PHYSICAL RENDER SMOKE PASSED: selective native camera HUD and scene resource isolation only."); return; }
                        options.CloseScreenNow(); EyeResolution.Recommend(2112,2304);
                        var display=Sandbox.Engine.Platform.VideoMode.MyVideoSettingsManager.CurrentDeviceSettings;
                        display.BackBufferWidth=1920; display.BackBufferHeight=1080; display.WindowMode=VRage.MyWindowModeEnum.Window;
                        Sandbox.Engine.Platform.VideoMode.MyVideoSettingsManager.Apply(display);
                        options=new GUI.RenderingOptions(); Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(options);
                    }
                    if(phase==26) { options.CloseScreenNow(); options=null; }
                    if(phase==33) { options=new GUI.BindingHelp(); Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(options); }
                    if(phase==34 || phase==35)
                    {
                        HarmonyLib.AccessTools.Field(typeof(GUI.BindingHelp),"page").SetValue(options,phase==34 ? 2:3);
                        options.RecreateControls(false);
                    }
                    if(phase==36) { options?.CloseScreenNow(); options=null; }
                    if(phase>=37 && phase<=44)
                    {
                        options?.CloseScreenNow();
                        options=phase==42 ? (Sandbox.Graphics.GUI.MyGuiScreenBase)new GUI.BodyOptions() : phase==38 ? (Sandbox.Graphics.GUI.MyGuiScreenBase)new GUI.FlightOptions() :
                            phase==43 ? (Sandbox.Graphics.GUI.MyGuiScreenBase)new GUI.SettingsPage("Signals") : phase==44 ? (Sandbox.Graphics.GUI.MyGuiScreenBase)new GUI.SettingsPage("Signal ranges") :
                            (Sandbox.Graphics.GUI.MyGuiScreenBase)new GUI.SettingsPage(phase==37 ? "Character" : phase==39 ? "Third person" : phase==40 ? "Release glide" : phase==41 ? "HUD & Interface" : "Advanced controls");
                        Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(options);
                    }
                    if(phase==45)
                    {
                        options?.CloseScreenNow(); options=null; CockpitRender.Reset();
                        MyRenderProxy.RemoveRenderObject(native,MyRenderProxy.ObjectType.Entity); native=uint.MaxValue;
                        NativeGloves.Create(GloveGeometry.DefaultModel,new Vector3(.58f,-.25f,0));
                    }
                    if(phase==47)
                    {
                        NativeGloves.Reset();
                        options=new PausePreview();
                        Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(options);
                    }
                    if(phase==48)
                    {
                        options?.CloseScreenNow(); options=null;
                        native=MyRenderProxy.CreateRenderEntity("SEVR observer character",@"Models\Characters\Astronaut\SE_astronaut.mwm",MatrixD.Identity,MyMeshDrawTechnique.MESH,
                            RenderFlags.Visible|RenderFlags.CastShadows,(CullingOptions)0,Color.White,neutralPaint);
                    }
                    if(phase==49)
                    {
                        options=new GUI.PhysicalFlightOptions(); Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(options);
                    }
                    if (phase==50)
                    {
                        options?.CloseScreenNow(); options=null;
                        if(!rotationPreviewsSaved) throw new InvalidOperationException("Native rotation previews not rendered");
                        ValidateImages();
                        if(Environment.GetEnvironmentVariable("SEVR_PHYSICAL_INTERFACE_ONLY")=="1")
                        { Stop(); Logger.Info("PHYSICAL RENDER SMOKE PASSED: native interface, camera HUD, hand layer and reference cockpit. Rig sweep excluded."); return; }
                        rigs=SelectedRigs(); rigIndex=rigStep=0; BeginRig();
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
                    phase==10 ? "relocated-left" : phase==11 ? "relocated-right" : phase==12 ? "relocated-articulated-left" : phase==13 ? "relocated-articulated-right" : phase==14 ? "repainted-left" : phase==15 ? "repainted-native-left" : phase==16 ? "controls-rest" : phase==17 ? "controls-levers" : phase==18 ? "controls-covers" : phase==19 ? "controls-restored" :
                    phase==20 ? "miniature-left" : phase==21 ? "miniature-right" : phase==22 ? "miniature-enlarged-left" : phase==23 ? "miniature-enlarged-right" :
                    phase==48 ? "character-observer" : phase>=45 ? "gloves-scene-"+phase : phase>=37 ? "settings-scene-"+phase : phase==35 ? "bar-rest" : phase==36 ? "bar-pulled" :
                    phase==31 ? "front-closed" : phase==32 ? "front-open" : phase==33 ? "front-right-rest" : phase==34 ? "front-right-on" :
                    phase==29 ? "uncovered-rest" : phase==30 ? "uncovered-on" :
                    phase>=26 ? "camera-"+(Player.Control.ObserverMode)(phase-26) : "rendering-scene-"+phase;
                if(phase==1 || phase==3) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"assignment-page-"+(phase==1 ? "1" : "last")+".png"),false,false,false);
                if(phase==5 || phase==6) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"toolbar-assignment-"+(phase==5 ? "selected":"paged")+".png"),false,false,false);
                if(phase>=33 && phase<=35) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"bindings-native-"+phase+".png"),false,false,false);
                if(phase==24 || phase==25) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,phase==24 ? "options-native.png" : "rendering-options-native.png"),false,false,false);
                if(phase>=37 && phase<=44) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"settings-native-"+phase+".png"),false,false,false);
                if(phase==49) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"physical-sticks-options.png"),false,false,false);
                if(phase==47) MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"pause-vr-options.png"),false,false,false);
                pending=Path.Combine(output,name+".png");
                next=DateTime.UtcNow.AddSeconds(1);
            }
            catch(Exception ex) { Stop(); Logger.Warning(ex,"PHYSICAL RENDER SMOKE FAILED"); }
        }
        private static void BeginRig()
        {
            CockpitRender.Reset();
            if(native!=uint.MaxValue) MyRenderProxy.RemoveRenderObject(native,MyRenderProxy.ObjectType.Entity);
            var rig=rigs[rigIndex];
            native=MyRenderProxy.CreateRenderEntity("SEVR rig probe "+rig.Subtype,rig.Model,MatrixD.Identity,MyMeshDrawTechnique.MESH,
                RenderFlags.Visible|RenderFlags.ForceOldPipeline|RenderFlags.CastShadows,(CullingOptions)0,Color.White,neutralPaint);
            foreach(string material in rig.Pieces.Select(p=>p.Material).Distinct().Where(m=>CockpitRender.MovingScreenActor(rig,m)>=0 && m.StartsWith("CockpitScreen_")))
                MyRenderProxy.ChangeMaterialTexture(native,material,@"Textures\Models\default_online.dds");
            next=DateTime.UtcNow.AddSeconds(2);
        }
        private static void UpdateRig()
        {
            var rig=rigs[rigIndex];
            Vector3 center=rig.Wheel!=null ? rig.Wheel.Pivot : rig.Left!=null && rig.Right!=null ? (rig.Left.Contact+rig.Right.Contact)*.5f : (rig.Left ?? rig.Right)?.Contact ?? rig.Handles[0].Center;
            Vector3D eye=center+new Vector3(.02f,.55f,.48f);
            var view=MatrixD.CreateLookAt(eye,center,Vector3D.Up);
            var size=Wrappers.MyRender11.Resolution; float aspect=(float)size.X/size.Y;
            // Match the culling frustum to the FOV used by SetupCameraMatrices.
            float tangent=(float)Math.Tan(1.3f*.5f);
            var projection=VrMath.Projection(-aspect*tangent,aspect*tangent,-tangent,tangent,.01,100);
            MyRenderProxy.SetCameraViewMatrix(view,(Matrix)projection,(Matrix)projection,1.3f,1.3f,.01f,100,100,eye,smooth:false);
            camera=new MyRenderMessageSetCameraViewMatrix { ViewMatrix=view,ProjectionMatrix=(Matrix)projection,ProjectionFarMatrix=(Matrix)projection,
                FOV=1.3f,FOVForSkybox=1.3f,NearPlane=.01f,FarPlane=100,FarFarPlane=100,CameraPosition=eye,Smooth=false };
            if(rigStep==1 || rigStep==2)
            {
                bool moved=rigStep==2;
                Matrix left=moved && rig.Wheel!=null ? rig.Wheel.Visual(.7f) : moved && rig.Left!=null ? rig.Left.Visual(new Vector3(.5f,.4f,.6f)) : Matrix.Identity;
                Matrix right=moved && rig.Right!=null ? rig.Right.Visual(new Vector3(-.5f,.4f,-.6f)) : Matrix.Identity;
                CockpitRender.UpdateScene(rig,native,MatrixD.Identity,left,right,moved,moved,switchPreview:moved ? 1f:0f,coverPreview:moved ? 1f:0f,
                    colorMask:neutralPaint,nativeRest:!moved,barPreview:moved ? 1f:0f,buttonPreview:moved,throttlePreview:moved ? 1f:0f);
                foreach(string material in rig.Pieces.Select(p=>p.Material).Distinct().Where(m=>m.StartsWith("CockpitScreen_")))
                {
                    CockpitRender.ScreenTexture(material,@"Textures\Models\default_online.dds");
                    MyRenderProxy.UpdateModelProperties(native,material,RenderFlags.Visible,(RenderFlags)0,null,null);
                }
            }
            MyRenderProxy.Draw3DScene();
            if(DateTime.UtcNow<next || (rigStep==1 || rigStep==2) && !CockpitRender.Ready) return;
            string prefix="rig-"+rig.Subtype+"-";
            if(pending!=null)
            {
                if(captured!=pending) return;
                pending=null; rigStep++; next=DateTime.UtcNow.AddSeconds(2);
                if(rigStep==3) CockpitRender.Reset();
                if(rigStep<4) return;
                if(!PhysicalImageComparison.Rig(output,rig.Subtype,message=>Logger.Info(message))) rigFailures.Add(rig.Subtype);
                if(++rigIndex<rigs.Length) { rigStep=0; BeginRig(); return; }
                if(rigFailures.Count>0) throw new InvalidOperationException("Cockpit rig render/restoration mismatch: "+string.Join(", ",rigFailures));
                Stop(); Logger.Info("PHYSICAL RENDER SMOKE PASSED: installed cockpit rigs and material restoration. Images: "+output);
                return;
            }
            pending=Path.Combine(output,prefix+new[] {"native","rest","moved","restored"}[rigStep]+".png");
            next=DateTime.UtcNow.AddSeconds(1);
        }
        public static void Render()
        {
            if(Active && LeadIndicatorTests.Enabled)
            {
                try { LeadIndicatorTests.Render(); }
                catch(Exception ex) { renderError=ex.ToString(); }
                return;
            }
            if(Active && !rotationPreviewsSaved && BuildOrientationTests.Previews.Count==3) RenderRotationPreviews();
            if(Active && phase>=45 && phase<=46) NativeGloves.Preview(
                CockpitHandPose.GripWrist(Matrix.CreateTranslation(-.11f,0,0)),
                CockpitHandPose.GripWrist(Matrix.CreateTranslation(.11f,0,0)),phase==46);
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
                if(phase==24) ResolutionTests.RunNative(output,camera);
                var target=Wrappers.MyManagers.RwTexturesPool.BorrowRtv("SEVR physical probe",size.X,size.Y,SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb);
                try
                {
                    object ao=null;
                    try
                    {
                        if(phase>=45 && phase<=46 && !NativeGloves.Preview(
                            CockpitHandPose.GripWrist(Matrix.CreateTranslation(-.11f,0,0)),
                            CockpitHandPose.GripWrist(Matrix.CreateTranslation(.11f,0,0)),phase==46)) return;
                        if(phase==45) PolishRenderTests.BeginHands(size.X,size.Y);
                        Wrappers.MyRender11.DrawGameScene(target,out ao);
                    }
                    finally { if (ao!=null) new Wrappers.BorrowedRtvTexture(ao).Release(); }
                    // Exercise the installed engine's depth SRV and our spatial shader in a real scene.
                    var physicalTarget=(Texture2D)target.GetResource();
                    if(Environment.GetEnvironmentVariable("SEVR_ITEM_GRABS")=="1") UiTests.Save(physicalTarget,Path.ChangeExtension(path,null)+"-lit.png");
                    if(Environment.GetEnvironmentVariable("SEVR_AMMO_PARENT")=="1")
                    {
                        var item=WeaponProfile.All.First(p=>p.Model=="Models/Weapons/"+grabModels[grabStep/4]+".mwm");
                        var ammo=WeaponAmmo.Label(item,MatrixD.Identity,18,20); ammo.RenderParent=native;
                        PhysicalSurface.Draw(physicalTarget,new[] {ammo},camera.ViewMatrix,camera.ProjectionMatrix,PhysicalSurface.SceneDepth());
                        SignalTests.WaitIcons();
                        PhysicalSurface.Draw(physicalTarget,new[] {ammo},camera.ViewMatrix,camera.ProjectionMatrix,PhysicalSurface.SceneDepth());
                        UiTests.Save(physicalTarget,Path.ChangeExtension(path,null)+"-ammo.png");
                    }
                    if(phase==24)
                    {
                        if(RemoteHud.FixtureSprites==null || !RemoteHud.FixtureSprites.Contains("HitIndicator")) throw new InvalidOperationException("Native hit confirmation missing: "+RemoteHud.FixtureSprites);
                        if(RemoteHud.SpriteCount<3) throw new InvalidOperationException("Expected native filter and crosshair: "+RemoteHud.FixtureSprites);
                        Logger.Info("PASS native camera HUD allowlist: "+RemoteHud.SpriteCount+" camera/crosshair/hit sprites; options screen excluded");
                        var remote=new RemoteView.View { Source=123,Width=1.2f,Height=.675f,
                            Pose=MatrixD.CreateTranslation(0,0,-1.5)*MatrixD.Invert(camera.ViewMatrix) };
                        RemoteFeed.Render(remote);
                        // Restore the physical view's depth after rendering the camera feed.
                        object extra=null;
                        try { Wrappers.MyRender11.DrawGameScene(target,out extra); }
                        finally { if(extra!=null) new Wrappers.BorrowedRtvTexture(extra).Release(); }
                        RemoteFeed.Draw(physicalTarget,remote,camera.ViewMatrix,Wrappers.MyRender11.Environment_Matrices.Projection);
                        UiTests.Save(physicalTarget,Path.Combine(output,"remote-feed-native.png"));
                        RemoteFeed.Reset();
                        int retained=RemoteHud.SpriteCount;
                        for(int i=0;i<120;i++) RemoteHud.CollectFrame(int.MinValue+i,remote,new Vector2(size.X,size.Y));
                        if(RemoteHud.SpriteCount!=retained) throw new InvalidOperationException("Camera-only frames cleared the native HUD");
                        PolishRenderTests.HudRetention(physicalTarget.Device,remote,output);
                        RemoteHud.CollectFrame(int.MinValue+120,new RemoteView.View {Source=456},new Vector2(size.X,size.Y));
                        if(RemoteHud.SpriteCount!=0) throw new InvalidOperationException("Native HUD crossed camera ownership");
                        Logger.Info("PASS native HUD retained over 120 camera-only batches and cleared on source change");
                    }
                    MatrixD surfacePose=MatrixD.CreateTranslation(0,-.10,-.5)*MatrixD.Invert(camera.ViewMatrix);
                    PhysicalSurface.Draw(physicalTarget,new[] { new SurfaceView { Id="Physical probe",Title="SEAT FIT",Text="Scene depth probe",Width=.24f,Height=.18f,Pose=surfacePose,
                        Keys=new[] { new SurfaceKey("UP",.08f,.40f,.38f,.35f),new SurfaceKey("DOWN",.54f,.40f,.38f,.35f) } } },camera.ViewMatrix,camera.ProjectionMatrix,PhysicalSurface.SceneDepth());
                    // The main menu has no world lighting. Inspect actual rasterized albedo/depth occlusion.
                    var buffer=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main").GetValue(null);
                    var texture=(Texture2D)CockpitRender.Member(CockpitRender.Member(buffer,"GBuffer0"),"Resource");
                    if(phase==24 || phase==48)
                    {
                        texture.Device.ImmediateContext.CopyResource(texture,physicalTarget);
                        SignalTests.NativeOverlay(physicalTarget,camera.ViewMatrix,camera.ProjectionMatrix,output,phase==24 ? "cockpit":"character-observer");
                    }
                    if(phase==24)
                    {
                        texture.Device.ImmediateContext.CopyResource(texture,physicalTarget);
                        var window=new MenuWindow {MinimumWidth=.42f,MaximumWidth=1.2f,Aspect=.31f/.55f,BarOffset=.035f};
                        window.Place(Matrix.Identity,.55f,new Vector3(0,-.08f,-.60f));
                        var settings=FlightSettings.View("flight-native",window.Pose,.55f,.31f,
                            FlightSettings.Layout(new Multiplayer.FlightTuning(),false,false,false,"Fighter Cockpit Flight Settings"),"Fighter Cockpit Flight Settings");
                        settings.WindowPose=window.Pose;
                        SpatialUi.DrawFloating(physicalTarget,MatrixD.Identity,camera.ProjectionMatrix,new[] {settings});
                        UiTests.Save(physicalTarget,Path.Combine(output,"flight-settings-native-cockpit.png"));
                    }
                    if(phase==45 || phase==46)
                    {
                        var glove=GloveGeometry.Load(VRage.FileSystem.MyFileSystem.ContentPath,GloveGeometry.DefaultModel,true);
                        MatrixD mount=glove.WristMount*CockpitHandPose.GripWrist(Matrix.CreateTranslation(-.11f,0,0));
                        var tablet=new SurfaceView { Id="Native glove tablet",Style=SurfaceStyle.WristMenu,Width=.4f,Height=.225f,
                            Pose=SpatialUi.WristPose(mount,1,.225f,-1),Keys=WristPanel.Keys(null,false,false,false,true,null,false) };
                        texture.Device.ImmediateContext.CopyResource(texture,physicalTarget);
                        PhysicalSurface.Draw(physicalTarget,new[] {tablet},camera.ViewMatrix,camera.ProjectionMatrix,PhysicalSurface.SceneDepth());
                        UiTests.Save(physicalTarget,Path.Combine(output,"native-tablet-depth-"+phase+".png"));
                        if(phase==46)
                        {
                            texture.Device.ImmediateContext.CopyResource(texture,physicalTarget);
                            var settings=FlightSettings.View("flight-native-wrist",tablet.Pose,tablet.Width,tablet.Height,
                                FlightSettings.Layout(new Multiplayer.FlightTuning(),false,false,false,"Fighter Cockpit Flight Settings"),"Fighter Cockpit Flight Settings");
                            PhysicalSurface.Draw(physicalTarget,new[] {settings},camera.ViewMatrix,camera.ProjectionMatrix,PhysicalSurface.SceneDepth());
                            UiTests.Save(physicalTarget,Path.Combine(output,"flight-settings-native-wrist.png"));
                        }
                        if(phase==46)
                        {
                            SignalTests.InspectPanel(new[] {tablet},MatrixD.Invert(camera.ViewMatrix),DateTime.UtcNow);
                            if(!tablet.Signals.Candidates.Any(c=>c.Visible)) throw new InvalidOperationException("Native wrist fixture has no visible signal label");
                            PhysicalSurface.Draw(physicalTarget,new[] {tablet},camera.ViewMatrix,Wrappers.MyRender11.Environment_Matrices.Projection,PhysicalSurface.SceneDepth());
                            SignalTests.WaitIcons();
                            texture.Device.ImmediateContext.CopyResource(texture,physicalTarget);
                            PhysicalSurface.Draw(physicalTarget,new[] {tablet},camera.ViewMatrix,Wrappers.MyRender11.Environment_Matrices.Projection,PhysicalSurface.SceneDepth());
                            UiTests.Save(physicalTarget,Path.Combine(output,"signals-native-wrist.png"));
                            var hudConfig=new Config.PluginConfig(); hudConfig.InitializeHudProfiles(); WristHud.Open(hudConfig);
                            var hudPanel=HudProfileTests.Panel(hudConfig).At(tablet.Pose);
                            texture.Device.ImmediateContext.CopyResource(texture,physicalTarget);
                            PhysicalSurface.Draw(physicalTarget,new[] {hudPanel},camera.ViewMatrix,Wrappers.MyRender11.Environment_Matrices.Projection,PhysicalSurface.SceneDepth());
                            UiTests.Save(physicalTarget,Path.Combine(output,"hud-profiles-native.png"));
                        }
                        if(phase==45)
                        {
                            texture.Device.ImmediateContext.CopyResource(texture,physicalTarget);
                            PolishRenderTests.Hands(physicalTarget,output,camera.ViewMatrix,camera.ProjectionMatrix);
                        }
                    }
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
                                bitmap.Save(path,ImageFormat.Png);
                                if(lit<100 && phase!=47)
                                {
                                    throw new InvalidOperationException("Native probe scene is blank in phase "+phase+" ("+lit+" samples); proxy validation alone is not a render test.");
                                }
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
            if(Difference("bar-rest","bar-pulled")<10)
                throw new InvalidOperationException("Striped bar does not pull in the native renderer");
            if(Difference("front-closed","front-open")<15 || Difference("front-right-rest","front-right-on")<10)
                throw new InvalidOperationException("Remaining fighter controls did not articulate in the native renderer");
            if(Difference("uncovered-rest","uncovered-on")<2)
                throw new InvalidOperationException("Uncovered switch articulation missing from native render");
            if(Difference("camera-Ship","camera-Heading")<100 || Difference("camera-Heading","camera-Fixed")<100)
                throw new InvalidOperationException("Observer modes lost their distinct native orientations");
            if(Difference("miniature-left","miniature-right")<100 || Difference("miniature-left","miniature-enlarged-left")<100)
                throw new InvalidOperationException("Miniature scale or stereo disparity missing from native render");
            if(Difference("controls-rest","controls-levers")<2 || Difference("controls-rest","controls-covers")<2 ||
                Difference("controls-rest","controls-restored")>2)
                throw new InvalidOperationException("Control geometry highlights are missing or did not restore");
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
            int changed=PhysicalImageComparison.Difference(Path.Combine(output,first+".png"),Path.Combine(output,second+".png"));
            Logger.Info("Physical image difference "+first+" / "+second+": "+changed+" sampled pixels");
            return changed;
        }
        internal static void Stop()
        {
            options?.CloseScreenNow(); options=null;
            if(assignment!=null) { assignment.Finish(); assignment=null; }
            NativeGloves.Reset(); CockpitRender.Reset();
            if (native!=uint.MaxValue) MyRenderProxy.RemoveRenderObject(native,MyRenderProxy.ObjectType.Entity);
            native=uint.MaxValue; Active=false;
        }
        private static void BeginCameraHud()
        {
            RemoteHud.Fixture=new RemoteView.View {Source=123};
            options=new CameraHudPreview(); Sandbox.Graphics.GUI.MyGuiSandbox.AddScreen(options);
        }
    }
}
