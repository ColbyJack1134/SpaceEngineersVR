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
        public static void Initialize(string game,string data)
        {
            Directory.CreateDirectory(data);
            MyFileSystem.Init(Path.Combine(game,"..","Content"),data);
            VRage.MyTexts.LoadTexts(Path.Combine(MyFileSystem.ContentPath,"Data","Localization"),"en",null);
        }
        public static void Signals(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output);
            Initialize(game,Path.Combine(output,"data"));
            MarkerTests.Run(log);
            SignalTests.Run(log);
            using(var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport)) SignalTests.Render(device,output,log);
        }
        public static void Tablet(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output);
            Initialize(game,Path.Combine(output,"data"));
            SpatialUiTests.Run(log);
            using(var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))
            {
                foreach(float fold in new[] {0f,.5f,1f})
                    Save(MenuHands.PreviewGlove(device,true,0,new Vector3(0,-1,0),fold),Path.Combine(output,"tablet-"+fold+".png"));
                foreach(bool left in new[] {false,true})
                {
                    string side=left ? "left":"right";
                    Save(MenuHands.PreviewGlove(device,left,0,new Vector3(0,-1,0),pointer:.35f),Path.Combine(output,"pointer-"+side+".png"));
                    Save(MenuHands.PreviewGlove(device,left,0,new Vector3(0,-1,0),pointer:.35f,tipView:new Vector3(.16f,.03f,-.10f)),Path.Combine(output,"pointer-"+side+"-side.png"));
                    Save(MenuHands.PreviewGlove(device,left,0,new Vector3(0,-1,0),pointer:.35f,tipView:new Vector3(.02f,.20f,.04f)),Path.Combine(output,"pointer-"+side+"-top.png"));
                }
                var keyboard=new SurfaceView { Id="Keyboard",Style=SurfaceStyle.Keyboard,Pose=MatrixD.Identity,Width=.62f,Height=.62f*KeyboardWindow.Aspect,
                    TrackingSpace=true,Text="Two hands",Keys=MenuKeyboard.Keys,Hover=14,Pressed=-1,HoverAlt=22,PressedAlt=22 };
                Save(MenuHands.PreviewSurface(device,keyboard),Path.Combine(output,"keyboard-two-hands.png"));
            }
            string content=MyFileSystem.ContentPath;
            var rightGlove=GloveGeometry.Load(content,GloveGeometry.DefaultModel,false); var leftGlove=GloveGeometry.Load(content,GloveGeometry.DefaultModel,true);
            var mirror=Matrix.CreateScale(-1,1,1); var mirrored=mirror*rightGlove.PointFrame*mirror;
            if(Vector3.Distance(mirrored.Translation,leftGlove.PointFrame.Translation)>.002f || Vector3.Dot(mirrored.Forward,leftGlove.PointFrame.Forward)<.999f)
                throw new Exception("Left menu pointer is not the mirror of the right: "+leftGlove.PointFrame.Translation+" vs "+mirrored.Translation);
            log("PASS folded/open production tablet previews and both-hand menu pointer previews; left pointer mirrors right within 2 mm");
        }
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
            Initialize(game,Path.Combine(output,"data"));
            foreach (var action in GameActions.Quick.Concat(GameActions.Building).Concat(GameActions.Developer))
                if (!File.Exists(Path.Combine(MyFileSystem.ContentPath,action.Icon))) throw new FileNotFoundException("Action artwork",action.Icon);
            using (var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))

            {
                Save(MenuHands.PreviewGlove(device,true,0,new Vector3(0,-1,0)),Path.Combine(output,"glove-left.png"));
                Save(MenuHands.PreviewGlove(device,false,0,new Vector3(0,-1,0)),Path.Combine(output,"glove-right.png"));
                Save(MenuHands.PreviewGlove(device,false,1,new Vector3(0,.8f,0)),Path.Combine(output,"glove-closed-red.png"));
                Save(MenuHands.PreviewGlove(device,true,0,new Vector3(0,-1,0),palm:true),Path.Combine(output,"glove-fingers.png"));
                foreach(float fold in new[] {0f,1f})
                    Save(MenuHands.PreviewGlove(device,true,0,new Vector3(0,-1,0),fold),Path.Combine(output,"glove-tablet-"+fold+".png"));
                using (var hud=new OverlayCanvas("HUD test",1280,560,1,false,device))
                {
                    var model=new EssentialHud.View {
                        Levels=new[] { .72f,.46f,.88f,.19f }, Values=new[] { "72","46","88","19" },
                        Icons=new[] { @"Textures\GUI\Icons\WeaponWelder.dds" }, Selected="Enhanced Welder",Ammo="",
                        Helmet=true,Jetpack=true,Dampeners=false,Flying=true,Broadcasting=true,Flashlight=true,
                        OxygenBottles="2",HydrogenBottles="3",OxygenRefilling=true,EnvironmentOxygen="High",Temperature="Warm",
                        Speed="24.6",SpeedLevel=.246f,NaturalGravity="1.00",ArtificialGravity="0.00",Down=new Vector3(.2f,-.9f,.3f) };
                    Render(hud,()=>EssentialHud.Paint(hud,model));
                    Save(hud.Texture,Path.Combine(output,"hud-preview.png"));
                    model.FoodEnabled=true; model.RadiationEnabled=true; model.Food="64"; model.FoodLevel=.64f;
                    model.Radiation="21"; model.RadiationLevel=.21f; model.RadiationImmunity=true;
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"hud-survival-preview.png"));
                    model.FoodEnabled=false; model.RadiationEnabled=false;
                    model.Selected="Gatling Gun"; model.Icons=new[] {@"Textures\GUI\Icons\Cubes\gatling_gun.dds"}; model.Ammo="2,400";
                    model.Piloting=true; model.ShipHydrogen="78%"; model.ShipBattery="61%  12.4 MWh"; model.ShipLoad="43%"; model.ShipEndurance="2 h 12 min";
                    model.ShipMass="872,000 kg"; model.ShipPower=true; model.ShipBroadcasting=true; model.ShipPark=false; model.Dampeners=true;
                    model.ShipHydrogenLevel=.78f; model.ShipBatteryLevel=.61f; model.ShipLoadLevel=.43f;
                    Render(hud,()=>EssentialHud.Paint(hud,model));
                    Save(hud.Texture,Path.Combine(output,"ship-hud-preview.png"));
                    model.FoodEnabled=true; model.RadiationEnabled=true;
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"ship-hud-survival-preview.png"));
                    model.Selected="Very long selected ship weapon name"; model.Ammo="2,400,000";
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"ship-hud-long-equipment.png"));
                    model.Selected="Gatling Gun"; model.Ammo="2,400";
                    model.Speed="0.00"; model.SpeedLevel=.00001f;
                    Render(hud,()=>EssentialHud.Paint(hud,model));
                    Save(hud.Texture,Path.Combine(output,"ship-hud-zero-speed.png"));
                    model.Prompt="This switch action is unavailable or access is denied.";
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"ship-hud-error.png"));
                    model.Piloting=false; model.Selected="Enhanced Welder"; model.Icons=new[] {@"Textures\GUI\Icons\WeaponWelder.dds"}; model.Ammo=null;
                    model.Prompt="Select a block before adding it to the build planner";
                    Render(hud,()=>EssentialHud.Paint(hud,model)); Save(hud.Texture,Path.Combine(output,"hud-error.png"));
                    string rotation=Path.Combine(output,"..","physical-renderer","native-rotation-2.png");
                    if(File.Exists(rotation))
                    {
                        model.Selected="Light Armor Block"; model.Icons=new[] {@"Textures\GUI\Icons\Cubes\light_armor_cube.dds"}; model.Prompt=null;
                        Render(hud,()=>EssentialHud.Paint(hud,model));
                        using(var guide=new OverlayCanvas("rotation fixture",384,384,1,false,device))
                        using(var scene=new OverlayCanvas("building HUD fixture",2048,1536,1,false,device))
                        using(var source=System.Drawing.Image.FromFile(rotation))
                        using(var hudTexture=new ShaderResourceView(device,hud.Texture))
                        using(var guideTexture=new ShaderResourceView(device,guide.Texture))
                        {
                            guide.Clear(System.Drawing.Color.Transparent); guide.Graphics.DrawImage(source,0,0,384,384); guide.Upload();
                            scene.Clear(System.Drawing.Color.FromArgb(255,12,20,28)); scene.Upload();
                            var projection=VrMath.Projection(-1,1,-.75f,.75f,.03);
                            float half=BuildOrientationHud.Width/2;
                            NativeSprites.Draw(scene.Texture,new[] {
                                PhysicalSurface.Quad(hudTexture,MatrixD.CreateTranslation(0,EssentialHud.OverlayY,-EssentialHud.OverlayDepth),
                                    new VRageMath.RectangleF(-EssentialHud.OverlayWidth/2,EssentialHud.OverlayHeight/2,EssentialHud.OverlayWidth,EssentialHud.OverlayHeight),new Vector4(0,0,1,1),Vector4.One,MatrixD.Identity,projection),
                                PhysicalSurface.Quad(guideTexture,BuildOrientationHud.Mount,new VRageMath.RectangleF(-half,half,2*half,2*half),new Vector4(0,0,1,1),Vector4.One,MatrixD.Identity,projection) });
                            Save(scene.Texture,Path.Combine(output,"building-hud-composite.png"));
                        }
                    }
                }
                using (var wheel=new OverlayCanvas("Wheel test",1024,1024,1,false,device))
                {
                    var model=new ToolbarWheel.View {
                        Title="Quick actions",Hint=ToolbarWheel.ControlsHint,Group=1,Pages=1,
                        Labels=GameActions.WheelActions(false,false,false).Take(9).Select(a=>a.Label).ToArray(),Icons=GameActions.WheelActions(false,false,false).Take(9).Select(a=>new[] { a.Icon }).ToArray(),
                        Enabled=Enumerable.Repeat(true,9).ToArray(),SubIcons=new string[9],ItemText=new string[9],Selected=0 };
                    Render(wheel,()=>ToolbarWheel.Paint(wheel,model));
                    Save(wheel.Texture,Path.Combine(output,"wheel-actions-preview.png"));
                    foreach(var entry in new[] { Tuple.Create("third-person",false,true,true),Tuple.Create("cockpit-building",true,true,true),Tuple.Create("cockpit-first",false,true,false),Tuple.Create("building",true,false,false),Tuple.Create("blueprint",true,false,false),Tuple.Create("character",false,false,false),Tuple.Create("jetpack",false,false,false) })
                    {
                        var choices=entry.Item1=="blueprint" ? GameActions.ClipboardActions():GameActions.WheelActions(entry.Item2,entry.Item3,entry.Item4,entry.Item1=="jetpack");
                        var pages=BlockVariants.Pages(Array.Empty<ActionChoice>(),choices);
                        for(int page=0;page<pages.Length;page++)
                        {
                            var items=pages[page];
                            model.Title="Quick actions "+(page+1)+" / "+pages.Length; model.Group=1; model.Page=page; model.Pages=pages.Length;
                            model.Hint="Hold Y · Right stick selects · Release Y confirms\nLeft / right trigger: previous / next page";
                            model.Labels=items.Select(c=>c?.Label ?? "").ToArray(); model.Icons=items.Select(c=>c==null ? Array.Empty<string>() : new[] {c.Icon}).ToArray();
                            model.Enabled=items.Select(c=>c!=null).ToArray();
                            Render(wheel,()=>ToolbarWheel.Paint(wheel,model)); Save(wheel.Texture,Path.Combine(output,"wheel-"+entry.Item1+"-"+page+".png"));
                        }
                    }
                    var variantIcons=new[] { "light_armor_cube","light_armor_slope","light_armor_corner","light_armor_inv_corner","Slope2x1x1Base","Slope2x1x1Tip","LightArmorSquareSlab","LightArmorSlopeSlab" };
                    foreach(var icon in variantIcons)
                        if(!File.Exists(Path.Combine(MyFileSystem.ContentPath,@"Textures\GUI\Icons\Cubes\"+icon+".dds"))) throw new FileNotFoundException("Variant artwork",icon);
                    var variants=new[] { "Light Armor Block","Light Armor Slope","Light Armor Corner","Light Armor Inv. Corner","Light Armor Slope 2x1x1 Base","Light Armor Slope 2x1x1 Tip","Light Armor Half Block","Light Armor Half Slope" }
                        .Select((label,i)=>new ActionChoice(label,()=> {},icon:@"Textures\GUI\Icons\Cubes\"+variantIcons[i]+".dds")).ToArray();
                    var buildingPages=BlockVariants.Pages(variants,GameActions.WheelActions(true,false,false));
                    for(int p=0;p<buildingPages.Length;p++)
                    {
                        var choices=buildingPages[p]; model.Title="Building "+(p+1)+" / "+buildingPages.Length;
                        model.Variants=p<(variants.Length+8)/9; model.Group=1; model.Page=p; model.Pages=buildingPages.Length; model.Selected=p==0 ? 0:-1;
                        model.Hint="Hold Y · Right stick selects · Release Y confirms\nLeft / right trigger: previous / next page";
                        model.Labels=choices.Select(c=>c?.Label ?? "").ToArray(); model.Icons=choices.Select(c=>c==null ? new string[0]:new[] {c.Icon}).ToArray();
                        model.Enabled=choices.Select(c=>c!=null).ToArray();
                        Render(wheel,()=>ToolbarWheel.Paint(wheel,model)); Save(wheel.Texture,Path.Combine(output,"wheel-variants-"+p+".png"));
                    }
                    model.Variants=false; model.Hint=ToolbarWheel.ControlsHint; model.Selected=0;
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
                SignalTests.Render(device,output,log);
                MenuColors(device,output,log);
                SurfacePreviews(device,output,log);
                CockpitHandTests.Preview(output);
                GameplayFeatureTests.Run(log,output);
                BlockPreviews(device,output);
                RemoteViewTests.Preview(device,output);
                using(var feed=new OverlayCanvas("remote feed frame",1600,1100,1,false,device))
                    foreach(float width in new[] {.7f,1.2f,3.2f})
                    {
                        RemoteFeed.Paint(feed,new RemoteView.View { Width=width,Height=width*9/16,Hover=3 });
                        feed.Upload(); Save(feed.Texture,Path.Combine(output,"remote-frame-"+width.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".png"));
                    }
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
        private static void BlockPreviews(Device device,string output)
        {
            var names=new[] {"Steel Plate","Construction Comp.","Large Steel Tube","Metal Grid","Computer"};
            var icons=new[] {"steel_plate_component","construction_components_component","large_tube_component","metal_grid_component","computer_component"};
            var totals=new[] {280,40,80,40,4}; var mounted=new[] {155,10,10,25,4}; var available=new[] {82,100,4,25,8};
            foreach(string variant in new[] {"unfinished","damaged","complete","preview"})
            {
                var data=new BlockInspection.Data {Built=variant=="unfinished" ? .65f:variant=="preview" ? 0:1,
                    Integrity=variant=="damaged" ? .48f:variant=="unfinished" ? .65f:variant=="preview" ? 0:1,Critical=.6f,Ownership=.2f,Pcu=15,Preview=variant=="preview",
                    Components=names.Select((n,i)=>new Sandbox.Game.Gui.MyHudBlockInfo.ComponentInfo {ComponentName=n,TotalCount=totals[i],
                        MountedCount=variant=="complete" ? totals[i]:variant=="preview" ? 0:mounted[i],AvailableAmount=available[i],
                        Icons=new[] {@"Textures\GUI\Icons\component\"+icons[i]+".dds"}}).ToArray()};
                var view=new SurfaceView {Title="Large Hydrogen Thruster",Block=data,Icons=new[] {@"Textures\GUI\Icons\Cubes\HydrogenThrusterLarge.dds"}};
                var size=PhysicalSurface.TextureSize(SurfaceStyle.BlockInfo);
                using(var card=new OverlayCanvas("block inspection",size.X,size.Y,1,false,device))
                {
                    Render(card,()=>BlockInspection.Paint(card,view));
                    Save(card.Texture,Path.Combine(output,"block-"+variant+"-texture.png"));
                    using(var image=System.Drawing.Image.FromFile(Path.Combine(output,"block-"+variant+"-texture.png")))
                    using(var face=new System.Drawing.Bitmap(size.X,(int)(size.X*.441f)))
                    using(var g=System.Drawing.Graphics.FromImage(face))
                    {
                        g.DrawImage(image,0,0,face.Width,face.Height);
                        face.Save(Path.Combine(output,"block-"+variant+".png"));
                    }
                }
            }
        }
        private static void MenuColors(Device device,string output,Action<string> log)
        {
            using(var pixels=new OverlayCanvas("Menu colour source",8,8,1,false,device))
            {
                pixels.Clear(System.Drawing.Color.FromArgb(255,128,64,192)); pixels.Upload();
                var description=pixels.Texture.Description;
                description.Format=SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb;
                using(var source=new Texture2D(device,description))
                using(var view=new ShaderResourceView(device,source))
                {
                    device.ImmediateContext.CopyResource(pixels.Texture,source);
                    foreach(var format in new[] {SharpDX.DXGI.Format.B8G8R8A8_UNorm,SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb})
                    {
                        description.Width=description.Height=256; description.Format=format;
                        using(var target=new Texture2D(device,description))
                        {
                            FloatingMenu.DrawPanel(target,view,new FloatingMenu.Snapshot {Pose=Matrix.CreateTranslation(0,0,-1),Width=1,Height=1},
                                MatrixD.Identity,VrMath.Projection(-1,1,-1,1,.03));
                            string path=Path.Combine(output,"menu-colour-"+format+".png"); Save(target,path);
                            using(var image=new Bitmap(path))
                            {
                                var color=image.GetPixel(128,128);
                                if(Math.Abs(color.R-128)>2 || Math.Abs(color.G-64)>2 || Math.Abs(color.B-192)>2)
                                    throw new Exception("Native menu colour changed on "+format+": "+color);
                            }
                        }
                    }
                }
            }
            log("PASS native menu colour: sRGB contents preserve encoded pixels on menu and world eye targets");
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
        private static EssentialHud.View WristFixture() => new EssentialHud.View {
            Levels=new[] {.72f,.46f,.88f,.19f},Values=new[] {"72","46","88","19"},
            Icons=new[] {@"Textures\GUI\Icons\WeaponWelder.dds"},Selected="Enhanced Welder",Ammo="",
            Helmet=true,Jetpack=true,Dampeners=true,AutoDampeners=true,Flying=true,Broadcasting=true,Flashlight=true,
            OxygenBottles="2",HydrogenBottles="3",OxygenRefilling=true,EnvironmentOxygen="High",Temperature="Warm",
            Speed="24.6",SpeedLevel=.246f,NaturalGravity="1.00",ArtificialGravity="0.00",Food="64",FoodLevel=.64f,
            Radiation="21",RadiationLevel=.21f,RadiationImmunity=true,
            ShipHydrogen="78%",ShipBattery="61%  12.4 MWh",ShipLoad="43%",ShipEndurance="2 h 12 min",
            ShipMass="872,000 kg",ShipPower=true,ShipBroadcasting=true,ShipHydrogenLevel=.78f,ShipBatteryLevel=.61f,ShipLoadLevel=.43f };
        private static void WristPreview(Device device,OverlayCanvas canvas,SurfaceView wrist,string output,string name)
        {
            Render(canvas,()=>PhysicalSurface.Paint(canvas,wrist));
            using(var face=new OverlayCanvas("wrist face",1024,(int)(1024*wrist.Height/wrist.Width),1,false,device))
            using(var texture=new ShaderResourceView(device,canvas.Texture))
            {
                face.Clear(System.Drawing.Color.Black); face.Upload();
                NativeSprites.Draw(face.Texture,new[] {new NativeSprite(null,new VRageMath.RectangleF(0,0,face.Width,face.Height),Vector4.One) {Texture=texture}});
                Save(face.Texture,Path.Combine(output,name+".png"));
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
                keyboard.Keys=MenuKeyboard.MakeKeys(true);
                PhysicalSurface.Paint(canvas,keyboard); canvas.Upload(); Save(canvas.Texture,Path.Combine(output,"keyboard-symbols-preview.png"));
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
                using(var badge=new OverlayCanvas("switch action preview",PhysicalSurface.TextureSize(SurfaceStyle.Label).X,PhysicalSurface.TextureSize(SurfaceStyle.Label).Y,1,false,device))
                {
                    foreach(bool assigned in new[] {false,true})
                    {
                        var view=new SurfaceView { Style=SurfaceStyle.Label,Title=assigned ? "Power · Reactor" : "Assign · 10",Text=assigned ? null : "+",Icons=assigned ? new[] { NativeSprites.Hud("GridPowerOn") } : new string[0],Levels=assigned ? new[] {1f} : null };
                        Render(badge,()=>PhysicalSurface.Paint(badge,view));
                        Save(badge.Texture,Path.Combine(output,"switch-label-"+(assigned ? "assigned" : "empty")+".png"));
                    }
                    foreach(var entry in new[] { Tuple.Create("long-name","Run","fire"),Tuple.Create("long-argument","Run","fire all forward batteries with a long argument"),
                        Tuple.Create("analog-piston","Set and move","2.75 m"),Tuple.Create("analog-rotor","Rotate to angle","-45.0°"),Tuple.Create("analog-thrust","Set thrust override","37.5%"),Tuple.Create("analog-thrust-off","Set thrust override","Disabled"),
                        Tuple.Create("long-action","Increase velocity limit","fire"),Tuple.Create("no-argument","On/Off","") })
                    {
                        var view=new SurfaceView { Style=SurfaceStyle.Label,Title="Forward battery programmable block with a very long custom name",Action=entry.Item2,
                            Argument=entry.Item3,Icons=new[] {NativeSprites.Hud("GridPowerOn")} };
                        Render(badge,()=>PhysicalSurface.Paint(badge,view));
                        Save(badge.Texture,Path.Combine(output,"switch-label-"+entry.Item1+".png"));
                    }
                }
                var wrist=new SurfaceView { Id="Wrist preview",Style=SurfaceStyle.WristStatus,Width=.133f,Height=.07f,Levels=new[] { .8f,.7f,.6f,.5f },Status=WristFixture() };
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
                WristPreview(device,canvas,wrist,output,"wrist-status-preview");
                wrist.Status.FoodEnabled=true; wrist.Status.RadiationEnabled=true;
                WristPreview(device,canvas,wrist,output,"wrist-survival-preview");
                wrist.Status.RadiationLevel=0; wrist.Status.Radiation="0";
                WristPreview(device,canvas,wrist,output,"wrist-radiation-immunity-preview");
                wrist.Status.RadiationLevel=.21f; wrist.Status.Radiation="21";
                wrist.Status.Piloting=true;
                wrist.Status.Selected="Gatling Gun"; wrist.Status.Icons=new[] {@"Textures\GUI\Icons\Cubes\gatling_gun.dds"}; wrist.Status.Ammo="2,400";
                WristPreview(device,canvas,wrist,output,"wrist-ship-preview");
                wrist.Status.Selected="Very long selected ship weapon name"; wrist.Status.Ammo="2,400,000";
                WristPreview(device,canvas,wrist,output,"wrist-long-equipment-preview");
                wrist.Status.Selected="Ship Welder"; wrist.Status.Icons=new[] {@"Textures\GUI\Icons\Cubes\Welder.dds"}; wrist.Status.Ammo=null;
                WristPreview(device,canvas,wrist,output,"wrist-ship-tool-preview");
                wrist.Status.Prompt="This action is unavailable or access is denied.";
                WristPreview(device,canvas,wrist,output,"wrist-alert-preview");
                wrist.Status.Prompt=null;
                wrist.Status.Selected=null; wrist.Status.Icons=new string[0];
                WristPreview(device,canvas,wrist,output,"wrist-ship-empty-preview");
                wrist.Status.Piloting=false;
                wrist.Style=SurfaceStyle.WristMenu; wrist.Width=.4f; wrist.Height=.225f;
                wrist.Keys=WristPanel.Keys(null,false,false,false,true,wrist.Status,false);
                WristPreview(device,canvas,wrist,output,"wrist-menu-preview");
                wrist.Hover=5; WristPreview(device,canvas,wrist,output,"wrist-idle-hover");
                wrist.Pressed=5; WristPreview(device,canvas,wrist,output,"wrist-click-feedback");
                wrist.Hover=wrist.Pressed=-1;
                wrist.Keys=WristPanel.Keys(null,false,true,true,false,wrist.Status,false);
                WristPreview(device,canvas,wrist,output,"wrist-ship-controls");
                foreach(bool unlocked in new[] {false,true})
                {
                    WristPanel.OpenSeat();
                    var seatKeys=WristPanel.Keys(null,false,true,false,false,wrist.Status).Where(k=>k.SeatControl<0).ToList();
                    SeatPanel.WristKeys(seatKeys,true,true,unlocked);
                    wrist.Keys=seatKeys.ToArray(); wrist.SeatSettings=true; wrist.Handle=unlocked ? 1:0; wrist.Levels=new[] {1f,1f,0f,0f,1f};
                    WristPreview(device,canvas,wrist,output,"wrist-seat-"+(unlocked ? "unlocked":"locked"));
                    wrist.Pressed=Array.FindIndex(wrist.Keys,k=>k.SeatControl==0);
                    WristPreview(device,canvas,wrist,output,"wrist-seat-"+(unlocked ? "unlocked":"locked")+"-pressed");
                    wrist.Pressed=-1;
                }
                WristPanel.Reset(); wrist.SeatSettings=false; wrist.Handle=0; wrist.Levels=null;
                wrist.Keys=WristPanel.Keys(null,true,false,true,true,wrist.Status,false);
                foreach(var key in wrist.Keys.Where(k=>k.Action==GameActions.BuildShapeAction)) key.Enabled=true;
                WristPreview(device,canvas,wrist,output,"wrist-building-controls");
                wrist.Keys=WristPanel.Keys(null,false,false,false,false,wrist.Status,true);
                string[] names={"Welder","Grinder","Drill","Automatic rifle","Enhanced welder","Assign slot","Proficient welder","Elite grinder","Elite drill"};
                string[] artwork={"WeaponWelder","WeaponGrinder","WeaponDrill","WeaponAutomaticRifle","WeaponWelder_1",null,"WeaponWelder_2","WeaponGrinder_3","WeaponDrill_3"};
                for(int i=0;i<9;i++)
                {
                    var key=wrist.Keys[4+i]; key.Enabled=true; key.Label=names[i]; key.Active=i==0;
                    if(artwork[i]!=null) key.Icons=new[] {@"Textures\GUI\Icons\"+artwork[i]+".dds"};
                }
                wrist.Keys[13].Enabled=true; wrist.Keys[14].Enabled=true; wrist.Keys[15].Enabled=true; wrist.Keys[14].Label="Assign  1 / 9";
                WristPreview(device,canvas,wrist,output,"wrist-toolbar-preview");
                WristPanel.Show(2);
                wrist.Keys=WristPanel.Keys(null,false,false,false,true,wrist.Status);
                WristPreview(device,canvas,wrist,output,"wrist-search-preview");
                WristPanel.SetQuery("damp");
                wrist.Keys=WristPanel.Keys(null,false,false,false,true,wrist.Status);
                WristPreview(device,canvas,wrist,output,"wrist-search-keyboard");
                WristPanel.StopEditing();
                wrist.Keys=WristPanel.Keys(null,false,false,false,true,wrist.Status);
                WristPreview(device,canvas,wrist,output,"wrist-search-results");
                WristPanel.Reset();
                var ray=new SurfaceView { Id="Cockpit ray test",Style=SurfaceStyle.Pointer,Width=.004f,Height=.35f,
                    Pose=MatrixD.CreateWorld(new Vector3D(.1,-.1,-.5),Vector3D.Normalize(new Vector3D(-.3,.1,-.4)),Vector3D.Up) };
                var capsule=new SurfaceView { Id="Tablet capsule preview",Style=SurfaceStyle.Pointer,
                    Width=CockpitProbe.Radius*2,Height=CockpitProbe.Length+CockpitProbe.Radius*2,RoundEnds=true,
                    Pose=MatrixD.CreateWorld(new Vector3D(0,0,-.18),Vector3D.Normalize(new Vector3D(.6,-.3,-.2)),Vector3D.Up) };
                scene.Clear(System.Drawing.Color.FromArgb(255,7,12,18)); scene.Upload();
                device.ImmediateContext.ClearDepthStencilView(dsv,DepthStencilClearFlags.Depth,0,0);
                PhysicalSurface.Draw(scene.Texture,new[] {capsule},MatrixD.Identity,projection,srv);
                Save(scene.Texture,Path.Combine(output,"tablet-capsule.png"));
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
                        Labels=GameActions.WheelActions(false,false,false).Take(9).Select(a=>a.Label).ToArray(),Icons=GameActions.WheelActions(false,false,false).Take(9).Select(a=>new[] {a.Icon}).ToArray(),
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
            d.BindFlags=BindFlags.None; d.Usage=ResourceUsage.Staging; d.CpuAccessFlags=CpuAccessFlags.Read|CpuAccessFlags.Write;
            d.OptionFlags=ResourceOptionFlags.None;
            using (var staging=new Texture2D(texture.Device,d))
            {
                var context=texture.Device.ImmediateContext;
                context.CopyResource(texture,staging);
                var mapped=context.MapSubresource(staging,0,MapMode.ReadWrite,MapFlags.None);
                try
                {
                    if(d.Format==SharpDX.DXGI.Format.R8G8B8A8_UNorm || d.Format==SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb)
                    {
                        var row=new byte[d.Width*4];
                        for(int y=0;y<d.Height;y++)
                        {
                            var address=IntPtr.Add(mapped.DataPointer,y*mapped.RowPitch);
                            System.Runtime.InteropServices.Marshal.Copy(address,row,0,row.Length);
                            for(int x=0;x<row.Length;x+=4) { byte red=row[x]; row[x]=row[x+2]; row[x+2]=red; }
                            System.Runtime.InteropServices.Marshal.Copy(row,0,address,row.Length);
                        }
                    }
                    using(var bitmap=new Bitmap(d.Width,d.Height,mapped.RowPitch,PixelFormat.Format32bppArgb,mapped.DataPointer)) bitmap.Save(path,ImageFormat.Png);
                }
                finally { context.UnmapSubresource(staging,0); }
            }
        }
    }
}
