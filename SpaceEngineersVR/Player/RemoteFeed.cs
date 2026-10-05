using System;
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
            if(view==null) { image.Dispose(); source=0; return; }
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
                using(var isolated=new RemoteScene()) MyRender11.DrawGameScene(feed,out ambient);
                var rendered=(Texture2D)feed.GetResource();
                RemoteHud.Composite(rendered,view);
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
            image.Dispose(); source=0; skip=false;
            WindowFrame.Reset("Remote"); RemoteExposure.Reset();
        }
        internal static void Paint(OverlayCanvas canvas,RemoteView.View view) => WindowFrame.Paint(canvas,
            new WindowFrame.Snapshot {Width=view.Width,Height=view.Height,Hover=view.Hover,Zoom=true});
        public static void Draw(Texture2D target,RemoteView.View remote,MatrixD view,MatrixD projection)
        {
            if(remote==null || image.View==null || source!=remote.Source) return;
            DrawPanel(target,image.View,remote,view,projection,NativeHandLayer.Depth);
        }
        internal static void DrawPanel(Texture2D target,ShaderResourceView image,RemoteView.View remote,MatrixD view,MatrixD projection,ShaderResourceView hands=null)
        {
            var picture=PhysicalSurface.Quad(image,remote.Pose,new VRageMath.RectangleF(-remote.Width/2,remote.Height/2,remote.Width,remote.Height),new Vector4(0,0,1,1),Vector4.One,view,projection);
            NativeSprites.Draw(target,new[] {picture},handDepth:hands);
            WindowFrame.Draw(target,"Remote",new WindowFrame.Snapshot {Pose=(Matrix)remote.Pose,Width=remote.Width,Height=remote.Height,Hover=remote.Hover,Zoom=true},view,projection,hands);
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
