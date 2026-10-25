using System;
using System.Collections.Generic;
using Sandbox.Game.World;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    // Mod HUD under an open menu goes into the menu image behind the GUI, where the desktop draws it.
    internal static class MenuHudLayer
    {
        private static readonly List<NativeSprite> building=new List<NativeSprite>();
        private static volatile NativeSprite[] published;
        private static readonly Vector4[] corners=new Vector4[4];
        private static MatrixD world,inverse;
        private static readonly RenderRecovery recovery=new RenderRecovery("Menu mod HUD");
        internal static bool Active => Main.VrActive && Main.MenuOpen && Main.WorldAvailable;

        // Game thread, inside a known mod HUD draw; false when the quad is not on the camera plane.
        internal static bool Add(MyBillboard billboard)
        {
            if(!ToScreen(billboard,out var sprite)) return false;
            building.Add(sprite);
            return true;
        }
        // A camera-facing quad as a sprite in clip coordinates of the desktop frame.
        internal static bool ToScreen(MyBillboard billboard,out NativeSprite sprite)
        {
            sprite=default(NativeSprite);
            var camera=MySector.MainCamera;
            if(camera==null || billboard is MyTriangleBillboard) return false;
            if(camera.WorldMatrix!=world) { world=camera.WorldMatrix; inverse=MatrixD.Invert(world); }
            double tan=Math.Tan(camera.FovWithZoom/2),aspect=Aspect;
            var points=new[] {billboard.Position0,billboard.Position1,billboard.Position2,billboard.Position3};
            for(int i=0;i<4;i++)
            {
                if(!Screen(Vector3D.Transform(points[i],inverse),tan,aspect,out corners[i])) return false;
            }
            sprite=BuildOrientationHud.Sprite(billboard,corners);
            return true;
        }
        internal static double Aspect { get { var viewport=MySector.MainCamera?.Viewport; return viewport==null ? 0 : viewport.Value.Width/Math.Max(1,viewport.Value.Height); } }
        // Camera-space point to clip coordinates of the desktop frame the mod laid out for.
        internal static bool Screen(Vector3D local,double tan,double aspect,out Vector4 clip)
        {
            clip=default(Vector4);
            if(!local.IsValid() || local.Z>-.001 || !(tan>0) || !(aspect>0)) return false;
            clip=new Vector4((float)(local.X/-local.Z/(tan*aspect)),(float)(local.Y/-local.Z/tan),0,1);
            return true;
        }
        // Game thread, once per update: the previous frame's draws become the layer.
        internal static void Publish()
        {
            published=Active && building.Count>0 ? building.ToArray() : null;
            building.Clear();
        }
        // Render thread, on the cleared menu image before native sprites.
        internal static void Draw(SharpDX.Direct3D11.Texture2D target)
        {
            var sprites=published;
            if(sprites==null || recovery.Failed) return;
            try { NativeSprites.Draw(target,sprites); }
            catch(Exception ex) { recovery.Fail(ex,"Mod HUD on menus disabled"); }
        }
        // Game thread, as GUI sprites in the screen's draw order: Build Info's toolbar list at its desktop size, shrunk to fit, on the area's bottom left.
        internal static void DrawToolbarList(RectangleF area)
        {
            var view=BuildInfoPanel.Toolbar.Current;
            var full=Sandbox.Graphics.MyGuiManager.GetFullscreenRectangle();
            if(!BuildInfoPanel.Fresh(view,DateTime.UtcNow) || full.Width<=0 || full.Height<=0) return;
            double height=BuildInfoPanel.Toolbar.ScreenShare(view),width=height*view.Width/view.Height*full.Height/full.Width;
            double fit=Math.Min(1,Math.Min(area.Width/width,area.Height/height));
            foreach(var s in Fit(view.Sprites,area.X,area.Y+area.Height-height*fit,width*fit,height*fit))
            {
                if(s.Path==null) continue;
                float left=full.X+(s.TopLeft.X+1)/2*full.Width,top=full.Y+(1-s.TopLeft.Y)/2*full.Height;
                float right=full.X+(s.BottomRight.X+1)/2*full.Width,bottom=full.Y+(1-s.BottomRight.Y)/2*full.Height;
                MyRenderProxy.DrawSpriteAtlas(s.Path,new Vector2((left+right)/2,(top+bottom)/2),new Vector2(s.UV.X,s.UV.Y),new Vector2(s.UV.Z,s.UV.W),Vector2.UnitX,Vector2.One,
                    GuiColor(s.Tint),new Vector2((right-left)/2,(bottom-top)/2),true);
            }
        }
        // Billboard tints are linear and straight; GUI sprites blend premultiplied gamma colours.
        internal static Color GuiColor(Vector4 linear)
        {
            float alpha=MathHelper.Clamp(linear.W,0,1);
            var gamma=Vector4.Clamp(linear,Vector4.Zero,Vector4.One).ToSRGB();
            return new Color(gamma.X*alpha,gamma.Y*alpha,gamma.Z*alpha,alpha);
        }
        // Panel clip coordinates into a screen rectangle given as fractions from the top left.
        internal static NativeSprite[] Fit(NativeSprite[] sprites,double left,double top,double width,double height)
        {
            Vector4 Map(Vector4 p) => new Vector4((float)((left+(p.X+1)/2*width)*2-1),(float)(1-(top+(1-p.Y)/2*height)*2),0,1);
            var result=new NativeSprite[sprites.Length];
            for(int i=0;i<sprites.Length;i++)
            {
                var s=sprites[i];
                s.TopLeft=Map(s.TopLeft); s.TopRight=Map(s.TopRight); s.BottomLeft=Map(s.BottomLeft); s.BottomRight=Map(s.BottomRight);
                result[i]=s;
            }
            return result;
        }
        internal static void Reset() { building.Clear(); published=null; }
#if SEVR_DIAGNOSTICS
        internal static int Count => published?.Length ?? 0;
#endif
    }
}
