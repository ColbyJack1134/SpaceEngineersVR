using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using SharpDX.Direct3D11;
using VRageMath;
using Color=System.Drawing.Color;
using Matrix=VRageMath.Matrix;

namespace SpaceEngineersVR.Player
{
    internal static class WindowFrame
    {
        internal sealed class Snapshot
        {
            public Matrix Pose;
            public float Width,Height,BarOffset=.035f;
            public int Hover;
            public bool Zoom,Close;
            public int Monitor;
            public int SignalMode=-1;
            // Turret aim mode icon: -1 hidden, 0 vanilla, 1 head, 2 head with the face screen.
            public int Aim=-1;
        }
        private sealed class Cache
        {
            internal OverlayCanvas Canvas;
            internal ShaderResourceView Texture;
            internal string Key;
        }
        private static readonly Dictionary<string,Cache> frames=new Dictionary<string,Cache>();
        internal static float ZoomX(float width,bool plus) => -width/2+(plus ? .115f:.05f);
        // Move bar hit half-length; the drawn bar is a little shorter.
        internal static float BarHalf(float width) => MathHelper.Clamp(width*.095f,.125f,.19f);
        internal const int CloseHover=5,MonitorHover=6,SignalsHover=7,AimHover=8;
        internal static float SignalsX(float width) => width/2-.035f;
        internal static float SignalsY(float height) => height/2+.04f;
        internal static float AimX(float width) => ZoomX(width,false);
        private static float Top(Snapshot s) => s.SignalMode>=0 || s.Aim>=0 ? .08f:.03f;
        internal static int Control(Vector3 point,float width,float height,float barOffset,bool zoom,bool close,bool monitor,bool signals=false,bool aim=false)
        {
            if(signals && Math.Abs(point.Y-SignalsY(height))<.027f && Math.Abs(point.X-SignalsX(width))<.027f) return SignalsHover;
            if(aim && Math.Abs(point.Y-SignalsY(height))<.027f && Math.Abs(point.X-AimX(width))<.035f) return AimHover;
            if(Math.Abs(point.Y+height/2+barOffset)>=.035f) return 0;
            if(Math.Abs(point.X-ZoomX(width,false))<.027f) return zoom ? 3 : close ? CloseHover : 0;
            if(Math.Abs(point.X-ZoomX(width,true))<.027f) return zoom ? 4 : monitor ? MonitorHover : 0;
            return 0;
        }
        internal static void Reset(string id)
        {
            if(!frames.TryGetValue(id,out var frame)) return;
            frame.Texture.Dispose(); frame.Canvas.Dispose(); frames.Remove(id);
        }
        internal static void Paint(OverlayCanvas canvas,WindowFrame.Snapshot s)
        {
            canvas.Clear(Color.Transparent);
            var g=canvas.Graphics; var state=g.Save();
            try
            {
                float w=s.Width+.06f,h=s.Height+Top(s)+.085f+s.BarOffset;
                g.ScaleTransform(1600/w,1100/h); g.TranslateTransform(w/2,Top(s)+s.Height/2);
                // Native menu contents retain their own texture, without a tint.
                float bottom=s.Height/2+s.BarOffset;
                using(var pen=new Pen(s.Hover==1 ? Color.Cyan : Color.White,.010f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
                {
                    float half=BarHalf(s.Width)*.79f;
                    g.DrawLine(pen,-half,bottom,half,bottom);
                }
                using(var pen=new Pen(s.Hover==2 ? Color.Cyan : Color.White,.006f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
                {
                    g.DrawLine(pen,s.Width/2-.025f,bottom+.024f,s.Width/2+.024f,bottom+.024f);
                    g.DrawLine(pen,s.Width/2+.024f,bottom+.024f,s.Width/2+.024f,bottom-.025f);
                }
                if(s.Zoom)
                {
                    using(var font=new Font("Segoe UI",.045f,FontStyle.Regular,GraphicsUnit.Pixel))
                    using(var centered=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                    {
                        g.DrawString("−",font,s.Hover==3 ? Brushes.Cyan:Brushes.White,ZoomX(s.Width,false),bottom,centered);
                        g.DrawString("+",font,s.Hover==4 ? Brushes.Cyan:Brushes.White,ZoomX(s.Width,true),bottom,centered);
                    }
                }
                if(s.SignalMode>=0)
                    using(var pen=new Pen(s.Hover==SignalsHover ? Color.Cyan:Color.White,.003f) {StartCap=LineCap.Round,EndCap=LineCap.Round})
                    {
                        float x=SignalsX(s.Width),y=-SignalsY(s.Height);
                        g.DrawPolygon(pen,new[] {new PointF(x,y-.016f),new PointF(x+.016f,y),new PointF(x,y+.016f),new PointF(x-.016f,y)});
                        if(s.SignalMode!=3) g.DrawEllipse(pen,x-.003f,y-.003f,.006f,.006f);
                        if(s.SignalMode<=1) g.DrawLine(pen,x-.012f,y+.025f,x+.012f,y+.025f);
                        if(s.SignalMode==1) g.DrawLine(pen,x-.018f,y+.031f,x+.018f,y+.031f);
                        if(s.SignalMode==3) g.DrawLine(pen,x-.021f,y+.021f,x+.021f,y-.021f);
                    }
                if(s.Aim>=0)
                    using(var pen=new Pen(s.Hover==AimHover ? Color.Cyan:Color.White,.003f) {StartCap=LineCap.Round,EndCap=LineCap.Round})
                        AimIcon(g,pen,AimX(s.Width),-SignalsY(s.Height),s.Aim);
                if(s.Close)
                    using(var pen=new Pen(s.Hover==CloseHover ? Color.Cyan : Color.White,.006f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
                    {
                        float x=ZoomX(s.Width,false);
                        g.DrawLine(pen,x-.014f,bottom-.014f,x+.014f,bottom+.014f);
                        g.DrawLine(pen,x-.014f,bottom+.014f,x+.014f,bottom-.014f);
                    }
                if(s.Monitor>0)
                {
                    var color=s.Hover==MonitorHover ? Color.Cyan : Color.White;
                    float x=ZoomX(s.Width,true);
                    using(var pen=new Pen(color,.004f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
                    {
                        g.DrawRectangle(pen,x-.019f,bottom-.016f,.038f,.024f);
                        g.DrawLine(pen,x,bottom+.008f,x,bottom+.014f);
                        g.DrawLine(pen,x-.009f,bottom+.016f,x+.009f,bottom+.016f);
                    }
                    using(var font=new Font("Segoe UI",.017f,FontStyle.Bold,GraphicsUnit.Pixel))
                    using(var brush=new SolidBrush(color))
                    using(var centered=new StringFormat {Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center})
                        g.DrawString(s.Monitor.ToString(),font,brush,x,bottom-.0035f,centered);
                }
            }
            finally { g.Restore(state); }
        }
        // Crosshair for vanilla aim, an eye for head aim, the eye inside a screen when the picture is on the face.
        private static void AimIcon(Graphics g,Pen pen,float x,float y,int mode)
        {
            if(mode==0)
            {
                g.DrawEllipse(pen,x-.009f,y-.009f,.018f,.018f);
                foreach(var (dx,dy) in new[] {(1,0),(-1,0),(0,1),(0,-1)}) g.DrawLine(pen,x+dx*.013f,y+dy*.013f,x+dx*.02f,y+dy*.02f);
                return;
            }
            float w=mode==2 ? .017f:.022f,h=mode==2 ? .009f:.012f;
            using(var eye=new GraphicsPath())
            {
                eye.AddBezier(x-w,y,x-w*.4f,y-h*1.6f,x+w*.4f,y-h*1.6f,x+w,y);
                eye.AddBezier(x+w,y,x+w*.4f,y+h*1.6f,x-w*.4f,y+h*1.6f,x-w,y);
                g.DrawPath(pen,eye);
            }
            float r=h*.55f;
            g.DrawEllipse(pen,x-r,y-r,r*2,r*2);
            if(mode==2) g.DrawRectangle(pen,x-.026f,y-.018f,.052f,.036f);
        }
        internal static void Draw(Texture2D target,string id,WindowFrame.Snapshot s,MatrixD view,MatrixD projection,ShaderResourceView handDepth=null,ShaderResourceView controllerDepth=null)
        {
            if(!frames.TryGetValue(id,out var frame))
            {
                var canvas=new OverlayCanvas("Window frame "+id,1600,1100,1,false,target.Device,true);
                frames[id]=frame=new Cache {Canvas=canvas,Texture=new ShaderResourceView(target.Device,canvas.Texture)};
            }
            string key=s.Width+"|"+s.Height+"|"+s.Hover+"|"+s.BarOffset+"|"+s.Zoom+"|"+s.Close+"|"+s.Monitor+"|"+s.SignalMode+"|"+s.Aim;
            if(frame.Key!=key)
            {
                Paint(frame.Canvas,s); frame.Canvas.Upload(); target.Device.ImmediateContext.GenerateMips(frame.Texture); frame.Key=key;
            }
            var sprite=PhysicalSurface.Quad(frame.Texture,s.Pose,
                new VRageMath.RectangleF(-s.Width/2-.03f,s.Height/2+Top(s),s.Width+.06f,s.Height+Top(s)+.085f+s.BarOffset),
                new Vector4(0,0,1,1),Vector4.One,view,projection);
            NativeSprites.Draw(target,new[] {sprite},controllerDepth,handDepth);
        }
    }
}
