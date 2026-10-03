using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using VRage.FileSystem;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SignalFont
    {
        private sealed class Glyph {public string Path; public Vector4 UV; public float Width,Height,Advance,Bearing;}
        private static readonly Dictionary<char,Glyph> glyphs=new Dictionary<char,Glyph>();
        private static readonly Dictionary<string,float> kerning=new Dictionary<string,float>();
        private static bool loaded;
        private static float lineHeight=37;
        private static void Load()
        {
            if(loaded) return;
            string folder=@"Fonts\white\";
            string content=MyFileSystem.ContentPath ?? Path.Combine(Path.GetDirectoryName(typeof(Sandbox.Game.GUI.HudViewers.MyHudMarkerRender).Assembly.Location),"..","Content");
            using(var stream=File.OpenRead(Path.Combine(content,folder+"FontDataPA.xml")))
            {
                var root=XDocument.Load(stream).Root;
                var ns=root.Name.Namespace;
                lineHeight=(float)root.Attribute("height");
                var bitmaps=root.Element(ns+"bitmaps").Elements().ToDictionary(e=>(string)e.Attribute("id"));
                foreach(var e in root.Element(ns+"glyphs").Elements())
                {
                    var bitmap=bitmaps[(string)e.Attribute("bm")];
                    var bitmapSize=((string)bitmap.Attribute("size")).Split('x').Select(float.Parse).ToArray();
                    var size=((string)e.Attribute("size")).Split('x').Select(float.Parse).ToArray();
                    var origin=((string)e.Attribute("origin")).Split(',').Select(float.Parse).ToArray();
                    glyphs[((string)e.Attribute("ch"))[0]]=new Glyph {Path=folder+(string)bitmap.Attribute("name"),
                        UV=new Vector4(origin[0]/bitmapSize[0],origin[1]/bitmapSize[1],size[0]/bitmapSize[0],size[1]/bitmapSize[1]),
                        Width=size[0],Height=size[1],Advance=(float)e.Attribute("aw"),Bearing=(float)e.Attribute("lsb")};
                }
                foreach(var e in root.Element(ns+"kernpairs").Elements()) kerning[(string)e.Attribute("left")+(string)e.Attribute("right")]=(float)e.Attribute("adjust");
            }
            loaded=true;
        }
        private static Glyph Get(char c) {Load(); return glyphs.TryGetValue(c,out var glyph) ? glyph:glyphs['?'];}
        internal static float Width(string text,float height)
        {
            Load(); float width=0; char previous='\0';
            foreach(char c in text ?? "") {if(kerning.TryGetValue(previous.ToString()+c,out float adjust)) width+=adjust; width+=Get(c).Advance; previous=c;}
            return width*height/lineHeight;
        }
        internal static string[] Wrap(string text,float height,float width)
        {
            var lines=new List<string>(); string line="";
            foreach(char c in text ?? "")
            {
                if(c=='\n') {lines.Add(line); line=""; continue;}
                if(char.IsControl(c)) continue;
                if(Width(line+c,height)>width+.01f && line.Length>0)
                {
                    int space=line.LastIndexOf(' ');
                    if(space>line.Length/2) {lines.Add(line.Substring(0,space)); line=line.Substring(space+1);}
                    else {lines.Add(line); line="";}
                }
                line+=c;
            }
            lines.Add(line); return lines.ToArray();
        }
        internal static void Add(List<NativeSprite> output,string text,float x,float y,float height,float width,Vector4 color,int targetWidth,int targetHeight,bool center=false,float stretch=1)
        {
            Load(); text=text ?? "";
            if(Width(text,height)>width)
            {
                while(text.Length>0 && Width(text,height)>width) text=text.Substring(0,text.Length-1);
            }
            if(center) x+=(width-Width(text,height))*stretch/2;
            float scale=height/lineHeight; char previous='\0';
            foreach(char c in text)
            {
                if(kerning.TryGetValue(previous.ToString()+c,out float adjust)) x+=adjust*scale*stretch;
                var glyph=Get(c);
                output.Add(new NativeSprite(glyph.Path,new RectangleF(x+glyph.Bearing*scale*stretch,y,glyph.Width*scale*stretch,glyph.Height*scale),color) {UV=glyph.UV});
                x+=glyph.Advance*scale*stretch; previous=c;
            }
        }
    }
}
