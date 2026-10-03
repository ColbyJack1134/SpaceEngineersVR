using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.Serialization;
using Sandbox.Game.GUI;
using VRage.FileSystem;
using VRage.Game.ObjectBuilders.Definitions;
using SpaceEngineersVR.Player;
using System.Text;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI.HudViewers;
using Sandbox.Game.World;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;
using VRage.Game;
using VRage.Game.Utils;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class NativeSignalProbe
    {
        private sealed class Screen : MyGuiScreenHudBase
        {
            internal string Scenario;
            private MyHudMarkerRender markers;
            public override bool Draw()
            {
                var property=AccessTools.Property(typeof(MySector),nameof(MySector.MainCamera));
                var previous=MySector.MainCamera;
                var mode=MyHudMarkerRender.SignalDisplayMode;
                var reveal=AccessTools.Field(typeof(MyHudMarkerRender),"m_disableFading");
                bool wasRevealed=(bool)reveal.GetValue(null);
                try
                {
                    MyGuiManager.DrawSpriteBatch("Textures\\GUI\\Blank.dds",new Vector2(.5f),new Vector2(2),Scenario.EndsWith("bright") ? new Color(225,225,215):new Color(7,12,20),VRage.Utils.MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
                    if(Scenario=="settings") return true;
                    var camera=new MyCamera(1f,new MyViewport(0,0,1920,1080));
                    camera.SetViewMatrix(MatrixD.Identity,false); property.SetValue(null,camera,null);
                    if(markers==null) markers=new MyHudMarkerRender(this);
                    if(Scenario.StartsWith("lead")) { LeadIndicatorTests.DrawNative(markers,Scenario); DrawTexts(); return true; }
                    AccessTools.Property(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.SignalDisplayMode)).SetValue(null,
                        Scenario.Contains("icons") ? MyHudMarkerRender.SignalMode.NoNames:Scenario.StartsWith("group") ? MyHudMarkerRender.SignalMode.DefaultMode:MyHudMarkerRender.SignalMode.FullDisplay);
                    reveal.SetValue(null,Scenario.Contains("reveal"));
                    if(Scenario.Contains("full")) AccessTools.Property(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.SignalDisplayMode)).SetValue(null,MyHudMarkerRender.SignalMode.FullDisplay);
                    if(Scenario.StartsWith("long")) markers.AddPOI(new Vector3D(0,0,-1400),new StringBuilder("Mining Outpost 02 - Ice and Iron Storage - Landing Pad GPS"),MyRelationsBetweenPlayerAndBlock.Owner);
                    markers.AddPOI(new Vector3D(-170,70,-1000),new StringBuilder("Base"),MyRelationsBetweenPlayerAndBlock.Owner);
                    markers.AddPOI(new Vector3D(210,100,-2000),new StringBuilder("Ice"),MyRelationsBetweenPlayerAndBlock.Owner);
                    markers.AddOre(new Vector3D(-28,-15,-160),"Iron");
                    markers.AddProxyEntity(new Vector3D(150,-60,-1200),MyRelationsBetweenPlayerAndBlock.Enemies,new StringBuilder("Small Grid"));
                    if(Scenario.StartsWith("group"))
                        for(int i=0;i<4;i++) markers.AddProxyEntity(new Vector3D((i-1.5)*12+(Scenario.Contains("peripheral") ? 180:0),0,-850-i*5),MyRelationsBetweenPlayerAndBlock.FactionShare,new StringBuilder("Small Grid "+(i+1)));
                    if(Scenario.StartsWith("edges")) markers.AddPOI(new Vector3D(1500,100,-500),new StringBuilder("GPS"),MyRelationsBetweenPlayerAndBlock.Owner);
                    markers.Draw(); DrawTexts();
                    if(Scenario.StartsWith("rings"))
                    {
                        var previousSession=MySession.Static;
                        try
                        {
                            MySession.Static=(MySession)FormatterServices.GetUninitializedObject(typeof(MySession));
                            var circle=Circle();
                            for(int i=0;i<3;i++)
                            {
                                circle.Position=new Vector2(650+i*310,300);
                                circle.SetTargetType(i==0 ? MyRelationsBetweenPlayerAndBlock.Neutral:i==1 ? MyRelationsBetweenPlayerAndBlock.Enemies:MyRelationsBetweenPlayerAndBlock.FactionShare);
                                circle.StatCurrent=i==2 ? .65f:0;
                                circle.Draw(1);
                            }
                        }
                        finally {MySession.Static=previousSession;}
                    }
                    return true;
                }
                catch(Exception ex) { Error=ex; return false; }
                finally { reveal.SetValue(null,wasRevealed); property.SetValue(null,previous,null); AccessTools.Property(typeof(MyHudMarkerRender),nameof(MyHudMarkerRender.SignalDisplayMode)).SetValue(null,mode); }
            }
        }
        internal static SignalRing Ring(string relation,float progress)
        {
            var style=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,@"Data\Hud\Default.sbc")).Descendants("StatStyle").First(e=>(string)e.Element("StatId")=="targeting_circle");
            var textures=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,@"Data\GuiTextures.sbc"));
            Func<string,string> texture=name=>(string)textures.Descendants("Texture").First(e=>(string)e.Element("SubtypeName")==name).Element("Path");
            Func<string,Vector2> vector=name=>new Vector2((float)style.Element(name).Element("X"),(float)style.Element(name).Element("Y"));
            Func<string,Vector4> color=name=>new Vector4((float)style.Element(name).Element("X"),(float)style.Element(name).Element("Y"),(float)style.Element(name).Element("Z"),(float)style.Element(name).Element("W"));
            return new SignalRing {Texture=texture((string)style.Element("SegmentTexture")),FilledTexture=texture((string)style.Element("FilledTexture")),
                Size=vector("SizePx"),SegmentSize=vector("SegmentSizePx"),Origin=vector("SegmentOrigin"),Segments=(int)style.Element("NumberOfSegments"),
                Angle=(float)style.Element("SpacingAngle"),Offset=(float)style.Element("AngleOffset"),ShowEmpty=(bool)style.Element("ShowEmptySegments"),Progress=progress,
                FocusColor=color(relation+"FocusSegmentColorMask"),LockColor=color(relation+"LockingSegmentColorMask")};
        }
        internal static MyStatControlTargetingProgressBar Circle()
        {
            var document=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,@"Data\Hud\Default.sbc"));
            var style=document.Descendants("StatStyle").First(e=>(string)e.Element("StatId")=="targeting_circle");
            var textures=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,@"Data\GuiTextures.sbc"));
            Func<string,MyObjectBuilder_GuiTexture> texture=name=>
            {
                var entry=textures.Descendants("Texture").First(e=>(string)e.Element("SubtypeName")==name);
                return new MyObjectBuilder_GuiTexture {Path=(string)entry.Element("Path"),SizePx=new Vector2I(32)};
            };
            var circle=new MyStatControlTargetingProgressBar(null,texture((string)style.Element("SegmentTexture")),filledTexture:texture((string)style.Element("FilledTexture")));
            Func<string,Vector2> vector=name=>new Vector2((float)style.Element(name).Element("X"),(float)style.Element(name).Element("Y"));
            circle.Size=vector("SizePx"); circle.SegmentSize=vector("SegmentSizePx"); circle.SegmentOrigin=vector("SegmentOrigin");
            circle.NumberOfSegments=(int)style.Element("NumberOfSegments"); circle.TextureRotationAngle=(float)style.Element("SpacingAngle"); circle.TextureRotationOffset=(float)style.Element("AngleOffset");
            circle.ShowEmptySegments=(bool)style.Element("ShowEmptySegments"); circle.StatMaxValue=1;
            foreach(string color in new[] {"EnemyFocusSegmentColorMask","EnemyLockingSegmentColorMask","FriendlyFocusSegmentColorMask","FriendlyLockingSegmentColorMask","NeutralFocusSegmentColorMask","NeutralLockingSegmentColorMask"})
            {
                var node=style.Element(color);
                AccessTools.Property(circle.GetType(),color).SetValue(circle,new Vector4((float)node.Element("X"),(float)node.Element("Y"),(float)node.Element("Z"),(float)node.Element("W")));
            }
            return circle;
        }
        private static Screen screen;
        private static MyGuiScreenBase settings;
        private static DateTime next;
        private static int step;
        private static Exception Error;
        private static readonly string[] scenarios=Environment.GetEnvironmentVariable("SEVR_NATIVE_HUD_REVIEW")=="1" ? new[] {"group-icons-dark","group-reveal-dark","group-icons-peripheral-dark","group-peripheral-reveal-dark","settings"}:LeadIndicatorTests.Enabled ? new[] {"lead-horizontal-dark","lead-diagonal-dark","lead-left-dark","lead-range-dark","lead-horizontal-bright","lead-overlap-dark"}:new[] {"sparse-dark","sparse-bright","icons-dark","group-dark","group-reveal-dark","group-full-dark","long-dark","edges-dark","rings-dark","rings-bright"};
        internal static void Update()
        {
            if(Error!=null) throw new InvalidOperationException("Native signal reference failed",Error);
            if(DateTime.UtcNow<next) return;
            string output=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SEVRPrototype","Reports","native-signals");
            if(LeadIndicatorTests.Enabled) output=LeadIndicatorTests.Output;
            Directory.CreateDirectory(output);
            if(screen==null)
            {
                screen=new Screen {Scenario=scenarios[0]}; MyGuiSandbox.AddScreen(screen);
                next=DateTime.UtcNow.AddSeconds(5); return;
            }
            if(step%2==0)
            {
                MyRenderProxy.TakeScreenshot(Vector2.One,Path.Combine(output,"vanilla-"+scenarios[step/2]+".png"),false,false,false);
                next=DateTime.UtcNow.AddSeconds(2); step++; return;
            }
            step++;
            if(step/2==scenarios.Length)
            {
                if(LeadIndicatorTests.Enabled) LeadIndicatorTests.Verify(scenarios.Length);
                settings?.CloseScreenNow(); screen.CloseScreenNow(); PhysicalRendererProbe.Stop();
                Logger.Info("PHYSICAL RENDER SMOKE PASSED: installed vanilla marker and text renderer, isolated main-menu reference."); return;
            }
            screen.Scenario=scenarios[step/2];
            if(screen.Scenario=="settings") { settings=new GUI.SettingsPage("Signals"); MyGuiSandbox.AddScreen(settings); }
            next=DateTime.UtcNow.AddSeconds(3);
        }
    }
}
