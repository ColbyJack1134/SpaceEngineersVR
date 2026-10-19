using System;
using System.Collections.Generic;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class RemoteFeed
    {
        private static readonly TextureCopy image=new TextureCopy();
        private static long source;
        private static bool skip;
        internal static Vector2I Resolution => Size(Common.Config?.RemoteFeedScale ?? 5f/6);
        // A width in 16-pixel steps keeps the height an exact 16:9 integer.
        internal static Vector2I Size(float scale)
        {
            int width=(int)Math.Round(1920*scale/16)*16;
            return new Vector2I(width,width*9/16);
        }
        public static void Render(RemoteView.View view)
        {
            if(view==null) { image.Dispose(); source=0; FeedCamera.Reset(); FeedShadows.Release(); return; }
            // Refresh every second headset frame; a new camera renders immediately.
            if(source==view.Source && (skip=!skip)) return;
            source=0;
            var size=Resolution;
            var feed=MyManagers.RwTexturesPool.BorrowRtv("SEVR.Remote",size.X,size.Y,Format.R8G8B8A8_UNorm_SRgb);
            object ambient=null;
            long started=FeatureTiming.Start();
            GpuTiming.Begin(GpuTiming.Area.RemoteFeed);
            try
            {
                using(var resources=MyRender11.Resolution==size ? null:new EyeResolution.Scene(size,true))
                using(var exposure=new RemoteExposure())
                using(var isolated=new RemoteScene(view))
                {
                    MyRender11.DrawGameScene(feed,out ambient);
                    if(WeaponCoreBridge.ApiReady) FeedCamera.Capture(view.Source);
                }
                var rendered=(Texture2D)feed.GetResource();
                RemoteHud.Composite(rendered,view);
                RemoteCombat.DrawAmmo(rendered,view);
                image.Store(rendered);
                source=view.Source;
            }
            finally
            {
                if(ambient!=null) new BorrowedRtvTexture(ambient).Release();
                feed.Release();
                FeatureTiming.End(FeatureTiming.Area.RemoteFeed,started);
                GpuTiming.End(GpuTiming.Area.RemoteFeed);
            }
        }
        public static void Reset()
        {
            image.Dispose(); source=0; skip=false; FeedShadows.Release();
            WindowFrame.Reset("Remote"); RemoteExposure.Reset();
        }
        internal static void Paint(OverlayCanvas canvas,RemoteView.View view) => WindowFrame.Paint(canvas,
            new WindowFrame.Snapshot {Width=view.Width,Height=view.Height,Hover=view.Hover,Zoom=true,SignalMode=view.SignalMode});
        // Red corner brackets on the image around the target an aimed grip release would lock, like its world marker.
        private const float BracketWidth=.004f;
        private static readonly Vector4 pointedColor=new Vector4(1,0,0,1);
        private static void AddPointed(List<NativeSprite> sprites,RemoteView.View remote,MatrixD view,MatrixD projection)
        {
            if(!remote.Pointed.HasValue) return;
            var sphere=remote.Pointed.Value;
            if(!FeedCamera.TryProject(remote.Source,sphere.Center,sphere.Radius,out var p)) return;
            float w=BracketWidth;
            // Inset by half a bar so the brackets stay on the image at its edges.
            float left=Math.Max(-remote.Width/2+p.X*remote.Width-p.Z*remote.Width,-remote.Width/2+w/2),
                right=Math.Min(-remote.Width/2+p.X*remote.Width+p.Z*remote.Width,remote.Width/2-w/2),
                bottom=Math.Max(remote.Height/2-p.Y*remote.Height-p.W*remote.Height,-remote.Height/2+w/2),
                top=Math.Min(remote.Height/2-p.Y*remote.Height+p.W*remote.Height,remote.Height/2-w/2);
            if(left>=right || bottom>=top) return;
            float cx=(left+right)/2,cy=(bottom+top)/2,hx=(right-left)/2,hy=(top-bottom)/2;
            float arm=Math.Min(hx,hy)*.45f;
            for(int corner=0;corner<4;corner++)
            {
                float sx=corner%2==0 ? -1:1,sy=corner<2 ? 1:-1,x=cx+sx*hx,y=cy+sy*hy;
                sprites.Add(Bar(remote.Pose,new VRageMath.RectangleF(Math.Min(x,x-sx*arm)-w/2,y+w/2,arm+w,w),view,projection));
                sprites.Add(Bar(remote.Pose,new VRageMath.RectangleF(x-w/2,Math.Max(y,y-sy*arm)+w/2,w,arm+w),view,projection));
            }
        }
        private static NativeSprite Bar(MatrixD pose,VRageMath.RectangleF bounds,MatrixD view,MatrixD projection)
        {
            var bar=PhysicalSurface.Quad(null,pose,bounds,new Vector4(0,0,1,1),pointedColor,view,projection);
            bar.Path=@"Textures\GUI\Blank.dds";
            return bar;
        }
        public static void Draw(Texture2D target,RemoteView.View remote,MatrixD view,MatrixD projection)
        {
            if(remote==null || image.View==null || source!=remote.Source) return;
            DrawPanel(target,image.View,remote,view,projection,NativeHandLayer.Depth);
        }
        internal static void DrawPanel(Texture2D target,ShaderResourceView image,RemoteView.View remote,MatrixD view,MatrixD projection,ShaderResourceView hands=null)
        {
            var picture=PhysicalSurface.Quad(image,remote.Pose,new VRageMath.RectangleF(-remote.Width/2,remote.Height/2,remote.Width,remote.Height),new Vector4(0,0,1,1),Vector4.One,view,projection);
            var sprites=new List<NativeSprite> {picture};
            AddPointed(sprites,remote,view,projection);
            NativeSprites.Draw(target,sprites,handDepth:hands);
            WindowFrame.Draw(target,"Remote",new WindowFrame.Snapshot {Pose=(Matrix)remote.Pose,Width=remote.Width,Height=remote.Height,Hover=remote.Hover,Zoom=true,SignalMode=remote.SignalMode},view,projection,hands);
            if(remote.RayStart.HasValue && remote.RayEnd.HasValue)
            {
                var delta=remote.RayEnd.Value-remote.RayStart.Value;
                if(delta.LengthSquared()>.0001)
                    PhysicalSurface.Draw(target,new[] {new SurfaceView {Id="CameraRay",Style=SurfaceStyle.Pointer,Width=.002f,Height=(float)delta.Length(),LeftHand=remote.LeftHand,
                        Pose=MatrixD.CreateWorld((remote.RayStart.Value+remote.RayEnd.Value)*.5,Vector3D.Normalize(delta),remote.Pose.Up)}},view,projection,null);
            }
        }
    }
}
