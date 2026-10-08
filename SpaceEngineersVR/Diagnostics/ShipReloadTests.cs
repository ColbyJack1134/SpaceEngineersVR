using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using Sandbox.Game.GUI;
using VRage.FileSystem;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ShipReloadTests
    {
        internal static ShipReload.View Fixture(float progress,int count)
        {
            var root=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,@"Data\Hud\Default.sbc")).Descendants("Crosshair").Elements("StatStyles").Elements("StatStyle");
            var style=root.First(e=>(string)e.Element("StatId")=="controlled_reloading");
            var label=root.First(e=>(string)e.Element("StatId")=="controlled_reloading_count");
            Func<XElement,string,Vector2> vector=(e,name)=>new Vector2((float)e.Element(name).Element("X"),(float)e.Element(name).Element("Y"));
            Func<string,Vector4> color=name=>new Vector4((float)style.Element(name).Element("X"),(float)style.Element(name).Element("Y"),(float)style.Element(name).Element("Z"),(float)style.Element(name).Element("W"));
            var texture=XDocument.Load(Path.Combine(MyFileSystem.ContentPath,@"Data\GuiTextures.sbc")).Descendants("Texture").First(e=>(string)e.Element("SubtypeName")== (string)style.Element("SegmentTexture"));
            var ring=(MyStatControlCircularProgressBar)FormatterServices.GetUninitializedObject(typeof(MyStatControlCircularProgressBar));
            object sized=new MyGuiSizedTexture {Texture=(string)texture.Element("Path")};
            AccessTools.Field(typeof(MyGuiSizedTexture),"m_sizePx").SetValue(sized,new Vector2(32));
            AccessTools.Field(typeof(MyStatControlCircularProgressBar),"m_texture").SetValue(ring,sized);
            ring.Position=vector(style,"OffsetPx"); ring.Size=vector(style,"SizePx");ring.SegmentSize=vector(style,"SegmentSizePx");ring.SegmentOrigin=vector(style,"SegmentOrigin");
            ring.NumberOfSegments=(int)style.Element("NumberOfSegments");ring.TextureRotationAngle=(float)style.Element("SpacingAngle");ring.TextureRotationOffset=(float)style.Element("AngleOffset");
            ring.ShowEmptySegments=(bool)style.Element("ShowEmptySegments");ring.FullSegmentColorMask=color("FullSegmentColorMask");ring.EmptySegmentColorMask=color("EmptySegmentColorMask");ring.StatMaxValue=1;
            var text=(MyStatControlText)FormatterServices.GetUninitializedObject(typeof(MyStatControlText));
            text.Position=vector(label,"OffsetPx");text.Size=vector(label,"SizePx");text.Scale=(float)label.Element("Scale");text.TextColorMask=Vector4.One;
            AccessTools.Field(typeof(MyStatControlText),"m_fontHash").SetValue(text,MyStringHash.GetOrCompute((string)label.Element("Font")));
            var font=new VRageRender.MyFont("",dummyFont:true);
            AccessTools.Method(typeof(VRageRender.MyFont),"LoadFontXML",new[] {typeof(string)}).Invoke(font,new object[] {Path.Combine(MyFileSystem.ContentPath,@"Fonts\white\FontDataPA.xml")});
            AccessTools.Field(typeof(MyStatControlText),"m_font").SetValue(text,font);
            return ShipReload.Read(ring,text,Vector2.Zero,progress,count);
        }
        internal static void Run(Action<string> log)
        {
            if(Fixture(0,2)!=null || Fixture(1,2)!=null || Fixture(.5f,0)!=null) throw new Exception("Inactive reload retained cockpit feedback");
            var captured=Fixture(.5f,2);
            if(captured.Text!="2x" || captured.Ring.Progress!=.5f || captured.RingOffset!=Vector2.Zero || captured.TextOffset.Y<20)
                throw new Exception("Native reload progress/count/layout was not captured");
            var later=Fixture(.8f,1);
            if(captured.Text!="2x" || captured.Ring.Progress!=.5f || ReferenceEquals(captured.Ring,later.Ring)) throw new Exception("Reload snapshot borrowed later HUD state");
            log("PASS cockpit reload: native ring/count layout, ready/empty suppression and independent render snapshots.");
        }
    }
}
