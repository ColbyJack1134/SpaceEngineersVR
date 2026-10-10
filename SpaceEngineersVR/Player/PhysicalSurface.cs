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
    internal enum SurfaceStyle { Default,WristStatus,WristMenu,Keyboard,ModelControl,Pointer,Label,BlockInfo,Ammo }
    internal sealed class SurfaceKey
    {
        public string Label;
        internal int SeatControl=-1;
        public bool Enabled=true,Active;
        public string[] Icons=new string[0];
        public string SubIcon,Text,Value;
        public ActionChoice Action;
        internal bool SearchResult;
        public float? Knob,Slider;
        public Action<float> Change;
        public bool Caption,Horizontal;
        public bool Invisible,DirectOnly,Round;
        public VRageMath.RectangleF Bounds;
        internal bool Contains(Vector2 uv)
        {
            if(Bounds.Width<=0 || Bounds.Height<=0 || !Bounds.Contains(uv)) return false;
            var delta=(uv-Bounds.Center)/new Vector2(Bounds.Width/2,Bounds.Height/2);
            return !Round || delta.LengthSquared()<=1;
        }
        public SurfaceKey(string label,float x,float y,float w,float h) { Label=label; Bounds=new VRageMath.RectangleF(x,y,w,h); }
    }
    internal sealed class SurfaceView
    {
        public string Id,Title,Text,Action,Argument;
        public EssentialHud.View Status;
        public BlockInspection.Data Block;
        public bool SignalWindow,SeatSettings,FlightPage,Desktop;
        public WristSignals.View Signals;
        public WristHud.View HudSettings;
        public string[] Icons=new string[0];
        public string SubIcon;
        public bool Enabled=true;
        public MatrixD Pose;
        public MatrixD? HandLocal;
        internal MatrixD? WindowPose;
        internal uint RenderParent=uint.MaxValue;
        internal MatrixD ParentLocal;
        internal int WindowHover;
        internal SurfaceView At(MatrixD pose) { var copy=(SurfaceView)MemberwiseClone(); copy.Pose=pose; return copy; }
        public float Width,Height;
        public SurfaceKey[] Keys=new SurfaceKey[0];
        public int Hover=-1,Pressed=-1,HoverAlt=-1,PressedAlt=-1;
        public bool LeftHand;
        internal bool Hovered(int i) => i>=0 && (i==Hover || i==HoverAlt);
        internal bool IsPressed(int i) => i>=0 && (i==Pressed || i==PressedAlt);
        public bool TrackingSpace,GeometryFeedback,RoundEnds;
        public SurfaceStyle Style;
        public float[] Levels;
        public int Handle;
        public string ContentKey => Desktop+"|"+FlightPage+"|"+SeatSettings+"|"+SignalWindow+"|"+(SignalWindow ? Signals?.Tint:0)+"|"+(SignalWindow && Signals?.Candidates.Length==0)+"|"+(SignalWindow ? Plugin.Common.Config?.WaypointMode:0)+"|"+Style+"|"+Handle+"|"+Title+"|"+Text+"|"+Action+"|"+Argument+"|"+Hover+"|"+Pressed+"|"+HoverAlt+"|"+PressedAlt+"|"+string.Join("|",Keys.Select(k=>k.Label+":"+k.Horizontal+":"+k.Enabled+":"+k.Active+":"+k.Text+":"+k.SubIcon+":"+string.Join(",",k.Icons)))+
            "|"+HudSettings?.Key+"|"+string.Join("|",Keys.Select(k=>k.Value+":"+k.Knob+":"+k.Slider))+"|"+string.Join("|",Icons)+"|"+SubIcon+"|"+Enabled+"|"+GeometryFeedback+
            (Levels==null ? "" : string.Join(",",Levels.Select(v=>v.ToString("0.00"))))+(Id=="Seat" ? "|"+Width+"|"+Height : "");
        public int KeyAt(Vector2 uv)
        {
            for (int i=0;i<Keys.Length;i++) if (Keys[i].Enabled && Keys[i].Contains(uv)) return i;
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
        internal static readonly Vector4 LeftLaser=new Vector4(1,.706f,.235f,1);
        internal static Vector2I TextureSize(SurfaceStyle style) => style==SurfaceStyle.Ammo ? new Vector2I(512,192) : style==SurfaceStyle.Label ? new Vector2I(1536,236) : style==SurfaceStyle.BlockInfo ? new Vector2I(2048,1280) : new Vector2I(1024,640);
        private sealed class Cache { public OverlayCanvas Canvas; public ShaderResourceView Texture; public string Content; public int Revision; public EssentialHud.View Status; }
        private static readonly Dictionary<string,Cache> cache=new Dictionary<string,Cache>();
        private static readonly Font title=new Font("Segoe UI",32,FontStyle.Bold,GraphicsUnit.Pixel),text=new Font("Segoe UI",27,FontStyle.Regular,GraphicsUnit.Pixel),smallText=new Font("Segoe UI",21,FontStyle.Regular,GraphicsUnit.Pixel);
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
            if(s.Style==SurfaceStyle.Ammo) { WeaponAmmo.Paint(target,s); return; }
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
            if(s.Style==SurfaceStyle.WristMenu) { if(s.Desktop) PaintDesktopKeys(target,s); else if(s.SignalWindow) WristSignals.Paint(target,s); else if(s.HudSettings!=null) WristHud.Paint(target,s); else PaintWristMenu(target,s); return; }
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
                bool leftOnly=i!=s.Pressed && i!=s.Hover && (i==s.PressedAlt || i==s.HoverAlt);
                using(var brush=new SolidBrush(s.IsPressed(i) || k.Active ? leftOnly ? Color.FromArgb(150,96,32) : Color.FromArgb(40,133,151) :
                    s.Hovered(i) ? leftOnly ? Color.FromArgb(96,70,40) : Color.FromArgb(55,83,100) : Color.FromArgb(31,47,60))) g.FillRectangle(brush,r);
                using(var pen=new Pen(s.Hovered(i) ? leftOnly ? Color.Orange : Color.Cyan : Color.FromArgb(86,117,133),2)) g.DrawRectangle(pen,r.X,r.Y,r.Width,r.Height);
                g.DrawString(k.Label,g.MeasureString(k.Label,text).Width>r.Width-4 ? smallText:text,Brushes.White,r,centered);
            }
            g.FillRectangle(Brushes.White,1020,636,4,4);
        }
        private static void PaintSwitchInfo(OverlayCanvas target,SurfaceView s)
        {
            target.Clear(Color.Transparent);
            var g=target.Graphics;
            var saved=g.Save();
            g.ScaleTransform(target.Width/768f,target.Height/118f);
            using(var shape=Rounded(new System.Drawing.RectangleF(2,2,764,114),12))
            using(var fill=new SolidBrush(Color.FromArgb(230,12,24,33))) g.FillPath(fill,shape);
            using(var font=new Font("Segoe UI",39,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var format=new StringFormat(StringFormat.GenericTypographic) { LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap })
            {
                bool details=!string.IsNullOrEmpty(s.Action);
                bool argument=details && !string.IsNullOrEmpty(s.Argument);
                float width=s.Levels?.Length>0 ? 624:642;
                float split=width*2/3,topHeight=argument ? 54:110;
                g.DrawString(FitLabelText(g,s.Title,font,details ? split-17:width,format),font,s.Enabled ? Brushes.White : Brushes.Gray,new System.Drawing.RectangleF(110,4,details ? split-17:width,topHeight),format);
                if(details)
                {
                    using(var separator=new SolidBrush(s.Enabled ? Color.SlateGray:Color.DimGray))
                        g.FillEllipse(separator,105.5f+split,topHeight/2-.5f,9,9);
                    g.DrawString(FitLabelText(g,s.Action,font,width-split-17,format),font,s.Enabled ? Brushes.LightCyan:Brushes.Gray,new System.Drawing.RectangleF(127+split,4,width-split-17,topHeight),format);
                    if(argument) g.DrawString(FitLabelText(g,s.Argument,font,width,format),font,s.Enabled ? Brushes.White:Brushes.Gray,new System.Drawing.RectangleF(110,58,width,54),format);
                }
            }
            if(s.Icons.Length==0 && !string.IsNullOrEmpty(s.Text))
                using(var symbol=new Font("Segoe UI",72,FontStyle.Regular,GraphicsUnit.Pixel))
                    g.DrawString(s.Text,symbol,Brushes.LightCyan,new System.Drawing.RectangleF(8,5,94,108),centered);
            if(s.Levels?.Length>0)
                using(var state=new SolidBrush(s.Levels[0]>.99f ? Color.LightGreen : s.Levels[0]>.01f ? Color.Orange : Color.FromArgb(83,103,111)))
                    g.FillEllipse(state,741,51,16,16);
            g.Restore(saved);
            foreach(string icon in s.Icons)
                target.Icon(icon,target.Width*8f/768,target.Height*12f/118,target.Width*94f/768,target.Height*94f/118,new Vector4(0,0,1,1),s.Enabled ? Color.White : Color.Gray);
            if(!string.IsNullOrEmpty(s.SubIcon))
                target.Icon(s.SubIcon,target.Width*65f/768,target.Height*69f/118,target.Width*37f/768,target.Height*37f/118,new Vector4(0,0,1,1),Color.White);
        }
        private static string FitLabelText(Graphics g,string value,Font font,float width,StringFormat format)
        {
            if(string.IsNullOrEmpty(value)) return "";
            if(g.MeasureString(value,font,int.MaxValue,format).Width<=width) return value;
            const string more="…";
            var starts=System.Globalization.StringInfo.ParseCombiningCharacters(value);
            int low=0,high=starts.Length;
            while(low<high)
            {
                int middle=(low+high+1)/2;
                string candidate=value.Substring(0,middle==starts.Length ? value.Length:starts[middle])+more;
                if(g.MeasureString(candidate,font,int.MaxValue,format).Width<=width) low=middle;
                else high=middle-1;
            }
            return value.Substring(0,low==starts.Length ? value.Length:starts[low])+more;
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
            PaintSeatKeys(target,s,true);
        }
        private static void PaintSeatKeys(OverlayCanvas target,SurfaceView s,bool borderVisible=false)
        {
            var g=target.Graphics;
            var saved=g.Save();
            try
            {
                // Draw in physical units so symbols keep their shape on narrow consoles.
                g.ScaleTransform(1024/s.Width,640/s.Height);
                using(var border=new Pen(Color.FromArgb(95,109,115),.0006f))
                {
                    if(borderVisible) g.DrawRectangle(border,.002f,.002f,s.Width-.004f,s.Height-.004f);
                    for(int i=0;i<s.Keys.Length;i++)
                    {
                        int key=s.Keys[i].SeatControl;
                        if(key<0) continue;
                        var b=s.Keys[i].Bounds;
                        if(b.Width<=0 || b.Height<=0) continue;
                        var r=new System.Drawing.RectangleF(b.X*s.Width,b.Y*s.Height,b.Width*s.Width,b.Height*s.Height);
                        using(var path=Rounded(r,.003f))
                        using(var brush=new SolidBrush(s.IsPressed(i) ? Color.FromArgb(38,124,139) : s.Hovered(i) ? Color.FromArgb(61,85,94) : Color.FromArgb(44,51,56)))
                        { g.FillPath(brush,path); g.DrawPath(border,path); }
                        if(key==7) DrawLock(g,r,s.Handle==1,s.Keys[i].Enabled);
                        else if(key==4) DrawSettings(g,r);
                        else if(key<9) DrawSeatSymbol(g,key==8 && s.Handle!=1 ? 4:key,r,s.Keys[i].Enabled ? Color.LightCyan : Color.SlateGray);
                        else
                        {
                            bool on=s.Levels!=null && key-9<s.Levels.Length && s.Levels[key-9]>.5f;
                            using(var status=new SolidBrush(on ? Color.FromArgb(99,229,158) : Color.FromArgb(115,126,131)))
                                g.FillRectangle(status,r.X+r.Width*.22f,r.Bottom-.0025f,r.Width*.56f,.0012f);
                        }
                    }

                }
            }
            finally { g.Restore(saved); }
            for(int i=0;i<s.Keys.Length;i++)
            {
                int key=s.Keys[i].SeatControl;
                if(key<9) continue;
                var b=s.Keys[i].Bounds;
                if(b.Width<=0 || b.Height<=0) continue;
                float size=Math.Min(b.Width*s.Width,b.Height*s.Height)*.70f;
                target.Icon(NativeSprites.Hud(SeatPanel.IconNames[key-9]),
                    (b.Center.X-size/s.Width/2)*1024,(b.Center.Y-size/s.Height/2-.006f)*640,
                    size/s.Width*1024,size/s.Height*640,new Vector4(0,0,1,1),Color.LightCyan);
            }
            g.FillRectangle(Brushes.White,1020,636,4,4);
        }
        internal static void DrawSettings(Graphics g,System.Drawing.RectangleF r)
        {
            float size=Math.Min(r.Width,r.Height)*.68f;
            var points=new List<PointF>();
            for(int i=0;i<8;i++) foreach(var part in new[] {new Vector2(-.5f,26),new Vector2(-.26f,26),new Vector2(-.20f,34),new Vector2(.20f,34),new Vector2(.26f,26),new Vector2(.5f,26)})
            {
                double a=(i+part.X)*Math.PI/4; float radius=part.Y/68*size;
                points.Add(new PointF(r.X+r.Width/2+(float)Math.Cos(a)*radius,r.Y+r.Height/2+(float)Math.Sin(a)*radius));
            }
            using(var pen=new Pen(Color.LightCyan,size*.055f) {LineJoin=System.Drawing.Drawing2D.LineJoin.Round})
            { g.DrawPolygon(pen,points.ToArray()); float inner=size*11/68; g.DrawEllipse(pen,r.X+r.Width/2-inner,r.Y+r.Height/2-inner,inner*2,inner*2); }
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
            PaintWristKeys(target,s);
            if(s.FlightPage) using(var font=new Font("Segoe UI",27,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var format=new StringFormat {FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter})
                target.Graphics.DrawString(s.Title,font,Brushes.White,new System.Drawing.RectangleF(26,17,970,50),format);
            if(s.SeatSettings) PaintSeatKeys(target,s);
            target.Graphics.FillRectangle(Brushes.White,1020,636,4,4);
        }
        private static void PaintDesktopKeys(OverlayCanvas target,SurfaceView s)
        {
            target.Clear(Color.FromArgb(255,12,20,28));
            var g=target.Graphics;
            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            for(int i=0;i<s.Keys.Length;i++)
            {
                var key=s.Keys[i]; var b=key.Bounds;
                if(b.Width>=1) continue;
                var r=new System.Drawing.RectangleF(b.X*1024,b.Y*640,b.Width*1024,b.Height*640);
                using(var path=Rounded(r,10))
                using(var brush=new SolidBrush(s.IsPressed(i) ? Color.FromArgb(40,133,151) : s.Hovered(i) ? Color.FromArgb(55,83,100) : Color.FromArgb(31,47,60))) g.FillPath(brush,path);
                float cx=r.X+r.Width/2,cy=r.Y+r.Height/2,u=Math.Min(r.Width,r.Height)/10;
                using(var pen=new Pen(Color.White,u*.9f) {StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round})
                {
                    if(key.Label=="Back")
                    {
                        g.DrawLine(pen,cx+2.6f*u,cy,cx-2.6f*u,cy);
                        g.DrawLine(pen,cx-2.6f*u,cy,cx-.4f*u,cy-2.2f*u); g.DrawLine(pen,cx-2.6f*u,cy,cx-.4f*u,cy+2.2f*u);
                        continue;
                    }
                    pen.Width=u*.7f;
                    g.DrawRectangle(pen,cx-3.2f*u,cy-2.6f*u,6.4f*u,4f*u);
                    g.DrawLine(pen,cx,cy+1.4f*u,cx,cy+2.6f*u); g.DrawLine(pen,cx-1.6f*u,cy+2.8f*u,cx+1.6f*u,cy+2.8f*u);
                }
                using(var font=new Font("Segoe UI",2.8f*u,FontStyle.Bold,GraphicsUnit.Pixel))
                    g.DrawString(key.Text ?? "",font,Brushes.White,new System.Drawing.RectangleF(cx-3.2f*u,cy-2.6f*u,6.4f*u,4f*u),centered);
            }
        }
        internal static void PaintWristKeys(OverlayCanvas target,SurfaceView s)
        {
            var g=target.Graphics;
            using(var labelFont=new Font("Segoe UI",23,FontStyle.Regular,GraphicsUnit.Pixel))
            using(var format=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisWord })
            for(int i=0;i<s.Keys.Length;i++)
            {
                var key=s.Keys[i]; if(key.Invisible || key.SeatControl>=0) continue; var b=key.Bounds;
                var r=new System.Drawing.RectangleF(b.X*1024,b.Y*640,b.Width*1024,b.Height*640);
                if(key.Slider.HasValue)
                {
                    using(var left=new StringFormat {Alignment=StringAlignment.Near})
                    using(var right=new StringFormat {Alignment=StringAlignment.Far})
                    {
                        g.DrawString(key.Label,labelFont,Brushes.White,r,left); g.DrawString(key.Value,labelFont,Brushes.LightCyan,r,right);
                    }
                    float railY=r.Y+r.Height*.76f,railX=r.X+10,railWidth=r.Width-20;
                    using(var rail=new Pen(Color.FromArgb(91,112,123),6)) g.DrawLine(rail,railX,railY,railX+railWidth,railY);
                    using(var fill=new Pen(Color.FromArgb(111,214,232),6)) g.DrawLine(fill,railX,railY,railX+railWidth*key.Slider.Value,railY);
                    g.FillEllipse(Brushes.LightCyan,railX+railWidth*key.Slider.Value-9,railY-9,18,18);
                    continue;
                }
                if(key.Caption) { g.DrawString(key.Label,labelFont,Brushes.LightCyan,r); continue; }
                using(var path=Rounded(r,10))
                using(var brush=new SolidBrush(s.IsPressed(i) ? Color.FromArgb(40,133,151) : s.Hovered(i) ? Color.FromArgb(55,83,100) : Color.FromArgb(31,47,60))) g.FillPath(brush,path);
                if(key.Horizontal)
                {
                    float iconSize=Math.Min(46,r.Height-22);
                    foreach(string icon in key.Icons) target.Icon(icon,r.X+14,r.Y+(r.Height-iconSize)/2,iconSize,key.Enabled);
                    using(var left=new StringFormat {Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisWord})
                        g.DrawString(key.Label,labelFont,key.Enabled ? Brushes.White:Brushes.Gray,
                            new System.Drawing.RectangleF(r.X+iconSize+26,r.Y+4,r.Width-iconSize-40,r.Height-8),left);
                    continue;
                }
                var label=key.Icons.Length>0 ? new System.Drawing.RectangleF(r.X+4,r.Bottom-40,r.Width-8,36):r;
                if(key.Value==null) g.DrawString(key.Label,labelFont,key.Enabled ? Brushes.White:Brushes.Gray,label,format);
                else
                {
                    using(var left=new StringFormat {Alignment=StringAlignment.Near,LineAlignment=StringAlignment.Center})
                    using(var right=new StringFormat {Alignment=StringAlignment.Far,LineAlignment=StringAlignment.Center})
                    {
                        var inset=new System.Drawing.RectangleF(r.X+18,r.Y,r.Width-36,r.Height);
                        g.DrawString(key.Label,labelFont,Brushes.White,inset,left);
                        g.DrawString(key.Value,labelFont,Brushes.LightCyan,inset,right);
                    }
                }
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
                {
                    Paint(c.Canvas,s); c.Canvas.Upload(); target.Device.ImmediateContext.GenerateMips(c.Texture);
                    c.Content=content; c.Revision=NativeSprites.Revision; c.Status=s.Status;
                }
                var full=new VRageMath.RectangleF(-s.Width/2,s.Height/2,s.Width,s.Height);
                if(s.Style==SurfaceStyle.Pointer)
                {
                    Vector3D toward=MatrixD.Invert(view).Translation-s.Pose.Translation;
                    Vector3D normal=toward-s.Pose.Forward*Vector3D.Dot(toward,s.Pose.Forward);
                    if(normal.LengthSquared()<1e-10) continue;
                    var pose=MatrixD.CreateWorld(s.Pose.Translation,-Vector3D.Normalize(normal),s.Pose.Forward);
                    var tint=s.LeftHand ? LeftLaser : new Vector4(.33f,.92f,1,1); tint.W=s.RoundEnds ? .55f : 1;
                    var pointer=Quad(c.Texture,pose,full,new Vector4(0,0,1,1),tint,view,projection);
                    if(s.RoundEnds) pointer.Rounded=new Vector2(.5f,s.Width/(2*s.Height));
                    sprites.Add(pointer);
                    continue;
                }
                if(s.Style==SurfaceStyle.ModelControl || s.Style==SurfaceStyle.Label || s.Style==SurfaceStyle.BlockInfo || s.Style==SurfaceStyle.Ammo)
                {
                    var pose=s.Pose;
                    if(s.RenderParent!=uint.MaxValue)
                    {
                        var parent=VRage.Render.Scene.MyIDTracker<VRage.Render.Scene.MyActor>.FindByID(s.RenderParent);
                        if(parent==null) continue;
                        pose=s.ParentLocal*parent.WorldMatrix;
                    }
                    if(Vector3D.Dot(pose.Backward,MatrixD.Invert(view).Translation-pose.Translation)>0)
                    {
                        var sprite=Quad(c.Texture,pose,full,new Vector4(0,0,1,1),Vector4.One,view,projection,.001f);
                        sprite.IgnoreSceneDepth=s.Style==SurfaceStyle.Label || s.Style==SurfaceStyle.BlockInfo;
                        sprites.Add(sprite);
                    }
                    continue;
                }
                float thickness=s.Style==SurfaceStyle.WristStatus ? .002f : .008f;
                if(!s.SignalWindow) sprites.Add(Quad(c.Texture,s.Pose,full,new Vector4(.01f,.01f,.001f,.001f),Vector4.One,view,projection,-thickness));
                for(int edge=0;edge<4 && !s.SignalWindow;edge++)
                {
                    MatrixD side=MatrixD.CreateRotationY(edge<2 ? (edge==0 ? Math.PI/2 : -Math.PI/2) : 0);
                    if(edge>=2) side=MatrixD.CreateRotationX(edge==2 ? Math.PI/2 : -Math.PI/2);
                    side.Translation=edge<2 ? new Vector3D((edge==0 ? -1 : 1)*s.Width/2,0,-thickness/2) : new Vector3D(0,(edge==2 ? -1 : 1)*s.Height/2,-thickness/2);
                    float w=edge<2 ? thickness : s.Width,h=edge<2 ? s.Height : thickness;
                    sprites.Add(Quad(c.Texture,side*s.Pose,new VRageMath.RectangleF(-w/2,h/2,w,h),new Vector4(.01f,.01f,.001f,.001f),Vector4.One,view,projection));
                }
                if(Vector3D.Dot(s.Pose.Backward,MatrixD.Invert(view).Translation-s.Pose.Translation)<=0) continue;
                sprites.Add(Quad(c.Texture,s.Pose,full,new Vector4(0,0,1,1),Vector4.One,view,projection));
                if(s.SignalWindow) WristSignals.AddSprites(sprites,target.Device,s,view,projection);
                if(s.Desktop && DesktopCapture.View!=null)
                {
                    sprites.Add(DesktopCapture.Picture(DesktopCapture.View,s.Pose,s.Width,s.Height,view,projection,target.Description.Format,.0008f));
                    if(DesktopCapture.Badge(target.Device,s.Pose,.045f,view,projection,.0016f,DesktopCapture.BadgeAlpha(DateTime.UtcNow),out var badge)) sprites.Add(badge);
                }
                foreach(var k in s.Keys)
                {
                    if(k.Knob.HasValue) {WristKnob.Add(sprites,c.Texture,s,k.Knob.Value,view,projection); continue;}
                    if(k.Invisible || k.Caption) continue;
                    var b=k.Bounds;
                    int index=Array.IndexOf(s.Keys,k);
                    float raised=s.IsPressed(index) ? .001f : KeyHeight(s);
                    // Extruded key walls prevent the face from looking like a hovering label.
                    var bounds=new VRageMath.RectangleF((b.X-.5f)*s.Width,(.5f-b.Y)*s.Height,b.Width*s.Width,b.Height*s.Height);
                    for(int edge=0;edge<4 && !s.SignalWindow;edge++)
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
            }
            NativeSprites.Draw(target,sprites,depth,handDepth);
        }
    }
}
