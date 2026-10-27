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
                var projection=Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4,1,.1f,100);
                var points=new[] { billboard.Position0,billboard.Position1,billboard.Position2,billboard.Position3 }
                    .Select(p=>(Vector3)Vector3D.Transform(p,view)-new Vector3(1,.6f,0)).ToArray();
                var clip=points.Select(p=>Vector4.Transform(new Vector4(p,1),projection)).ToArray();
                if(clip.Any(p=>!new Vector3(p.X,p.Y,p.Z).IsValid() || !p.W.IsValid() || p.W<=0)) return;
                sprites.Add(Tuple.Create(Sprite(billboard,clip),points.Average(p=>p.Z)));
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
        // Clip corners in billboard vertex order.
        internal static NativeSprite Sprite(MyBillboard billboard,Vector4[] clip)
        {
            var material=MyTransparentMaterials.GetMaterial(billboard.Material);
            var uv=material.UVOffset+billboard.UVOffset;
            var size=material.UVSize*billboard.UVSize;
            return new NativeSprite(material.Texture,default(RectangleF),billboard.Color) {
                EncodeSrgb=true,Projected=true,TopLeft=clip[0],TopRight=clip[1],BottomLeft=clip[3],BottomRight=clip[2],UV=new Vector4(uv.X,uv.Y,size.X,size.Y) };
        }
        [ThreadStatic] private static Capture capturing;
        internal static Capture Capturing => capturing;
        internal static Capture Begin(bool draw) => capturing=new Capture(draw);
        internal static readonly Matrix Mount=Matrix.CreateTranslation(.025f,-.5f,-1.5f);
        internal const float Width=.32f;
        private static volatile View current;
        private static OverlayCanvas canvas;
        private static DateTime nextDraw;
        private static bool failed;
        internal static bool Visible(View view,DateTime now,bool hud,bool rotating) => view!=null && hud && rotating &&
            now>=view.Captured && (now-view.Captured).TotalSeconds<.25;
        internal static void Paint(OverlayCanvas target,View view)
        {
            target.Clear(System.Drawing.Color.Transparent);
            foreach(var sprite in view.Sprites) target.Sprite(sprite);
        }
        // Clear terrain needs a drill and rotate mode a placement preview, so they never share the panel.
        internal static void PaintClearTerrain(OverlayCanvas target)
        {
            target.Clear(System.Drawing.Color.Transparent);
            target.Icon(NativeSprites.Hud("DrillIcon"),132,120,120,60,new Vector4(0,0,1,1),System.Drawing.Color.White);
            using(var font=new System.Drawing.Font("Segoe UI",34,System.Drawing.FontStyle.Regular,System.Drawing.GraphicsUnit.Pixel))
            using(var centered=new System.Drawing.StringFormat { Alignment=System.Drawing.StringAlignment.Center })
                target.Graphics.DrawString("Clear terrain",font,System.Drawing.Brushes.LightCyan,new System.Drawing.RectangleF(0,196,384,48),centered);
        }
        public static void Draw()
        {
            if(failed) return;
            try
            {
                var view=current; var now=DateTime.UtcNow;
                bool hud=HelmetHud.Visible && !Main.MenuOpen && Player.Headset.renderPose.isTracked;
                bool rotation=Visible(view,now,hud,PlacementControls.Adjusting),clear=hud && !rotation && ClearTerrain.Shown;
                if(!rotation && !clear) { Hide(); nextDraw=DateTime.MinValue; return; }
                if(canvas==null) { canvas=new OverlayCanvas("Build orientation",384,384,Width); canvas.Position(Mount,true); }
                if(clear) { if(!showingClear) { PaintClearTerrain(canvas); canvas.Upload(); showingClear=true; } }
                else if(now>=nextDraw)
                {
                    nextDraw=now.AddMilliseconds(1000.0/30);
                    Paint(canvas,view); canvas.Upload(); showingClear=false;
                }
                float alpha=HelmetHud.HudAlpha; if(alpha!=shownAlpha) { canvas.Show(alpha); shownAlpha=alpha; }
            }
            catch(Exception ex) { failed=true; Logger.Warning(ex,"Build orientation HUD disabled"); Hide(); }
        }
        private static float shownAlpha=-1;
        private static bool showingClear;
        public static void Hide() { canvas?.Hide(); shownAlpha=-1; showingClear=false; }
        public static void Reset() { current=null; nextDraw=DateTime.MinValue; }
    }
}
