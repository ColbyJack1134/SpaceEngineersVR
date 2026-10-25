using System;
using System.Collections.Generic;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    // A Build Info box laid out in the axes of Text HUD API's own quad plane, so no camera matrix is needed.
    internal sealed class BuildInfoPanel
    {
        // The toolbar list below the 0.52 m wheel is enlarged to the wheel's label size.
        internal static readonly BuildInfoPanel Text=new BuildInfoPanel(1),Toolbar=new BuildInfoPanel(1.6f);
        // Discards what Build Info draws inside its scope.
        internal static readonly BuildInfoPanel Hidden=new BuildInfoPanel(1);
        private readonly float scale;
        private BuildInfoPanel(float scale) { this.scale=scale; }
        internal sealed class View
        {
            public NativeSprite[] Sprites;
            public float Width,Height;
            public int Hash;
            public DateTime Captured;
        }
        internal struct Quad
        {
            public NativeSprite Sprite;
            public Vector2D TopLeft,TopRight,BottomRight,BottomLeft;
        }
        // Desktop proportions: the native block info panel is 0.3 screen heights wide (324 GUI pixels at 1080p).
        internal static readonly float MetresPerScreenHeight=BlockInspection.Width/(NativeBlockInfo.Pixels(0).X/1080f);
        internal const float Gap=.006f;
        // Twice the card's density: small mod font text stays legible in the headset.
        internal const float PixelsPerMetre=2*NativeBlockInfo.TextureWidth/BlockInspection.Width;
        internal const int MaxTexture=4096;
        internal const double Stale=.25;
        private static readonly Vector4[] unit={new Vector4(-1,1,0,1),new Vector4(1,1,0,1),new Vector4(1,-1,0,1),new Vector4(-1,-1,0,1)};

        internal sealed class Capture : IDisposable
        {
            private readonly BuildInfoPanel panel;
            private readonly bool keep;
            private readonly double halfHeight;
            private readonly List<Quad> quads=new List<Quad>();
            private Vector3D origin,right,down;
            private bool framed;
            // halfHeight: Text HUD API's localscale, half the screen height on its plane.
            internal Capture(BuildInfoPanel panel,bool keep,double halfHeight) { this.panel=panel; this.keep=keep; this.halfHeight=halfHeight; }
            internal void Add(MyBillboard billboard)
            {
                if(!keep || billboard==null || billboard is MyTriangleBillboard || billboard.CustomViewProjection!=-1) return;
                Add(default(NativeSprite),billboard.Position0,billboard.Position1,billboard.Position2,billboard.Position3,billboard);
            }
            // Billboard vertices run top-left, top-right, bottom-right, bottom-left.
            internal void Add(NativeSprite sprite,Vector3D topLeft,Vector3D topRight,Vector3D bottomRight,Vector3D bottomLeft,MyBillboard billboard=null)
            {
                if(!framed)
                {
                    right=topRight-topLeft; down=bottomLeft-topLeft;
                    if(!(right.LengthSquared()>1e-14) || !(down.LengthSquared()>1e-14)) return;
                    origin=topLeft; right.Normalize(); down.Normalize(); framed=true;
                }
                quads.Add(new Quad { Sprite=billboard!=null ? BuildOrientationHud.Sprite(billboard,unit) : sprite,
                    TopLeft=Local(topLeft),TopRight=Local(topRight),BottomRight=Local(bottomRight),BottomLeft=Local(bottomLeft) });
            }
            private Vector2D Local(Vector3D point) { var offset=point-origin; return new Vector2D(offset.Dot(right),offset.Dot(down)); }
            internal View Complete()
            {
                var view=keep ? Build(quads,halfHeight,DateTime.UtcNow,panel.scale) : null;
                var current=panel.current;
                bool changed=view!=null && (current==null || current.Hash!=view.Hash);
                if(view!=null && !changed) view.Sprites=current.Sprites;
                // The render thread reads the revision first, so the view it then reads is at least as new.
                panel.current=view;
                if(changed) panel.revision++;
                return view;
            }
            public void Dispose() { if(ReferenceEquals(capturing,this)) capturing=null; }
        }
        [ThreadStatic] private static Capture capturing;
        private volatile View current;
        private volatile int revision;
        internal static Capture Capturing => capturing;
        internal View Current => current;
        internal int Revision => revision;
        internal Capture Begin(bool keep,double halfHeight) => capturing=new Capture(this,keep,halfHeight);

        // Null when nothing usable was drawn. Plane metres scale to the card by their share of the screen height.
        internal static View Build(List<Quad> quads,double halfHeight,DateTime now,float enlarge=1)
        {
            if(quads.Count==0 || !(halfHeight>0)) return null;
            var min=new Vector2D(double.MaxValue); var max=new Vector2D(double.MinValue);
            foreach(var q in quads)
            {
                min=Vector2D.Min(Vector2D.Min(min,q.TopLeft),Vector2D.Min(Vector2D.Min(q.TopRight,q.BottomRight),q.BottomLeft));
                max=Vector2D.Max(Vector2D.Max(max,q.TopLeft),Vector2D.Max(Vector2D.Max(q.TopRight,q.BottomRight),q.BottomLeft));
            }
            var size=max-min;
            if(!(size.X>1e-6 && size.X<1) || !(size.Y>1e-6 && size.Y<1)) return null;
            double scale=MetresPerScreenHeight*enlarge/(2*halfHeight);
            var sprites=new NativeSprite[quads.Count];
            int hash=quads.Count;
            unchecked
            {
                for(int i=0;i<quads.Count;i++)
                {
                    var q=quads[i]; var s=q.Sprite;
                    s.TopLeft=Clip(q.TopLeft,min,size); s.TopRight=Clip(q.TopRight,min,size);
                    s.BottomRight=Clip(q.BottomRight,min,size); s.BottomLeft=Clip(q.BottomLeft,min,size);
                    sprites[i]=s;
                    hash=hash*31+(s.Path?.GetHashCode() ?? 0);
                    hash=hash*31+s.Tint.GetHashCode();
                    hash=hash*31+s.UV.GetHashCode();
                    hash=hash*31+Quantize(q.TopLeft-min).GetHashCode();
                    hash=hash*31+Quantize(q.BottomRight-min).GetHashCode();
                }
                hash=hash*31+Quantize(size).GetHashCode();
            }
            return new View { Sprites=sprites,Width=(float)(size.X*scale),Height=(float)(size.Y*scale),Hash=hash,Captured=now };
        }
        // Micrometre steps on the 0.1 m plane hide float noise from camera motion.
        private static Vector2I Quantize(Vector2D v) => new Vector2I((int)Math.Round(v.X*1e6),(int)Math.Round(v.Y*1e6));
        internal static Vector4 Clip(Vector2D p,Vector2D min,Vector2D size) =>
            new Vector4((float)((p.X-min.X)/size.X*2-1),(float)(1-(p.Y-min.Y)/size.Y*2),0,1);

        // Share of the screen height the box had on the desktop.
        internal double ScreenShare(View view) => view.Height/(MetresPerScreenHeight*scale);
        internal static bool Fresh(View view,DateTime now) => view!=null && now>=view.Captured && (now-view.Captured).TotalSeconds<Stale;
        // Top-aligned with the card, on the card's far side from the hand.
        internal static MatrixD Beside(MatrixD card,float cardWidth,float cardHeight,float width,float height,int side)
        {
            var pose=card;
            pose.Translation=card.Translation+card.Right*side*(cardWidth/2+Gap+width/2)+card.Up*(cardHeight/2-height/2);
            return pose;
        }
        internal SurfaceView Surface(SurfaceView card,int side)
        {
            var view=current;
            if(card==null || !Fresh(view,DateTime.UtcNow)) return null;
            return new SurfaceView { Id="Build Info",Style=SurfaceStyle.BuildInfo,Width=view.Width,Height=view.Height,
                Pose=Beside(card.Pose,card.Width,card.Height,view.Width,view.Height,side) };
        }
        internal static Vector2I TextureSize(float width,float height)
        {
            double scale=Math.Min(PixelsPerMetre,MaxTexture/Math.Max(width,height));
            int Pixels(float metres) => (int)MathHelper.Clamp(Math.Ceiling(metres*scale-1e-3),1,MaxTexture);
            return new Vector2I(Pixels(width),Pixels(height));
        }
        internal void Paint(OverlayCanvas canvas) => Paint(canvas,current);
        private static void Paint(OverlayCanvas canvas,View view)
        {
            canvas.ClearTexture();
            if(view!=null) NativeSprites.Draw(canvas.Texture,view.Sprites);
        }
        internal void Reset() { current=null; }

        // Render thread: the panel as a scene quad at pose (its centre), repainted when the capture or a native image changes.
        private OverlayCanvas canvas;
        private SharpDX.Direct3D11.ShaderResourceView texture;
        private int painted=-1,paintedImages=-1;
        internal NativeSprite WorldQuad(SharpDX.Direct3D11.Device device,View view,MatrixD pose,MatrixD viewMatrix,MatrixD projection)
        {
            // Revision first, matching the publish order in Capture.Complete.
            int shown=revision;
            view=current ?? view;
            var size=TextureSize(view.Width,view.Height);
            if(canvas!=null && (canvas.Width!=size.X || canvas.Height!=size.Y)) { texture.Dispose(); canvas.Dispose(); canvas=null; }
            if(canvas==null)
            {
                canvas=new OverlayCanvas("Build Info panel",size.X,size.Y,1,false,device,mipMaps:true);
                texture=new SharpDX.Direct3D11.ShaderResourceView(device,canvas.Texture); painted=-1;
            }
            if(painted!=shown || paintedImages!=NativeSprites.Revision)
            {
                Paint(canvas,view); device.ImmediateContext.GenerateMips(texture);
                painted=shown; paintedImages=NativeSprites.Revision;
            }
            var sprite=PhysicalSurface.Quad(texture,pose,new RectangleF(-view.Width/2,view.Height/2,view.Width,view.Height),new Vector4(0,0,1,1),Vector4.One,viewMatrix,projection);
            sprite.IgnoreSceneDepth=true; sprite.Sharpen=true;
            return sprite;
        }
    }
}
