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
        private sealed class Glyph {public string Path; public Vector4 UV; public float Width,Height,Advance,Bearing,HeightOffset;}
        private sealed class Face
        {
            public readonly Dictionary<char,Glyph> Glyphs=new Dictionary<char,Glyph>();
            public readonly Dictionary<string,float> Kerning=new Dictionary<string,float>();
            public float Height;
        }
        private static readonly Dictionary<string,Face> faces=new Dictionary<string,Face>();
        private static readonly Dictionary<string,Vector4> masks=new Dictionary<string,Vector4>();
        private static readonly Dictionary<string,string> paths=new Dictionary<string,string>();
        private static string Content => MyFileSystem.ContentPath ?? Path.Combine(Path.GetDirectoryName(typeof(Sandbox.Game.GUI.HudViewers.MyHudMarkerRender).Assembly.Location),"..","Content");
        private static void Definitions()
        {
            if(paths.Count>0) return;
            foreach(var font in XDocument.Load(Path.Combine(Content,@"Data\Fonts.sbc")).Root.Element("Fonts").Elements())
            {
                string name=(string)font.Element("Id").Element("SubtypeId");
                paths[name]=(string)font.Element("Resources").Element("Resource").Attribute("Path");
                var mask=font.Element("ColorMask");
                masks[name]=mask==null ? Vector4.One:new Vector4((float)mask.Element("X")/255,(float)mask.Element("Y")/255,(float)mask.Element("Z")/255,1);
            }
        }
        private static Face Load(string path=@"Fonts\white\FontDataPA.xml")
        {
            if(faces.TryGetValue(path,out var face)) return face;
            face=new Face();
            string folder=Path.GetDirectoryName(path)+"\\";
            using(var stream=File.OpenRead(Path.Combine(Content,path)))
            {
                var root=XDocument.Load(stream).Root;
                var ns=root.Name.Namespace;
                face.Height=(float)root.Attribute("height");
                var bitmaps=root.Element(ns+"bitmaps").Elements().ToDictionary(e=>(string)e.Attribute("id"));
                foreach(var e in root.Element(ns+"glyphs").Elements())
                {
                    var bitmap=bitmaps[(string)e.Attribute("bm")];
                    var bitmapSize=((string)bitmap.Attribute("size")).Split('x').Select(float.Parse).ToArray();
                    var size=((string)e.Attribute("size")).Split('x').Select(float.Parse).ToArray();
                    var origin=((string)e.Attribute("origin")).Split(',').Select(float.Parse).ToArray();
                    face.Glyphs[((string)e.Attribute("ch"))[0]]=new Glyph {Path=folder+(string)bitmap.Attribute("name"),
                        UV=new Vector4(origin[0]/bitmapSize[0],origin[1]/bitmapSize[1],size[0]/bitmapSize[0],size[1]/bitmapSize[1]),
                        Width=size[0],Height=size[1],Advance=(float)e.Attribute("aw"),Bearing=(float)e.Attribute("lsb"),HeightOffset=(float?)e.Attribute("ho") ?? 0};
                }
                foreach(var e in root.Element(ns+"kernpairs").Elements()) face.Kerning[(string)e.Attribute("left")+(string)e.Attribute("right")]=(float)e.Attribute("adjust");
            }
            faces[path]=face; return face;
        }
        private static Glyph Get(Face face,char c) => face.Glyphs.TryGetValue(c,out var glyph) ? glyph:face.Glyphs['?'];
        internal static float Width(string text,float height)
        {
            var face=Load(); float width=0; char previous='\0';
            foreach(char c in text ?? "") {if(face.Kerning.TryGetValue(previous.ToString()+c,out float adjust)) width+=adjust; width+=Get(face,c).Advance; previous=c;}
            return width*height/face.Height;
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
        internal static void Add(List<NativeSprite> output,string text,float x,float y,float height,float width,Vector4 color,int targetWidth,int targetHeight,bool center=false,float stretch=1,string font=null,float spacing=0,bool nativeGui=false)
        {
            Face face;
            if(font==null) face=Load();
            else
            {
                Definitions();
                if(!paths.ContainsKey(font)) font="White";
                face=Load(paths[font]); color=(color*masks[font]).ToLinearRGB();
            }
            text=text ?? "";
            if(Width(text,height)>width)
            {
                while(text.Length>0 && Width(text,height)>width) text=text.Substring(0,text.Length-1);
            }
            if(center) x+=(width-Width(text,height))*stretch/2;
            float scale=height/face.Height; char previous='\0';
            foreach(char c in text)
            {
                if(face.Kerning.TryGetValue(previous.ToString()+c,out float adjust)) x+=adjust*scale*stretch;
                var glyph=Get(face,c);
                // Native font rendering includes the bearing in its vertical glyph offset.
                float top=y+(nativeGui ? (glyph.Bearing+glyph.HeightOffset+3.8333333f)*scale:0);
                output.Add(new NativeSprite(glyph.Path,new RectangleF(x+glyph.Bearing*scale*stretch,top,glyph.Width*scale*stretch,glyph.Height*scale),color) {UV=glyph.UV,Premultiplied=font!=null});
                x+=(glyph.Advance*scale+spacing)*stretch; previous=c;
            }
        }
    }
}
