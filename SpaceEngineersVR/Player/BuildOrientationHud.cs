using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    internal static class BuildOrientationHud
    {
        internal sealed class View
        {
            public NativeSprite[] Sprites;
            public DateTime Captured;
        }
        // Native rotation hints submit camera-relative billboards into their own
        // desktop viewport. Capture only that call, before the pooled objects reset.
        internal sealed class Capture : IDisposable
        {
            private readonly List<Tuple<NativeSprite,float>> sprites=new List<Tuple<NativeSprite,float>>();
            private Matrix view=Matrix.Identity;
            private readonly bool visible;
            internal Capture(bool visible) { this.visible=visible; }
            internal void Projection(MyBillboardViewProjection projection) { view=projection.ViewAtZero; }
            internal void Add(MyBillboard billboard)
            {
                if(!visible) return;
                var material=MyTransparentMaterials.GetMaterial(billboard.Material);
                var projection=Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4,1,.1f,100);
                var points=new[] { billboard.Position0,billboard.Position1,billboard.Position2,billboard.Position3 }
                    .Select(p=>(Vector3)Vector3D.Transform(p,view)-new Vector3(1,.6f,0)).ToArray();
                var clip=points.Select(p=>Vector4.Transform(new Vector4(p,1),projection)).ToArray();
                if(clip.Any(p=>!new Vector3(p.X,p.Y,p.Z).IsValid() || !p.W.IsValid() || p.W<=0)) return;
                var uv=material.UVOffset+billboard.UVOffset;
                var size=material.UVSize*billboard.UVSize;
                var sprite=new NativeSprite(material.Texture,default(RectangleF),billboard.Color) {
                    EncodeSrgb=true,Projected=true,TopLeft=clip[0],TopRight=clip[1],BottomLeft=clip[3],BottomRight=clip[2],UV=new Vector4(uv.X,uv.Y,size.X,size.Y) };
                sprites.Add(Tuple.Create(sprite,points.Average(p=>p.Z)));
            }
            internal View Complete()
            {
                var result=visible && sprites.Count>0 ? new View {
                    Sprites=sprites.OrderBy(s=>s.Item2).Select(s=>s.Item1).ToArray(),Captured=DateTime.UtcNow } : null;
                current=result;
                return result;
            }
            public void Dispose() { if(ReferenceEquals(capturing,this)) capturing=null; }
        }
        [ThreadStatic] private static Capture capturing;
        internal static Capture Capturing => capturing;
        internal static Capture Begin(bool draw) => capturing=new Capture(draw);
        internal static readonly Matrix Mount=Matrix.CreateTranslation(-.49f,-.57f,-1.5f);
        internal const float Width=.32f;
        private static volatile View current;
        private static OverlayCanvas canvas;
        private static DateTime nextDraw;
        private static bool failed;
        internal static bool Visible(View view,DateTime now,bool hud,InputMode mode) => view!=null && hud &&
            (mode==InputMode.Building || mode==InputMode.Clipboard) && now>=view.Captured && (now-view.Captured).TotalSeconds<.25;
        internal static void Paint(OverlayCanvas target,View view)
        {
            target.Clear(System.Drawing.Color.Transparent);
            foreach(var sprite in view.Sprites) target.Sprite(sprite);
        }
        public static void Draw()
        {
            if(failed) return;
            try
            {
                var view=current; var now=DateTime.UtcNow;
                if(!Visible(view,now,HelmetHud.Visible && !Main.MenuOpen,InputRouter.Mode) || !Player.Headset.renderPose.isTracked)
                { Hide(); nextDraw=DateTime.MinValue; return; }
                if(now<nextDraw) return;
                nextDraw=now.AddMilliseconds(1000.0/30);
                if(canvas==null) { canvas=new OverlayCanvas("Build orientation",384,384,Width); canvas.Position(Mount,true); }
                Paint(canvas,view); canvas.Upload();
            }
            catch(Exception ex) { failed=true; Logger.Warning(ex,"Build orientation HUD disabled"); Hide(); }
        }
        public static void Hide() => canvas?.Hide();
        public static void Reset() { current=null; nextDraw=DateTime.MinValue; }
    }
}
