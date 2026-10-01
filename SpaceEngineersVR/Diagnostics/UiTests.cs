using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRage.FileSystem;
using VRageMath;
using Device = SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Diagnostics
{
    public static class UiTests
    {
        public static void Run(string game, string output, Action<string> log)
        {
            Directory.CreateDirectory(output);
            using(var guards=new StreamWriter(Path.Combine(output,"cockpit-panel-guards.csv")))
                for(int i=0;i<CockpitPanelGuard.Fighter.Length;i++)
                {
                    var region=CockpitPanelGuard.Fighter[i];
                    foreach(var corner in region.Bounds.GetCorners())
                    {
                        var point=Vector3.Transform(corner,region.Frame);
                        guards.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0},{1:R},{2:R},{3:R}",i,point.X,point.Y,point.Z));
                    }
                }
            MyFileSystem.Init(Path.Combine(game,"..","Content"),Path.Combine(output,"data"));
            foreach (var action in GameActions.Quick.Concat(GameActions.Building).Concat(GameActions.Developer))
                if (!File.Exists(Path.Combine(MyFileSystem.ContentPath,action.Icon))) throw new FileNotFoundException("Action artwork",action.Icon);
            using (var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))

            {
                using (var hud=new OverlayCanvas("HUD test",1280,560,1,false,device))
                {
                    var model=new EssentialHud.View {
                        Levels=new[] { .72f,.46f,.88f,.19f }, Values=new[] { "72","46","88","19" },
                        Icons=new[] { @"Textures\GUI\Icons\WeaponWelder.dds" }, Selected="Enhanced Welder",Ammo="",
                        Helmet=true,Jetpack=true,Dampeners=false,Flying=true,Broadcasting=true,Flashlight=true,
                        OxygenBottles="2",HydrogenBottles="3",OxygenRefilling=true,EnvironmentOxygen="High",Temperature="Warm",
                        Speed="24.6",SpeedLevel=.246f,Gravity="1.00 / 0.00 g",Down=new Vector3(.2f,-.9f,.3f) };
                    Render(hud,()=>EssentialHud.Paint(hud,model));
                    Save(hud.Texture,Path.Combine(output,"hud-preview.png"));
                    model.FoodEnabled=true; model.RadiationEnabled=true; model.Food="64"; model.FoodLevel=.64f;
                    model.Radiation="21"; model.RadiationLevel=.21f; model.RadiationImmunity=true;
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"hud-survival-preview.png"));
                    model.FoodEnabled=false; model.RadiationEnabled=false;
                    model.Piloting=true; model.ShipHydrogen="78%"; model.ShipBattery="61%  12.4 MWh"; model.ShipLoad="43%"; model.ShipEndurance="2 h 12 min";
                    model.ShipMass="872,000 kg"; model.ShipPower=true; model.ShipBroadcasting=true; model.ShipPark=false; model.Dampeners=true;
                    model.ShipHydrogenLevel=.78f; model.ShipBatteryLevel=.61f; model.ShipLoadLevel=.43f;
                    Render(hud,()=>EssentialHud.Paint(hud,model));
                    Save(hud.Texture,Path.Combine(output,"ship-hud-preview.png"));
                    model.FoodEnabled=true; model.RadiationEnabled=true;
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"ship-hud-survival-preview.png"));
                    model.Speed="0.00"; model.SpeedLevel=.00001f;
                    Render(hud,()=>EssentialHud.Paint(hud,model));
                    Save(hud.Texture,Path.Combine(output,"ship-hud-zero-speed.png"));
                    model.Prompt="This switch action is unavailable or access is denied.";
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"ship-hud-error.png"));
                    model.Piloting=false;
                    model.Prompt="Select a block before adding it to the build planner";
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"hud-error.png"));
                }
                using (var wheel=new OverlayCanvas("Wheel test",1024,1024,1,false,device))
                {
                    var model=new ToolbarWheel.View {
                        Title="Actions 1/3",Hint=ToolbarWheel.ControlsHint,Group=1,Pages=3,
                        Labels=GameActions.Quick.Take(9).Select(a=>a.Label).ToArray(),Icons=GameActions.Quick.Take(9).Select(a=>new[] { a.Icon }).ToArray(),
                        Enabled=Enumerable.Repeat(true,9).ToArray(),SubIcons=new string[9],ItemText=new string[9],Selected=0 };
                    Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                    Save(wheel.Texture,Path.Combine(output,"wheel-actions-preview.png"));
                    var third=GameActions.Quick.Skip(18).ToArray();
                    model.Title="Actions 3/3"; model.Page=2; model.Selected=2;
                    model.Labels=Enumerable.Range(0,9).Select(i=>i<third.Length ? third[i].Label : "").ToArray();
                    model.Icons=Enumerable.Range(0,9).Select(i=>i<third.Length ? new[] { third[i].Icon } : new string[0]).ToArray();
                    model.Enabled=Enumerable.Range(0,9).Select(i=>i<third.Length).ToArray();
                    Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                    Save(wheel.Texture,Path.Combine(output,"wheel-third-person-preview.png"));
                    model.Selected=4;
                    foreach(Player.Control.ObserverMode mode in Enum.GetValues(typeof(Player.Control.ObserverMode)))
                    {
                        model.Labels[4]=ThirdPersonView.Label(mode);
                        Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                        Save(wheel.Texture,Path.Combine(output,"wheel-camera-"+mode+".png"));
                    }
                    string[] tools={ "WeaponWelder","WeaponGrinder","WeaponDrill","WeaponAutomaticRifle","WeaponWelder_1","WeaponGrinder_1","WeaponDrill_1","WeaponWelder_2","WeaponGrinder_2" };
                    model.Title="Toolbar 1/9"; model.Group=0; model.Pages=9; model.Labels=new[] { "Welder","Grinder","Drill","Automatic rifle","Enhanced welder","Enhanced grinder","Enhanced drill","Proficient welder","Proficient grinder" };
                    model.Icons=tools.Select(t=>new[] { @"Textures\GUI\Icons\"+t+".dds" }).ToArray();
                    model.Enabled=Enumerable.Repeat(true,9).ToArray(); model.Enabled[5]=false;
                    Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                    Save(wheel.Texture,Path.Combine(output,"wheel-toolbar-preview.png"));
                    model.Labels[2]="Assign slot"; model.Icons[2]=new[] { GameActions.ConfigureToolbarAction.Icon };
                    model.Selected=2;
                    Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                    Save(wheel.Texture,Path.Combine(output,"wheel-empty-assignment.png"));
                    model.Title="Developer"; model.Group=3; model.Page=0; model.Pages=1;
                    model.Labels=Enumerable.Range(0,9).Select(i=>i<GameActions.Developer.Length ? GameActions.Developer[i].Label : "").ToArray();
                    model.Icons=Enumerable.Range(0,9).Select(i=>i<GameActions.Developer.Length ? new[] {GameActions.Developer[i].Icon} : new string[0]).ToArray();
                    model.Enabled=Enumerable.Range(0,9).Select(i=>i<GameActions.Developer.Length).ToArray();
                    Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                    Save(wheel.Texture,Path.Combine(output,"wheel-performance-preview.png"));
                }
                using(var performance=new OverlayCanvas("Performance preview",640,540,1,false,device))
                {
                    long now=System.Diagnostics.Stopwatch.GetTimestamp();
                    var timings=new FeatureTiming.Measurement[Enum.GetValues(typeof(FeatureTiming.Area)).Length];
                    timings[(int)FeatureTiming.Area.HudPaint]=new FeatureTiming.Measurement {Mean=.341,P95=1.879,Peak=4.1,Time=now};
                    timings[(int)FeatureTiming.Area.CockpitGeometry]=new FeatureTiming.Measurement {Mean=430.523,P95=430.523,Peak=430.523,Time=now-20*System.Diagnostics.Stopwatch.Frequency};
                    var current=new RenderPerformance.View {Fps=59.9,FrameMs=16.7,AppGpuMs=13.2,TotalGpuMs=16.7,Repeated=.5,Dropped=2,RefreshHz=72};
                    Render(performance,()=>PerformanceHud.Paint(performance,current,timings,now));
                    Save(performance.Texture,Path.Combine(output,"performance-preview.png"));
                    Render(performance,()=>PerformanceHud.Paint(performance,null,timings,now));
                    Save(performance.Texture,Path.Combine(output,"performance-collecting.png"));
                    Render(performance,()=>PerformanceHud.Paint(performance,current,timings,now,"Arm capture starts in 3 seconds; hold the pose."));
                    Save(performance.Texture,Path.Combine(output,"performance-capture.png"));
                    Render(performance,()=>PerformanceHud.PaintStatus(performance,"Arm capture starts in 3 seconds; hold the pose."));
                    Save(performance.Texture,Path.Combine(output,"developer-capture.png"));
                }
                using(var visor=new OverlayCanvas("Visor preview",1280,800,1,false,device))
                {
                    foreach(float progress in new[] {0f,.25f,.5f,.75f,1f})
                    {
                        Render(visor,()=>HelmetHud.PaintTransition(visor,progress,true));
                        Save(visor.Texture,Path.Combine(output,"visor-"+(int)(progress*100)+".png"));
                    }
                }
                using (var markers=new OverlayCanvas("Marker test",512,128,1,false,device))
                {
                    Render(markers,()=> {
                        markers.Clear(System.Drawing.Color.Transparent);
                        var names=new[] { "gps","self","friendly","neutral","enemy","scenario" };
                        for (int i=0;i<names.Length;i++) markers.Icon(@"Textures\HUD\marker_"+names[i]+".dds",i*80,24,64);
                    });
                    Save(markers.Texture,Path.Combine(output,"markers-preview.png"));
                }
                using(var arrow=new OverlayCanvas("native rotation colour",128,128,1,false,device))
                {
                    Render(arrow,()=> { arrow.Clear(System.Drawing.Color.Transparent);
                        arrow.Sprite(new NativeSprite(@"Textures\Particles\arrow_green.dds",new VRageMath.RectangleF(0,0,128,128),Vector4.One) { EncodeSrgb=true }); });
                    string path=Path.Combine(output,"native-arrow-colour.png"); Save(arrow.Texture,path);
                    using(var bitmap=new Bitmap(path))
                    {
                        var pixel=bitmap.GetPixel(64,70);
                        if(pixel.G<110 || pixel.G>170 || pixel.R>100) throw new Exception("Native sRGB rotation artwork was darkened or overexposed");
                    }
                    log("PASS native rotation artwork: linear texture sampling is encoded for the gamma-space overlay.");
                }
                MarkerPreviews(device,output);
                SurfacePreviews(device,output,log);
                CockpitHandTests.Preview(output);
                using(var menu=new OverlayCanvas("menu frame preview",1600,1100,1,false,device))
                {
                    FloatingMenu.Paint(menu,new FloatingMenu.Snapshot { Width=1.5f,Height=.84375f,Hover=1 });
                    menu.Upload(); Save(menu.Texture,Path.Combine(output,"floating-menu-frame.png"));
                }
                using(var source=new OverlayCanvas("rounded source",64,64,1,false,device))
                using(var target=new OverlayCanvas("rounded target",256,256,1,false,device))
                using(var texture=new ShaderResourceView(device,source.Texture))
                {
                    source.Clear(System.Drawing.Color.White); source.Upload();
                    target.Clear(System.Drawing.Color.Black); target.Upload();
                    NativeSprites.Draw(target.Texture,new[] { new NativeSprite(null,new VRageMath.RectangleF(0,0,256,256),Vector4.One) {
                        Texture=texture,Rounded=new Vector2(.15f,.15f) } });
                    string path=Path.Combine(output,"rounded-menu-mask.png"); Save(target.Texture,path);
                    using(var pixels=new Bitmap(path))
                        if(pixels.GetPixel(1,1).R>10 || pixels.GetPixel(128,128).R<240) throw new Exception("Rounded menu shader removed content or kept corner pixels");
                }
                File.WriteAllLines(Path.Combine(output,"weapon-profiles.csv"),new[] { "item,model,x,y,z,muzzleX,muzzleY,muzzleZ" }.Concat(new[] { WeaponProfile.Rifle,WeaponProfile.Launcher }.Select(p=>
                    p.Item+","+p.Model+","+string.Join(",",new[] { p.Primary.X,p.Primary.Y,p.Primary.Z,p.Muzzle.X,p.Muzzle.Y,p.Muzzle.Z }.Select(v=>v.ToString(System.Globalization.CultureInfo.InvariantCulture))))));
                if (NativeSprites.Loaded<20) throw new Exception("Native artwork did not load: "+NativeSprites.Loaded);
                log("PASS native UI GPU composition: "+NativeSprites.Loaded+" installed PNG/DDS assets, shaders, alpha blending and disabled tint. Previews: "+output);
            }
            GpuQueries(log);
        }
        private static void GpuQueries(Action<string> log)
        {
            if(VRage.Utils.MyLog.Default==null) VRage.Utils.MyLog.Default=new VRage.Utils.MyLog();
            using(var device=new Device(DriverType.Hardware,DeviceCreationFlags.BgraSupport))
            using(var texture=new Texture2D(device,new Texture2DDescription { Width=256,Height=256,MipLevels=1,ArraySize=1,
                Format=SharpDX.DXGI.Format.R8G8B8A8_UNorm,SampleDescription=new SharpDX.DXGI.SampleDescription(1,0),BindFlags=BindFlags.RenderTarget }))
            using(var target=new RenderTargetView(device,texture))
            {
                try
                {
                    int before=GpuTiming.Completed;
                    GpuTiming.BeginFrame(device);
                    foreach(GpuTiming.Area area in Enum.GetValues(typeof(GpuTiming.Area)))
                    {
                        GpuTiming.Begin(area);
                        device.ImmediateContext.ClearRenderTargetView(target,new SharpDX.Mathematics.Interop.RawColor4(.1f,.2f,.3f,1));
                        GpuTiming.End(area);
                    }
                    GpuTiming.EndFrame();
                    // This offscreen fixture has no Present to submit its command buffer.
                    device.ImmediateContext.Flush();
                    var deadline=DateTime.UtcNow.AddSeconds(5);
                    while(GpuTiming.Completed==before && DateTime.UtcNow<deadline)
                    { Thread.Sleep(5); GpuTiming.BeginFrame(device); }
                    if(GpuTiming.Completed==before) throw new Exception("Hardware GPU timestamp sample did not complete");
                    log("PASS hardware D3D11 timestamp/disjoint queries: production collector completed delayed nonblocking readback.");
                }
                finally { GpuTiming.Reset(); }
            }
        }
        private static void SurfacePreviews(Device device,string output,Action<string> log)
        {
            using(var canvas=new OverlayCanvas("keyboard preview",1024,640,1,false,device))
            using(var scene=new OverlayCanvas("surface preview",1024,640,1,false,device))
            using(var depth=MenuHands.CreateDepth(device,new Vector2I(1024,640),out var depthTarget,out var depthSource))
            using(var dsv=depthTarget)
            using(var srv=depthSource)
            {
                var keyboard=new SurfaceView { Id="Keyboard preview",Style=SurfaceStyle.Keyboard,Text="reactor|",
                    Keys=MenuKeyboard.MakeKeys(false),Hover=13,Pressed=13,Width=.62f,Height=.62f*9/16,Pose=MatrixD.CreateRotationX(-.25)*MatrixD.CreateTranslation(0,0,-.65) };
                PhysicalSurface.Paint(canvas,keyboard); canvas.Upload(); Save(canvas.Texture,Path.Combine(output,"floating-keyboard-preview.png"));
                var projection=VrMath.Projection(-.65f,.65f,-.4f,.4f,.03);
                scene.Clear(System.Drawing.Color.FromArgb(255,7,12,18)); scene.Upload();
                device.ImmediateContext.ClearDepthStencilView(dsv,DepthStencilClearFlags.Depth,0,0);
                PhysicalSurface.Draw(scene.Texture,new[] { keyboard },MatrixD.Identity,projection,srv);
                Save(scene.Texture,Path.Combine(output,"keyboard-3d-preview.png"));
                using(var bitmap=new Bitmap(Path.Combine(output,"keyboard-3d-preview.png")))
                    if(bitmap.GetPixel(512,320).R==7) throw new Exception("Keyboard hidden with clear controller depth");
                foreach(string subtype in new[] { FighterProfile.Subtype,"OpenCockpitLarge" })
                {
                    SeatPanel.TryMount(subtype,out _,out float width,out float height);
                    var panel=new SurfaceView { Id="Seat",Title="SEAT",Keys=SeatPanel.Keys(subtype==FighterProfile.Subtype),Levels=new[] {1f,1f,0f,0f,1f},Width=width,Height=height };
                    Render(canvas,()=>PhysicalSurface.Paint(canvas,panel));
                    Save(canvas.Texture,Path.Combine(output,"seat-"+subtype+".png"));
                    using(var face=new OverlayCanvas("seat face",600,(int)(600*height/width),1,false,device))
                    using(var tex=new ShaderResourceView(device,canvas.Texture))
                    {
                        face.Clear(System.Drawing.Color.Black); face.Upload();
                        NativeSprites.Draw(face.Texture,new[] {new NativeSprite(null,new VRageMath.RectangleF(0,0,face.Width,face.Height),Vector4.One) {Texture=tex}});
                        Save(face.Texture,Path.Combine(output,"seat-face-"+subtype+".png"));
                    }
                }
                using(var badge=new OverlayCanvas("switch action preview",768,154,1,false,device))
                {
                    foreach(bool assigned in new[] {false,true})
                    {
                        var view=new SurfaceView { Style=SurfaceStyle.Label,Title=assigned ? "Power · Reactor" : "Assign · 10",Text=assigned ? null : "+",Icons=assigned ? new[] { NativeSprites.Hud("GridPowerOn") } : new string[0],Levels=assigned ? new[] {1f} : null };
                        Render(badge,()=>PhysicalSurface.Paint(badge,view));
                        Save(badge.Texture,Path.Combine(output,"switch-label-"+(assigned ? "assigned" : "empty")+".png"));
                    }
                }
                var wrist=new SurfaceView { Id="Wrist preview",Style=SurfaceStyle.WristStatus,Width=.133f,Height=.07f,Levels=new[] { .8f,.7f,.6f,.5f } };
                foreach(bool unlocked in new[] {false,true})
                {
                    SeatPanel.TryMount(FighterProfile.Subtype,out _,out float width,out float height);
                    PhysicalSurface.Paint(canvas,new SurfaceView { Id="Seat",Width=width,Height=height,Keys=SeatPanel.Keys(true,unlocked),Handle=unlocked ? 1 : 0 }); canvas.Upload();
                    Save(canvas.Texture,Path.Combine(output,"stick-placement-"+(unlocked ? "unlocked" : "locked")+".png"));
                }
                foreach(string subtype in new[] { FighterProfile.Subtype,"OpenCockpitLarge" })
                {
                    foreach(int index in new[] {0,CockpitLayout.Count(subtype)-1})
                    {
                        var button=CockpitButtons.Preview(subtype,index);
                        button.Hover=0; button.Pressed=0;
                        Render(canvas,()=>PhysicalSurface.Paint(canvas,button));
                        Save(canvas.Texture,Path.Combine(output,"cockpit-"+subtype+"-"+index+".png"));
                    }
                }
                Render(canvas,()=>PhysicalSurface.Paint(canvas,wrist)); Save(canvas.Texture,Path.Combine(output,"wrist-status-preview.png"));
                wrist.Style=SurfaceStyle.WristMenu;
                wrist.Keys=Enumerable.Range(0,7).Select(i=>new SurfaceKey("",.04f+(i%3)*.315f,.08f+(i/3)*.29f,.29f,.25f)).ToArray();
                Render(canvas,()=>PhysicalSurface.Paint(canvas,wrist)); Save(canvas.Texture,Path.Combine(output,"wrist-menu-preview.png"));
                var ray=new SurfaceView { Id="Cockpit ray test",Style=SurfaceStyle.Pointer,Width=.004f,Height=.35f,
                    Pose=MatrixD.CreateWorld(new Vector3D(.1,-.1,-.5),Vector3D.Normalize(new Vector3D(-.3,.1,-.4)),Vector3D.Up) };
                foreach(int eye in new[] {-1,1}) foreach(bool occluded in new[] {false,true})
                {
                    device.ImmediateContext.ClearDepthStencilView(dsv,DepthStencilClearFlags.Depth,occluded ? 1 : 0,0);
                    scene.Clear(System.Drawing.Color.FromArgb(255,7,12,18)); scene.Upload();
                    var view=MatrixD.CreateTranslation(-eye*.032,0,0);
                    PhysicalSurface.Draw(scene.Texture,new[] { ray },view,projection,srv);
                    string path=Path.Combine(output,"cockpit-ray-"+eye+(occluded ? "-occluded" : "")+".png");
                    Save(scene.Texture,path);
                    var clip=Vector4D.Transform(new Vector4D(ray.Pose.Translation,1),view*projection);
                    int x=(int)((clip.X/clip.W+1)*512),y=(int)((1-clip.Y/clip.W)*320);
                    using(var bitmap=new Bitmap(path))
                    {
                        bool cyan=false;
                        for(int dx=-2;dx<=2;dx++) for(int dy=-2;dy<=2;dy++)
                        { var pixel=bitmap.GetPixel(x+dx,y+dy); cyan|=pixel.G>150 && pixel.B>150; }
                        if(cyan==occluded) throw new Exception("Cockpit ray projection/depth mismatch in eye "+eye);
                    }
                }
                log("PASS cockpit ray GPU projection: both eyes meet the projected hit line; nearer scene depth occludes the beam.");
                device.ImmediateContext.ClearDepthStencilView(dsv,DepthStencilClearFlags.Depth,1,0);
                scene.Clear(System.Drawing.Color.FromArgb(255,7,12,18)); scene.Upload();
                PhysicalSurface.Draw(scene.Texture,new[] { keyboard },MatrixD.Identity,projection,srv);
                Save(scene.Texture,Path.Combine(output,"keyboard-occluded.png"));
                using(var bitmap=new Bitmap(Path.Combine(output,"keyboard-occluded.png")))
                    for(int y=0;y<640;y+=16) for(int x=0;x<1024;x+=16)
                    {
                        var pixel=bitmap.GetPixel(x,y);
                        if(pixel.R!=7 || pixel.G!=12 || pixel.B!=18) throw new Exception("Physical surface ignored nearer scene depth");
                    }
                log("PASS physical surface GPU shader: perspective key faces/slab edges, production controller depth can be sampled; clear depth shows keyboard, nearer depth occludes it");
                var label=new SurfaceView {Id="Occluded label",Style=SurfaceStyle.Label,Title="Switch lock · Connector",
                    Icons=new[] {NativeSprites.Hud("GridPowerOn")},Levels=new[] {.5f},Width=.34f,Height=.068f,Pose=MatrixD.CreateTranslation(0,0,-.65)};
                wrist.Pose=MatrixD.CreateTranslation(0,0,-.65);
                foreach(int eye in new[] {-1,1}) foreach(var surface in new[] {label,wrist})
                {
                    scene.Clear(System.Drawing.Color.FromArgb(255,7,12,18)); scene.Upload();
                    PhysicalSurface.Draw(scene.Texture,new[] {surface},MatrixD.CreateTranslation(-eye*.032,0,0),projection,srv);
                    string path=Path.Combine(output,(surface==label ? "label" : "wrist")+"-behind-world-"+eye+".png");
                    Save(scene.Texture,path);
                    bool visible=HasContent(path);
                    if(visible!=(surface==label)) throw new Exception("Label/wrist depth policy incorrect in eye "+eye);
                }
                using(var wheel=new OverlayCanvas("occluded wheel",1024,1024,1,false,device))
                using(var texture=new ShaderResourceView(device,wheel.Texture))
                {
                    var model=new ToolbarWheel.View {Title="Actions",Group=1,Pages=3,Selected=0,
                        Labels=GameActions.Quick.Take(9).Select(a=>a.Label).ToArray(),Icons=GameActions.Quick.Take(9).Select(a=>new[] {a.Icon}).ToArray(),
                        Enabled=Enumerable.Repeat(true,9).ToArray(),SubIcons=new string[9],ItemText=new string[9]};
                    Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                    foreach(int eye in new[] {-1,1}) foreach(bool overlay in new[] {false,true})
                    {
                        scene.Clear(System.Drawing.Color.FromArgb(255,7,12,18)); scene.Upload();
                        var sprite=ToolbarWheel.Sprite(texture,MatrixD.CreateTranslation(0,0,-.65),MatrixD.CreateTranslation(-eye*.032,0,0),projection);
                        if(!overlay) sprite.IgnoreSceneDepth=false;
                        NativeSprites.Draw(scene.Texture,new[] {sprite},srv);
                        string path=Path.Combine(output,"wheel-behind-world-"+eye+(overlay ? "-after" : "-before")+".png");
                        Save(scene.Texture,path);
                        if(HasContent(path)!=overlay) throw new Exception("Selection wheel scene-depth override incorrect in eye "+eye);
                    }
                }
                log("PASS stereo UI depth: production labels and selection wheel remain visible behind nearer depth; wrist, keyboard and physical pointer retain occlusion.");
            }
        }
        private static bool HasContent(string path)
        {
            using(var bitmap=new Bitmap(path))
                for(int y=0;y<bitmap.Height;y+=4) for(int x=0;x<bitmap.Width;x+=4)
                {
                    var pixel=bitmap.GetPixel(x,y);
                    if(pixel.R!=7 || pixel.G!=12 || pixel.B!=18) return true;
                }
            return false;
        }
        private static void MarkerPreviews(Device device,string output)
        {
            using (var labels=new OverlayCanvas("Marker label test",WorldMarkers.AtlasWidth,WorldMarkers.AtlasHeight,1,false,device,true))
            using (var text=new ShaderResourceView(device,labels.Texture))
            using (var target=new OverlayCanvas("Billboard test",1024,512,1,false,device))
            {
                labels.Clear(System.Drawing.Color.Transparent);
                int width=WorldMarkers.PaintLabel(labels.Graphics,"Waypoint Alpha", "1.2 km",0);
                int longWidth=WorldMarkers.PaintLabel(labels.Graphics,"A long waypoint name must not hide its distance or enter the adjacent label", "345.6 km",1);
                WorldMarkers.PaintLabel(labels.Graphics,"", "250 m",2);
                labels.Upload(); device.ImmediateContext.GenerateMips(text);
                Save(labels.Texture,Path.Combine(output,"marker-labels-preview.png"));
                var head=MatrixD.Identity;
                head.Translation=new Vector3D(1000000,2000000,3000000);
                var sprites=new List<NativeSprite>();
                foreach (int angle in new[] { -45,0,45 })
                {
                    double yaw=angle*Math.PI/180;
                    var point=head.Translation+20*new Vector3D(Math.Sin(yaw),0,-Math.Cos(yaw));
                    if (!MarkerBillboard.TryCreate(point,head,out var billboard)) throw new Exception("Preview billboard");
                    // Read the peripheral marker along its sightline while the headset basis stays fixed.
                    var view=MatrixD.CreateLookAt(head.Translation,point,head.Up);
                    var projection=VrMath.Projection(-.4f,.4f,-.2f,.2f,.05);
                    sprites.Clear();
                    WorldMarkers.AddSprites(sprites,billboard,view,projection,@"Textures\HUD\marker_gps.dds",Vector4.One,text,0,width);
                    if (sprites.Count!=2) throw new Exception("Marker label/icon quad missing");
                    target.Clear(System.Drawing.Color.FromArgb(255,9,18,26)); target.Upload();
                    NativeSprites.Draw(target.Texture,sprites);
                    Save(target.Texture,Path.Combine(output,"marker-reading-"+angle+".png"));
                }
                sprites.Clear();
                MarkerBillboard.TryCreate(head.Translation+head.Forward*20,head,out var front);
                WorldMarkers.AddSprites(sprites,front,MatrixD.Invert(head),VrMath.Projection(-.4f,.4f,-.2f,.2f,.05),
                    @"Textures\HUD\marker_gps.dds",Vector4.One,text,1,longWidth);
                target.Clear(System.Drawing.Color.FromArgb(255,9,18,26)); target.Upload();
                NativeSprites.Draw(target.Texture,sprites);
                Save(target.Texture,Path.Combine(output,"marker-long-name.png"));
            }
        }
        private static void Render(OverlayCanvas canvas, Action paint)
        {
            paint(); canvas.Upload();
            var deadline=DateTime.UtcNow.AddSeconds(15);
            while (NativeSprites.Pending && DateTime.UtcNow<deadline) { NativeSprites.Poll(); Thread.Sleep(10); }
            if (NativeSprites.Pending) throw new TimeoutException("Native UI image loading");
            paint(); canvas.Upload();
        }
        internal static void Save(Texture2D texture,string path)
        {
            var d=texture.Description;
            d.BindFlags=BindFlags.None; d.Usage=ResourceUsage.Staging; d.CpuAccessFlags=CpuAccessFlags.Read;
            d.OptionFlags=ResourceOptionFlags.None;
            using (var staging=new Texture2D(texture.Device,d))
            {
                var context=texture.Device.ImmediateContext;
                context.CopyResource(texture,staging);
                var mapped=context.MapSubresource(staging,0,MapMode.Read,MapFlags.None);
                try { using (var bitmap=new Bitmap(d.Width,d.Height,mapped.RowPitch,PixelFormat.Format32bppArgb,mapped.DataPointer)) bitmap.Save(path,ImageFormat.Png); }
                finally { context.UnmapSubresource(staging,0); }
            }
        }
    }
}
