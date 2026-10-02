using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using HarmonyLib;
using SharpDX.Direct3D11;
using VRageMath;
using Color=System.Drawing.Color;

namespace SpaceEngineersVR.Player
{
    internal enum SurfaceStyle { Default,WristStatus,WristMenu,Keyboard,ModelControl,Pointer,Label,BlockInfo }
    internal sealed class SurfaceKey
    {
        public string Label;
        public bool Enabled=true,Active;
        public string[] Icons=new string[0];
        public string SubIcon,Text;
        public ActionChoice Action;
        public VRageMath.RectangleF Bounds;
        public SurfaceKey(string label,float x,float y,float w,float h) { Label=label; Bounds=new VRageMath.RectangleF(x,y,w,h); }
    }
    internal sealed class SurfaceView
    {
        public string Id,Title,Text;
        public EssentialHud.View Status;
        public BlockInspection.Data Block;
        public string[] Icons=new string[0];
        public string SubIcon;
        public bool Enabled=true;
        public MatrixD Pose;
        public MatrixD? HandLocal;
        internal SurfaceView At(MatrixD pose) { var copy=(SurfaceView)MemberwiseClone(); copy.Pose=pose; return copy; }
        public float Width,Height;
        public SurfaceKey[] Keys=new SurfaceKey[0];
        public int Hover=-1,Pressed=-1;
        public bool TrackingSpace,GeometryFeedback,RoundEnds;
        public SurfaceStyle Style;
        public float[] Levels;
        public int Handle;
        public Vector3? TouchPoint;
        public string ContentKey => Style+"|"+Handle+"|"+Title+"|"+Text+"|"+Hover+"|"+Pressed+"|"+string.Join("|",Keys.Select(k=>k.Label+":"+k.Enabled+":"+k.Active+":"+k.Text+":"+k.SubIcon+":"+string.Join(",",k.Icons)))+
            "|"+string.Join("|",Icons)+"|"+SubIcon+"|"+Enabled+"|"+GeometryFeedback+
            (Levels==null ? "" : string.Join(",",Levels.Select(v=>v.ToString("0.00"))))+(Id=="Seat" ? "|"+Width+"|"+Height : "");
        public int KeyAt(Vector2 uv)
        {
            for (int i=0;i<Keys.Length;i++) if (Keys[i].Enabled && Keys[i].Bounds.Width>0 && Keys[i].Bounds.Height>0 && Keys[i].Bounds.Contains(uv)) return i;
            return -1;
        }
    }
    // A poke has to approach from the front, and retract before another key fires.
    internal sealed class SurfaceTouch
    {
        private bool armed;
        private string owner;
        public int Held { get; private set; }=-1;
        public void Reset() { armed=false; owner=null; Held=-1; }
        public int Update(string id,Vector3 point,int key,bool continuous=false)
        {
            if (id!=owner) { Reset(); owner=id; }
            if (!point.IsValid()) { Reset(); return -1; }
            if (point.Z>0.027f) { armed=true; Held=-1; }
            if (point.Z< (continuous && Held>=0 ? -.12f : -.018f)) { armed=false; Held=-1; }
            if (key!=Held) Held=-1;
            if (armed && point.Z<=0.012f && point.Z>= -0.018f && key>=0)
            { armed=false; Held=key; return key; }
            return -1;
        }
    }
    internal static class PhysicalSurface
    {
        internal static Vector2I TextureSize(SurfaceStyle style) => style==SurfaceStyle.Label ? new Vector2I(1536,308) : style==SurfaceStyle.BlockInfo ? new Vector2I(2048,1280) : new Vector2I(1024,640);
        private sealed class Cache { public OverlayCanvas Canvas; public ShaderResourceView Texture; public string Content; public int Revision; public EssentialHud.View Status; }
        private static readonly Dictionary<string,Cache> cache=new Dictionary<string,Cache>();
        private static readonly Font title=new Font("Segoe UI",32,FontStyle.Bold,GraphicsUnit.Pixel),text=new Font("Segoe UI",27,FontStyle.Regular,GraphicsUnit.Pixel);
        private static readonly StringFormat centered=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center };
        private static readonly List<NativeSprite> sprites=new List<NativeSprite>();
        private static readonly System.Reflection.FieldInfo gbuffer=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main");
        internal static ShaderResourceView SceneDepth()
        {
            object depth=CockpitRender.Member(gbuffer.GetValue(null),"ResolvedDepthStencil");
            return (ShaderResourceView)CockpitRender.Member(CockpitRender.Member(depth,"SrvDepth"),"Srv");
        }
        internal static Vector3 Point(SurfaceView surface,Vector3D world)
        { return (Vector3)Vector3D.Transform(world,MatrixD.Invert(surface.Pose)); }
        internal static Vector2 UV(SurfaceView s,Vector3 local) => new Vector2(local.X/s.Width+0.5f,0.5f-local.Y/s.Height);
        internal static float KeyHeight(SurfaceView s) => s.Style==SurfaceStyle.ModelControl ? 0 : s.Style==SurfaceStyle.WristStatus ? .001f : .006f;
        internal static void Paint(OverlayCanvas target,SurfaceView s)
        {
            if(s.Style==SurfaceStyle.Pointer) { target.Clear(Color.White); return; }
            if(s.Style==SurfaceStyle.BlockInfo) { BlockInspection.Paint(target,s); return; }
            if(s.Style==SurfaceStyle.Label)
            {
                PaintSwitchInfo(target,s);
                return;
            }
            if(s.Style==SurfaceStyle.ModelControl)
            {
                target.Clear(Color.Transparent);
                if(!s.GeometryFeedback && (s.Hover>=0 || s.Pressed>=0 || s.Handle==1))
                {
                    using(var brush=new SolidBrush(s.Pressed>=0 ? Color.FromArgb(100,120,255,160) : Color.FromArgb(75,135,215,240)))
                        target.Graphics.FillEllipse(brush,96,60,832,520);
                }
                return;
            }
            if(s.Id=="Seat") { PaintSeat(target,s); return; }
            if(s.Style==SurfaceStyle.WristStatus) { PaintWrist(target,s); return; }
            if(s.Style==SurfaceStyle.WristMenu) { PaintWristMenu(target,s); return; }
            target.Clear(Color.FromArgb(255,12,20,28));
            var g=target.Graphics;
            using (var pen=new Pen(Color.FromArgb(255,84,125,143),6)) g.DrawRectangle(pen,5,5,1014,630);
            if(s.Style!=SurfaceStyle.Keyboard) g.DrawString(s.Title ?? "",title,Brushes.LightCyan,new System.Drawing.RectangleF(26,18,972,48),centered);
            var textBounds=s.Style==SurfaceStyle.Keyboard ? new System.Drawing.RectangleF(28,24,968,95) : new System.Drawing.RectangleF(28,75,968,170);
            g.DrawString(s.Text ?? "",text,Brushes.White,textBounds,centered);
            for(int i=0;i<s.Keys.Length;i++)
            {
                var k=s.Keys[i]; var b=k.Bounds;
                var r=new System.Drawing.RectangleF(b.X*1024,b.Y*640,b.Width*1024,b.Height*640);
                using(var brush=new SolidBrush(i==s.Pressed ? Color.FromArgb(40,133,151) : i==s.Hover ? Color.FromArgb(55,83,100) : Color.FromArgb(31,47,60))) g.FillRectangle(brush,r);
                using(var pen=new Pen(i==s.Hover ? Color.Cyan : Color.FromArgb(86,117,133),2)) g.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);
                g.DrawString(k.Label,text,Brushes.White,r,centered);
            }
            if(s.Style==SurfaceStyle.Keyboard)
            {
                using(var pen=new Pen(s.Handle==1 ? Color.Cyan : Color.LightSteelBlue,5) { StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawLine(pen,464,611,560,611);
                using(var pen=new Pen(s.Handle==2 ? Color.Cyan : Color.LightSteelBlue,4) { StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round })
                    g.DrawArc(pen,970,588,26,26,0,90);
            }
            g.FillRectangle(Brushes.White,1020,636,4,4);
        }
        private static void PaintSwitchInfo(OverlayCanvas target,SurfaceView s)
        {
            target.Clear(Color.Transparent);
            var g=target.Graphics;
            var saved=g.Save();
            g.ScaleTransform(target.Width/768f,target.Height/154f);
            using(var shape=Rounded(new System.Drawing.RectangleF(2,2,764,150),15))
            using(var fill=new SolidBrush(Color.FromArgb(230,12,24,33))) g.FillPath(fill,shape);
            using(var font=new Font("Segoe UI",39,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var format=new StringFormat { LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap })
                g.DrawString(s.Title ?? "",font,s.Enabled ? Brushes.White : Brushes.Gray,new System.Drawing.RectangleF(153,8,568,138),format);
            if(s.Icons.Length==0 && !string.IsNullOrEmpty(s.Text))
                using(var symbol=new Font("Segoe UI",90,FontStyle.Regular,GraphicsUnit.Pixel))
                    g.DrawString(s.Text,symbol,Brushes.LightCyan,new System.Drawing.RectangleF(12,5,130,139),centered);
            if(s.Levels?.Length>0)
                using(var state=new SolidBrush(s.Levels[0]>.99f ? Color.LightGreen : s.Levels[0]>.01f ? Color.Orange : Color.FromArgb(83,103,111)))
                    g.FillEllipse(state,735,64,20,20);
            g.Restore(saved);
            foreach(string icon in s.Icons)
                target.Icon(icon,target.Width*12f/768,target.Height*12f/154,target.Width*130f/768,target.Height*130f/154,new Vector4(0,0,1,1),s.Enabled ? Color.White : Color.Gray);
            if(!string.IsNullOrEmpty(s.SubIcon))
                target.Icon(s.SubIcon,target.Width*91f/768,target.Height*91f/154,target.Width*51f/768,target.Height*51f/154,new Vector4(0,0,1,1),Color.White);
        }

        private static void DrawLock(Graphics g,System.Drawing.RectangleF r,bool unlocked,bool enabled)
        {
            var state=g.Save();
            float size=Math.Min(r.Width,r.Height)*.80f;
            g.TranslateTransform(r.X+r.Width/2,r.Y+r.Height/2); g.ScaleTransform(size,size);
            Color color=!enabled ? Color.SlateGray : unlocked ? Color.Orange : Color.LightGreen;
            using(var pen=new Pen(color,.10f))
            using(var brush=new SolidBrush(color))
            {
                g.FillRectangle(brush,-.34f,-.02f,.68f,.43f);
                g.DrawArc(pen,unlocked ? -.02f : -.23f,-.47f,.46f,.56f,180,180);
                g.DrawLine(pen,unlocked ? .44f : .23f,-.19f,unlocked ? .44f : .23f,.02f);
                if(!unlocked) g.DrawLine(pen,-.23f,-.19f,-.23f,.02f);
            }
            g.Restore(state);
        }
        private static void PaintSeat(OverlayCanvas target,SurfaceView s)
        {
            target.Clear(Color.FromArgb(255,28,32,34));
            var g=target.Graphics;
            var saved=g.Save();
            try
            {
                // Draw in physical units so symbols keep their shape on narrow consoles.
                g.ScaleTransform(1024/s.Width,640/s.Height);
                using(var border=new Pen(Color.FromArgb(95,109,115),.0006f))
                {
                    g.DrawRectangle(border,.002f,.002f,s.Width-.004f,s.Height-.004f);
                    for(int i=0;i<s.Keys.Length;i++)
                    {
                        var b=s.Keys[i].Bounds;
                        if(b.Width<=0 || b.Height<=0) continue;
                        var r=new System.Drawing.RectangleF(b.X*s.Width,b.Y*s.Height,b.Width*s.Width,b.Height*s.Height);
                        using(var path=Rounded(r,.003f))
                        using(var brush=new SolidBrush(i==s.Pressed ? Color.FromArgb(38,124,139) : i==s.Hover ? Color.FromArgb(61,85,94) : Color.FromArgb(44,51,56)))
                        { g.FillPath(brush,path); g.DrawPath(border,path); }
                        if(i==7) DrawLock(g,r,s.Handle==1,s.Keys[i].Enabled);
                        else if(i<9) DrawSeatSymbol(g,i,r,s.Keys[i].Enabled ? Color.LightCyan : Color.SlateGray);
                        else
                        {
                            bool on=s.Levels!=null && i-9<s.Levels.Length && s.Levels[i-9]>.5f;
                            using(var status=new SolidBrush(on ? Color.FromArgb(99,229,158) : Color.FromArgb(115,126,131)))
                                g.FillRectangle(status,r.X+r.Width*.22f,r.Bottom-.0025f,r.Width*.56f,.0012f);
                        }
                    }

                }
            }
            finally { g.Restore(saved); }
            for(int i=9;i<s.Keys.Length;i++)
            {
                var b=s.Keys[i].Bounds;
                if(b.Width<=0 || b.Height<=0) continue;
                float size=Math.Min(b.Width*s.Width,b.Height*s.Height)*.70f;
                target.Icon(NativeSprites.Hud(SeatPanel.IconNames[i-9]),
                    (b.Center.X-size/s.Width/2)*1024,(b.Center.Y-size/s.Height/2-.006f)*640,
                    size/s.Width*1024,size/s.Height*640,new Vector4(0,0,1,1),Color.LightCyan);
            }
            g.FillRectangle(Brushes.White,1020,636,4,4);
        }
        private static void DrawSeatSymbol(Graphics g,int key,System.Drawing.RectangleF r,Color color)
        {
            var saved=g.Save();
            float size=Math.Min(r.Width,r.Height)*.68f;
            g.TranslateTransform(r.X+r.Width/2,r.Y+r.Height/2); g.ScaleTransform(size,size);
            using(var brush=new SolidBrush(color))
            if(key==4 || key==8)
            {
                using(var pen=new Pen(color,.075f) { LineJoin=System.Drawing.Drawing2D.LineJoin.Round })
                {
                    g.DrawArc(pen,-.43f,-.43f,.86f,.86f,40,285);
                    if(key==4)
                    {
                        g.DrawLines(pen,new[] {new PointF(-.16f,-.23f),new PointF(-.16f,.12f),new PointF(.20f,.12f),new PointF(.20f,.26f)});
                        g.DrawLine(pen,-.16f,.12f,-.16f,.26f);
                    }
                    else foreach(float x in new[] {-.17f,.17f})
                    {
                        g.FillRectangle(brush,x-.065f,-.22f,.13f,.20f);
                        g.DrawLine(pen,x,-.04f,x,.20f); g.DrawLine(pen,x-.10f,.23f,x+.10f,.23f);
                    }
                }
                g.FillPolygon(brush,new[] { new PointF(.45f,-.36f),new PointF(.22f,-.34f),new PointF(.41f,-.14f) });
            }
            else
            {
                g.RotateTransform(key==3 ? -90 : key==5 ? 90 : key==2 || key==6 ? 180 : 0);
                if(key==0 || key==2) g.FillPolygon(brush,new[] { new PointF(0,-.43f),new PointF(.43f,.32f),new PointF(-.43f,.32f) });
                else g.FillPolygon(brush,new[] { new PointF(0,-.46f),new PointF(.43f,-.02f),new PointF(.16f,-.02f),
                    new PointF(.16f,.44f),new PointF(-.16f,.44f),new PointF(-.16f,-.02f),new PointF(-.43f,-.02f) });
            }
            g.Restore(saved);
        }
        private static void PaintWrist(OverlayCanvas target,SurfaceView s) => EssentialHud.PaintWrist(target,s.Status,s.Width,s.Height);
        private static void PaintWristMenu(OverlayCanvas target,SurfaceView s)
        {
            target.Clear(Color.FromArgb(255,12,20,28));
            var g=target.Graphics;
            using(var labelFont=new Font("Segoe UI",23,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisWord })
            for(int i=0;i<s.Keys.Length;i++)
            {
                var key=s.Keys[i]; var b=key.Bounds;
                var r=new System.Drawing.RectangleF(b.X*1024,b.Y*640,b.Width*1024,b.Height*640);
                using(var path=Rounded(r,10))
                using(var brush=new SolidBrush(i==s.Pressed ? Color.FromArgb(40,133,151) : i==s.Hover ? Color.FromArgb(55,83,100) : Color.FromArgb(31,47,60))) g.FillPath(brush,path);
                var label=key.Icons.Length>0 ? new System.Drawing.RectangleF(r.X+4,r.Bottom-40,r.Width-8,36):r;
                g.DrawString(key.Label,labelFont,key.Enabled ? Brushes.White:Brushes.Gray,label,format);
                float size=Math.Min(66,r.Height-43),x=r.X+(r.Width-size)/2,y=r.Y+4;
                foreach(string icon in key.Icons) target.Icon(icon,x,y,size,key.Enabled);
                if(key.SubIcon!=null) target.Icon(key.SubIcon,x+size*.6f,y+size*.6f,size*.4f,key.Enabled);
                if(!string.IsNullOrEmpty(key.Text)) g.DrawString(key.Text,labelFont,Brushes.LightCyan,r.X+8,r.Y+3);
                if(key.Active) using(var pen=new Pen(Color.FromArgb(125,224,159),3)) g.DrawLine(pen,r.X+12,r.Bottom-4,r.Right-12,r.Bottom-4);
            }
            g.FillRectangle(Brushes.White,1020,636,4,4);
        }
        internal static System.Drawing.Drawing2D.GraphicsPath Rounded(System.Drawing.RectangleF r,float radius)
        {
            var p=new System.Drawing.Drawing2D.GraphicsPath(); float d=radius*2;
            p.AddArc(r.Left,r.Top,d,d,180,90); p.AddArc(r.Right-d,r.Top,d,d,270,90);
            p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90); p.AddArc(r.Left,r.Bottom-d,d,d,90,90); p.CloseFigure(); return p;
        }
        internal static NativeSprite Quad(ShaderResourceView texture,MatrixD pose,VRageMath.RectangleF bounds,Vector4 uv,Vector4 tint,MatrixD view,MatrixD projection,float z=0)
        {
            MatrixD transform=pose*view*projection;
            return new NativeSprite(null,default(VRageMath.RectangleF),tint) { Projected=true,Texture=texture,UV=uv,
                TopLeft=(Vector4)Vector4D.Transform(new Vector4D(bounds.X,bounds.Y,z,1),transform),
                TopRight=(Vector4)Vector4D.Transform(new Vector4D(bounds.X+bounds.Width,bounds.Y,z,1),transform),
                BottomLeft=(Vector4)Vector4D.Transform(new Vector4D(bounds.X,bounds.Y-bounds.Height,z,1),transform),
                BottomRight=(Vector4)Vector4D.Transform(new Vector4D(bounds.X+bounds.Width,bounds.Y-bounds.Height,z,1),transform) };
        }
        public static void Draw(Texture2D target,IEnumerable<SurfaceView> surfaces,MatrixD view,MatrixD projection,ShaderResourceView depth,ShaderResourceView handDepth=null)
        {
            sprites.Clear();
            foreach(var s in surfaces.OrderBy(s=>s.Style==SurfaceStyle.Pointer ? 2 : s.Style==SurfaceStyle.Label ? 1 : 0).ThenBy(s=>Vector3D.Transform(s.Pose.Translation,view).Z))
            {
                if(!cache.TryGetValue(s.Id,out var c))
                {
                    var size=TextureSize(s.Style);
                    var canvas=new OverlayCanvas(s.Id,size.X,size.Y,1,false,target.Device,mipMaps:true);
                    cache[s.Id]=c=new Cache { Canvas=canvas,Texture=new ShaderResourceView(target.Device,canvas.Texture) };
                }
                string content=s.ContentKey;
                if(c.Content!=content || c.Revision!=NativeSprites.Revision || (s.Style==SurfaceStyle.WristStatus && !ReferenceEquals(c.Status,s.Status)))
                { Paint(c.Canvas,s); c.Canvas.Upload(); target.Device.ImmediateContext.GenerateMips(c.Texture); c.Content=content; c.Revision=NativeSprites.Revision; c.Status=s.Status; }
                var full=new VRageMath.RectangleF(-s.Width/2,s.Height/2,s.Width,s.Height);
                if(s.Style==SurfaceStyle.Pointer)
                {
                    Vector3D toward=MatrixD.Invert(view).Translation-s.Pose.Translation;
                    Vector3D normal=toward-s.Pose.Forward*Vector3D.Dot(toward,s.Pose.Forward);
                    if(normal.LengthSquared()<1e-10) continue;
                    var pose=MatrixD.CreateWorld(s.Pose.Translation,-Vector3D.Normalize(normal),s.Pose.Forward);
                    var pointer=Quad(c.Texture,pose,full,new Vector4(0,0,1,1),new Vector4(.33f,.92f,1,s.RoundEnds ? .55f : 1),view,projection);
                    if(s.RoundEnds) pointer.Rounded=new Vector2(.5f,s.Width/(2*s.Height));
                    sprites.Add(pointer);
                    continue;
                }
                if(s.Style==SurfaceStyle.ModelControl || s.Style==SurfaceStyle.Label || s.Style==SurfaceStyle.BlockInfo)
                {
                    if(Vector3D.Dot(s.Pose.Backward,MatrixD.Invert(view).Translation-s.Pose.Translation)>0)
                    {
                        var sprite=Quad(c.Texture,s.Pose,full,new Vector4(0,0,1,1),Vector4.One,view,projection,.001f);
                        sprite.IgnoreSceneDepth=s.Style==SurfaceStyle.Label || s.Style==SurfaceStyle.BlockInfo;
                        sprites.Add(sprite);
                    }
                    continue;
                }
                float thickness=s.Style==SurfaceStyle.WristStatus ? .002f : .008f;
                sprites.Add(Quad(c.Texture,s.Pose,full,new Vector4(.01f,.01f,.001f,.001f),Vector4.One,view,projection,-thickness));
                for(int edge=0;edge<4;edge++)
                {
                    MatrixD side=MatrixD.CreateRotationY(edge<2 ? (edge==0 ? Math.PI/2 : -Math.PI/2) : 0);
                    if(edge>=2) side=MatrixD.CreateRotationX(edge==2 ? Math.PI/2 : -Math.PI/2);
                    side.Translation=edge<2 ? new Vector3D((edge==0 ? -1 : 1)*s.Width/2,0,-thickness/2) : new Vector3D(0,(edge==2 ? -1 : 1)*s.Height/2,-thickness/2);
                    float w=edge<2 ? thickness : s.Width,h=edge<2 ? s.Height : thickness;
                    sprites.Add(Quad(c.Texture,side*s.Pose,new VRageMath.RectangleF(-w/2,h/2,w,h),new Vector4(.01f,.01f,.001f,.001f),Vector4.One,view,projection));
                }
                if(Vector3D.Dot(s.Pose.Backward,MatrixD.Invert(view).Translation-s.Pose.Translation)<=0) continue;
                sprites.Add(Quad(c.Texture,s.Pose,full,new Vector4(0,0,1,1),Vector4.One,view,projection));
                foreach(var k in s.Keys)
                {
                    var b=k.Bounds;
                    int index=Array.IndexOf(s.Keys,k);
                    float raised=index==s.Pressed ? .001f : KeyHeight(s);
                    // Extruded key walls prevent the face from looking like a hovering label.
                    var bounds=new VRageMath.RectangleF((b.X-.5f)*s.Width,(.5f-b.Y)*s.Height,b.Width*s.Width,b.Height*s.Height);
                    for(int edge=0;edge<4;edge++)
                    {
                        MatrixD side=edge<2 ? MatrixD.CreateRotationY(edge==0 ? Math.PI/2 : -Math.PI/2) : MatrixD.CreateRotationX(edge==2 ? Math.PI/2 : -Math.PI/2);
                        side.Translation=edge<2 ? new Vector3D(bounds.X+(edge==0 ? 0 : bounds.Width),bounds.Y-bounds.Height/2,raised/2)
                            : new Vector3D(bounds.X+bounds.Width/2,bounds.Y-(edge==2 ? bounds.Height : 0),raised/2);
                        float w=edge<2 ? raised : bounds.Width,h=edge<2 ? bounds.Height : raised;
                        sprites.Add(Quad(c.Texture,side*s.Pose,new VRageMath.RectangleF(-w/2,h/2,w,h),new Vector4(.01f,.01f,.001f,.001f),new Vector4(1.5f,1.5f,1.5f,1),view,projection));
                    }
                    sprites.Add(Quad(c.Texture,s.Pose,new VRageMath.RectangleF((b.X-.5f)*s.Width,(.5f-b.Y)*s.Height,b.Width*s.Width,b.Height*s.Height),
                        new Vector4(b.X,b.Y,b.Width,b.Height),Vector4.One,view,projection,raised));
                }
                if(s.TouchPoint.HasValue)
                {
                    var p=s.TouchPoint.Value;
                    sprites.Add(Quad(c.Texture,s.Pose,new VRageMath.RectangleF(p.X-.003f,p.Y+.003f,.006f,.006f),new Vector4(1022f/1024,638f/640,0,0),
                        new Vector4(.2f,.9f,1,1),view,projection,p.Z));
                }
            }
            NativeSprites.Draw(target,sprites,depth,handDepth);
        }
    }
}
