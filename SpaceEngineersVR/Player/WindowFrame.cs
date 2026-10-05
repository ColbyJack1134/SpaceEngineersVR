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
            public bool Zoom;
        }
        private sealed class Cache
        {
            internal OverlayCanvas Canvas;
            internal ShaderResourceView Texture;
            internal string Key;
        }
        private static readonly Dictionary<string,Cache> frames=new Dictionary<string,Cache>();
        internal static float ZoomX(float width,bool plus) => -width/2+(plus ? .115f:.05f);
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
                float w=s.Width+.06f,h=s.Height+.115f+s.BarOffset;
                g.ScaleTransform(1600/w,1100/h); g.TranslateTransform(w/2,.03f+s.Height/2);
                // Native menu contents retain their own texture, without a tint.
                float bottom=s.Height/2+s.BarOffset;
                using(var pen=new Pen(s.Hover==1 ? Color.Cyan : Color.White,.010f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
                    g.DrawLine(pen,-.15f,bottom,.15f,bottom);
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
            }
            finally { g.Restore(state); }
        }
        internal static void Draw(Texture2D target,string id,WindowFrame.Snapshot s,MatrixD view,MatrixD projection,ShaderResourceView handDepth=null,ShaderResourceView controllerDepth=null)
        {
            if(!frames.TryGetValue(id,out var frame))
            {
                var canvas=new OverlayCanvas("Window frame "+id,1600,1100,1,false,target.Device,true);
                frames[id]=frame=new Cache {Canvas=canvas,Texture=new ShaderResourceView(target.Device,canvas.Texture)};
            }
            string key=s.Width+"|"+s.Height+"|"+s.Hover+"|"+s.BarOffset+"|"+s.Zoom;
            if(frame.Key!=key)
            {
                Paint(frame.Canvas,s); frame.Canvas.Upload(); target.Device.ImmediateContext.GenerateMips(frame.Texture); frame.Key=key;
            }
            var sprite=PhysicalSurface.Quad(frame.Texture,s.Pose,
                new VRageMath.RectangleF(-s.Width/2-.03f,s.Height/2+.03f,s.Width+.06f,s.Height+.115f+s.BarOffset),
                new Vector4(0,0,1,1),Vector4.One,view,projection);
            NativeSprites.Draw(target,new[] {sprite},controllerDepth,handDepth);
        }
    }
}
