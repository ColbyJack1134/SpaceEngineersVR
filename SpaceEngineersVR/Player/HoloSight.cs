using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
using Bitmap = System.Drawing.Bitmap;
using Brush = System.Drawing.SolidBrush;
using DrawingColor = System.Drawing.Color;
using Graphics = System.Drawing.Graphics;
using PointF = System.Drawing.PointF;
using SpaceEngineersVR.Util;

namespace SpaceEngineersVR.Player
{
    // Reticles drawn at infinity so both eyes see one reticle on the target; the models paint them 16 cm from the eye.
    internal static class HoloSight
    {
        private sealed class Sight
        {
            internal readonly string Item;
            internal readonly int First,Count;
            // Eye sets the reticle's angular size; Clip is the depth of the frame past the reticle; Window (left, right, bottom, top) is widened by the frame so the frame cuts the reticle.
            internal readonly float Eye,Clip;
            internal readonly Vector4 Window;
            // Sights with open sky beyond a thin frame, or a long tube, crop to their rims; the rest leave it to the depth test.
            internal readonly Rim[] Rims;
            internal Sight(string item,int first,int count,float eye,float clip,Vector4 window,params Rim[] rims)
            { Item=item; First=first; Count=count; Eye=eye; Clip=clip; Window=window; Rims=rims; }
        }
        // Opening edges along the axes through the sight line (left, right, bottom, top) at Depth along it, measured with parallel rays.
        internal struct Rim
        {
            internal Vector4 Edges; internal float Depth;
            internal Rim(Vector4 edges,float depth=0) { Edges=edges; Depth=depth; }
        }
        internal sealed class Reticle
        {
            internal string Texture;
            internal Vector3 Center,Forward,Right,Up;
            internal RectangleF Tangents;
            internal float Clip;
            internal Vector4 Window;
            internal Rim[] Rims;
        }
        internal sealed class View { internal MatrixD Gun; internal Reticle Reticle; }

        // Triangle runs of the painted reticle in the first mesh part, measured from the installed models.
        private static readonly Sight[] sights={
            new Sight("PreciseAutomaticRifleItem",5161,58,.16f,.012f,Widen(new Vector4(-.013f,.013f,-.003f,.01f),.005f)),
            new Sight("UltimateAutomaticRifleItem",5163,58,.16f,.012f,Widen(new Vector4(-.013f,.013f,-.003f,.01f),.005f)),
            new Sight("ElitePistolItem",6044,200,.16f,.012f,Widen(new Vector4(-.011f,.011f,-.006f,.008f),.02f),new Rim(new Vector4(-.014f,.014f,-.0087f,.01f),-.002f)),
            new Sight("BasicHandHeldLauncherItem",8628,160,.25f,.015f,Widen(new Vector4(-.04f,.028f,-.032f,.03f),.06f),new Rim(new Vector4(-.0432f,.0432f,-.0327f,.0315f))),
            new Sight("AdvancedHandHeldLauncherItem",9860,128,.25f,.11f,Widen(new Vector4(-.012f,.012f,-.012f,.012f),.03f),
                new Rim(new Vector4(-.0208f,.0205f,-.0206f,.0206f),-.1104f),new Rim(new Vector4(-.0301f,.0297f,-.0298f,.0301f),.0809f)) };
        private static Vector4 Widen(Vector4 window,float frame) => new Vector4(window.X-frame,window.Y+frame,window.Z-frame,window.W+frame);
        internal const double MinEye=.05,MaxEye=1.2;
        internal static readonly Vector4 Tint=new Vector4(1,.05f,.03f,1);
        private static readonly Dictionary<string,Reticle> reticles=new Dictionary<string,Reticle>(StringComparer.OrdinalIgnoreCase);
        private static bool failed;
        internal static string Folder => Path.Combine(MyFileSystem.UserDataPath,"SEVR","Sights");

        internal static void Prepare()
        {
            reticles.Clear(); failed=false;
            if(!Main.VrActive || MyDefinitionManager.Static==null) return;
            foreach(var sight in sights)
            {
                try
                {
                    var id=new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject),sight.Item);
                    var profile=WeaponProfile.All.FirstOrDefault(p=>p.Item==sight.Item && p.Reticle && p.Sight.HasValue);
                    if(profile==null || !MyDefinitionManager.Static.TryGetPhysicalItemDefinition(id,out var definition)) continue;
                    // Mods that replace the model keep their own sight.
                    if(!string.Equals(definition.Model?.Replace('\\','/'),profile.Model,StringComparison.OrdinalIgnoreCase)) continue;
                    string copy=Path.Combine(Folder,profile.Model.Replace('/',Path.DirectorySeparatorChar));
                    var reticle=Build(sight,profile,Path.Combine(MyFileSystem.ContentPath,profile.Model.Replace('/',Path.DirectorySeparatorChar)),copy,Folder);
                    definition.Model=copy;
                    reticles[copy]=reticle;
                }
                catch(Exception ex) { Logger.Warning(ex,"Holographic sight unavailable for {0}",sight.Item); }
            }
            if(reticles.Count>0) Logger.Info("Holographic sights: {0} weapon models without painted reticles",reticles.Count);
        }

        // A copy with its sight reflected across X=axis. Right is rebuilt from the mirrored forward, so the reticle image keeps its orientation
        // and the window and rims swap their left and right edges.
        internal static void Mirror(string source,string copy,float axis)
        {
            if(!reticles.TryGetValue(source,out var r)) return;
            var forward=new Vector3(-r.Forward.X,r.Forward.Y,r.Forward.Z);
            var right=Vector3.Normalize(Vector3.Cross(forward,Vector3.Up));
            Vector4 Swap(Vector4 edges) => new Vector4(-edges.Y,-edges.X,edges.Z,edges.W);
            reticles[copy]=new Reticle { Texture=r.Texture,Center=new Vector3(2*axis-r.Center.X,r.Center.Y,r.Center.Z),Forward=forward,Right=right,Up=Vector3.Cross(right,forward),
                Tangents=new RectangleF(-(r.Tangents.X+r.Tangents.Width),r.Tangents.Y,r.Tangents.Width,r.Tangents.Height),Clip=r.Clip,Window=Swap(r.Window),
                Rims=r.Rims.Select(rim=>new Rim(Swap(rim.Edges),rim.Depth)).ToArray() };
        }
        internal static string[] Items => sights.Select(s=>s.Item).ToArray();
        internal static int Triangles(string item) => sights.Single(s=>s.Item==item).Count;
        internal static Reticle Build(string item,string source,string copy,string folder) =>
            Build(sights.Single(s=>s.Item==item),WeaponProfile.All.First(p=>p.Item==item),source,copy,folder);
        private static Reticle Build(Sight sight,WeaponProfile profile,string source,string copy,string folder)
        {
            source=NativePath.Of(source);
            var tags=Tags(source,"Vertices","MeshParts","Dummies");
            var positions=(HalfVector4[])tags["Vertices"];
            var indices=((List<MyMeshPartInfo>)tags["MeshParts"])[0].m_indices.ToArray();
            if((sight.First+sight.Count)*3>indices.Length) throw new InvalidDataException("Reticle triangles out of range");
            var center=profile.Sight.Value; var forward=profile.SightDirection;
            var right=Vector3.Normalize(Vector3.Cross(forward,Vector3.Up)); var up=Vector3.Cross(right,forward);
            var eye=center-forward*sight.Eye;
            var triangles=new Vector2[sight.Count*3];
            for(int i=0;i<triangles.Length;i++)
            {
                var vertex=new Vector3(positions[indices[sight.First*3+i]].ToVector4());
                if(Math.Abs(Vector3.Dot(vertex-center,forward))>.003f || Vector3.Distance(vertex,center)>.06f) throw new InvalidDataException("Reticle triangles moved");
                triangles[i]=Tangent(vertex,eye,forward,right,up);
            }
            var reticle=new Reticle { Center=center,Forward=forward,Right=right,Up=up,Clip=sight.Clip,Window=sight.Window,Rims=sight.Rims,
                Texture=Path.Combine(folder,sight.Item+".png") };
            reticle.Tangents=Paint(triangles,reticle.Texture);
            Write(copy,RemoveTriangles(File.ReadAllBytes(source),indices,sight.First,sight.Count));
            // Subparts such as magazines load from the parent model's folder.
            foreach(var dummy in ((Dictionary<string,MyModelDummy>)tags["Dummies"]).Where(d=>d.Key.Contains("subpart_")))
            {
                if(!(dummy.Value.CustomData?.TryGetValue("file",out var file)==true && file is string name)) continue;
                string from=NativePath.Of(Path.Combine(Path.GetDirectoryName(source),name+".mwm")),to=Path.Combine(Path.GetDirectoryName(copy),name+".mwm");
                if(File.Exists(from)) Write(to,File.ReadAllBytes(from));
            }
            return reticle;
        }

        internal static Dictionary<string,object> Tags(string file,params string[] names)
        {
            var importer=new MyModelImporter();
            using(var reader=new BinaryReader(File.OpenRead(NativePath.Of(file))))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {reader,names});
            return importer.GetTagData();
        }

        private static Vector2 Tangent(Vector3 point,Vector3 eye,Vector3 forward,Vector3 right,Vector3 up)
        {
            var d=point-eye; float depth=Vector3.Dot(d,forward);
            return new Vector2(Vector3.Dot(d,right)/depth,Vector3.Dot(d,up)/depth);
        }

        // Degenerate triangles keep every count and offset in the file unchanged.
        internal static byte[] RemoveTriangles(byte[] data,int[] indices,int first,int count)
        {
            var pattern=new byte[count*12];
            System.Buffer.BlockCopy(indices,first*12,pattern,0,pattern.Length);
            int at=Find(data,pattern,0);
            if(at<0 || Find(data,pattern,at+1)>=0) throw new InvalidDataException("Reticle indices not found once in the model file");
            var result=(byte[])data.Clone();
            var repeated=BitConverter.GetBytes(indices[first*3]);
            for(int i=0;i<count*3;i++) System.Buffer.BlockCopy(repeated,0,result,at+i*4,4);
            return result;
        }
        internal static int Find(byte[] data,byte[] pattern,int start)
        {
            for(int i=start;i<=data.Length-pattern.Length;i++)
            {
                int j=0;
                while(j<pattern.Length && data[i+j]==pattern[j]) j++;
                if(j==pattern.Length) return i;
            }
            return -1;
        }
        internal static void Write(string file,byte[] data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            if(!File.Exists(file) || !Enumerable.SequenceEqual(File.ReadAllBytes(file),data)) File.WriteAllBytes(file,data);
        }

        // White reticle mask, drawn four times larger without antialiasing and filtered down so shared triangle edges leave no seams.
        private static RectangleF Paint(Vector2[] triangles,string file)
        {
            float minX=triangles.Min(t=>t.X),maxX=triangles.Max(t=>t.X),minY=triangles.Min(t=>t.Y),maxY=triangles.Max(t=>t.Y);
            float pad=.04f*Math.Max(maxX-minX,maxY-minY);
            minX-=pad; maxX+=pad; minY-=pad; maxY+=pad;
            const int Size=512,Scale=4;
            float perUnit=Size/Math.Max(maxX-minX,maxY-minY);
            int width=Math.Max(8,(int)Math.Ceiling((maxX-minX)*perUnit)),height=Math.Max(8,(int)Math.Ceiling((maxY-minY)*perUnit));
            var clear=DrawingColor.FromArgb(0,255,255,255);
            using(var large=new Bitmap(width*Scale,height*Scale,PixelFormat.Format32bppArgb))
            using(var small=new Bitmap(width,height,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(large))
                using(var brush=new Brush(DrawingColor.White))
                {
                    g.Clear(clear); g.SmoothingMode=SmoothingMode.None;
                    for(int i=0;i<triangles.Length;i+=3)
                        g.FillPolygon(brush,Enumerable.Range(i,3).Select(k=>new PointF((triangles[k].X-minX)*perUnit*Scale,(maxY-triangles[k].Y)*perUnit*Scale)).ToArray());
                }
                using(var g=Graphics.FromImage(small))
                {
                    g.Clear(clear); g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    g.DrawImage(large,0,0,width,height);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                small.Save(file,ImageFormat.Png);
            }
            return new RectangleF(minX,maxY,width/perUnit,height/perUnit);
        }

        internal static View Capture()
        {
            if(reticles.Count==0 || !Main.VrActive) return null;
            var weapon=MySession.Static?.LocalCharacter?.CurrentWeapon as MyEntity;
            string model=weapon?.Model?.AssetName;
            return model!=null && reticles.TryGetValue(model,out var reticle) ? new View { Gun=weapon.WorldMatrix,Reticle=reticle }:null;
        }

        // Distance from the eye to the reticle plane along the sight line, or null when the eye's parallel ray misses the window.
        internal static double? Along(Vector3D eye,Vector3D center,Vector3D forward,Vector3D right,Vector3D up,Vector4 window)
        {
            var offset=eye-center;
            double along=-Vector3D.Dot(offset,forward),x=Vector3D.Dot(offset,right),y=Vector3D.Dot(offset,up);
            return along>=MinEye && along<=MaxEye && x>=window.X && x<=window.Y && y>=window.Z && y<=window.W ? along:(double?)null;
        }

        // NDC half-planes; the sprite shader discards dot((ndc,1),clip)<0. Each side keeps the rim nearest the sight line.
        internal static Vector4[] Crop(Vector2[][] rims,Vector2 inside)
        {
            var clips=new Vector4[4]; var margins=new float[4];
            foreach(var corners in rims)
                for(int i=0;i<4;i++)
                {
                    Vector2 a=corners[i],b=corners[(i+1)%4];
                    var edge=new Vector3(a.Y-b.Y,b.X-a.X,0); edge.Z=-(edge.X*a.X+edge.Y*a.Y);
                    float margin=(edge.X*inside.X+edge.Y*inside.Y+edge.Z)/Math.Max(1e-12f,new Vector2(edge.X,edge.Y).Length());
                    if(margin<0) { edge=-edge; margin=-margin; }
                    if(clips[i]==Vector4.Zero || margin<margins[i]) { clips[i]=new Vector4(edge,0); margins[i]=margin; }
                }
            return clips;
        }

        internal static void Draw(Texture2D target,View state,MatrixD view,MatrixD projection,ShaderResourceView depth)
        {
            if(state==null || failed) return;
            try
            {
                var r=state.Reticle; var gun=state.Gun;
                var eye=MatrixD.Invert(view).Translation;
                var forward=Vector3D.Normalize(Vector3D.TransformNormal(r.Forward,gun));
                var right=Vector3D.Normalize(Vector3D.TransformNormal(r.Right,gun)); var up=Vector3D.Normalize(Vector3D.TransformNormal(r.Up,gun));
                var along=Along(eye,Vector3D.Transform(r.Center,gun),forward,right,up,r.Window);
                if(!along.HasValue) return;
                // The quad sits past the window frame so the scene depth test clips it to the window.
                double distance=along.Value+r.Clip;
                var origin=eye+forward*distance;
                right*=distance; up*=distance;
                var transform=view*projection;
                Vector4 Corner(double x,double y) => (Vector4)Vector4D.Transform(new Vector4D(origin+right*x+up*y,1),transform);
                var t=r.Tangents;
                var sprite=new NativeSprite(r.Texture,default(RectangleF),Tint) { Projected=true,
                    TopLeft=Corner(t.X,t.Y),TopRight=Corner(t.X+t.Width,t.Y),BottomLeft=Corner(t.X,t.Y-t.Height),BottomRight=Corner(t.X+t.Width,t.Y-t.Height) };
                if(r.Rims.Length>0)
                {
                    right.Normalize(); up.Normalize();
                    Vector2 Screen(Vector3D world) { var p=Vector4D.Transform(new Vector4D(world,1),transform); return new Vector2((float)(p.X/p.W),(float)(p.Y/p.W)); }
                    var inside=Screen(Vector3D.Transform(r.Center,gun));
                    var clips=Crop(r.Rims.Select(rim=> {
                        var o=rim.Edges; var plane=Vector3D.Transform(r.Center+r.Forward*rim.Depth,gun);
                        return new[] {Screen(plane+right*o.X+up*o.W),Screen(plane+right*o.Y+up*o.W),Screen(plane+right*o.Y+up*o.Z),Screen(plane+right*o.X+up*o.Z)}; }).ToArray(),inside);
                    sprite.Clip0=clips[0]; sprite.Clip1=clips[1]; sprite.Clip2=clips[2]; sprite.Clip3=clips[3];
                }
                NativeSprites.Draw(target,new[] {sprite},depth);
            }
            catch(Exception ex) { failed=true; Logger.Warning(ex,"Holographic sight drawing disabled"); }
        }
    }
}
