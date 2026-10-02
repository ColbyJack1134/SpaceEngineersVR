using System;
using System.Drawing;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Wrappers;
using VRageMath;
using Color=System.Drawing.Color;

namespace SpaceEngineersVR.Player
{
    internal static class RemoteFeed
    {
        private static BorrowedRtvTexture feed;
        private static ShaderResourceView texture,frameTexture;
        private static OverlayCanvas frame;
        private static string painted;
        private static long source;
        internal static readonly Vector2I Resolution=new Vector2I(1920,1080);
        public static void Render(RemoteView.View view)
        {
            texture?.Dispose(); texture=null; feed?.Release(); feed=null; source=0;
            if(view==null) return;
            feed=MyManagers.RwTexturesPool.BorrowRtv("SEVR.Remote",Resolution.X,Resolution.Y,Format.R8G8B8A8_UNorm_SRgb);
            object ambient=null;
            long started=FeatureTiming.Start();
            GpuTiming.Begin(GpuTiming.Area.RemoteFeed);
            try
            {
                using(var resources=MyRender11.Resolution==Resolution ? null:new EyeResolution.Scene(Resolution,true))
                using(var exposure=new RemoteExposure())
                using(var isolated=new RemoteScene()) MyRender11.DrawGameScene(feed,out ambient);
                RemoteHud.Composite((Texture2D)feed.GetResource(),view);
                texture=new ShaderResourceView(MyRender11.DeviceInstance,(Texture2D)feed.GetResource());
                source=view.Source;
            }
            finally
            {
                if(ambient!=null) new BorrowedRtvTexture(ambient).Release();
                FeatureTiming.End(FeatureTiming.Area.RemoteFeed,started);
                GpuTiming.End(GpuTiming.Area.RemoteFeed);
            }
        }
        public static void Reset()
        {
            texture?.Dispose(); texture=null; feed?.Release(); feed=null; source=0;
            frameTexture?.Dispose(); frameTexture=null; frame?.Dispose(); frame=null; painted=null; RemoteExposure.Reset();
        }
        internal static void Paint(OverlayCanvas canvas,RemoteView.View view)
        {
            FloatingMenu.Paint(canvas,new FloatingMenu.Snapshot { Width=view.Width,Height=view.Height,Hover=view.Hover,BarOffset=.035f });
            var g=canvas.Graphics; var state=g.Save();
            try
            {
                float width=view.Width+.06f,height=view.Height+.15f;
                g.ScaleTransform(1600/width,1100/height); g.TranslateTransform(width/2,.03f+view.Height/2);
                using(var font=new Font("Segoe UI",.045f,FontStyle.Regular,GraphicsUnit.Pixel))
                using(var centered=new StringFormat { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center })
                {
                    g.DrawString("−",font,view.Hover==3 ? Brushes.Cyan:Brushes.White,RemoteView.ZoomX(view.Width,false),view.Height/2+.035f,centered);
                    g.DrawString("+",font,view.Hover==4 ? Brushes.Cyan:Brushes.White,RemoteView.ZoomX(view.Width,true),view.Height/2+.035f,centered);
                }

            }
            finally { g.Restore(state); }
        }
        public static void Draw(Texture2D target,RemoteView.View remote,MatrixD view,MatrixD projection)
        {
            if(remote==null || texture==null || source!=remote.Source) return;
            if(frame==null)
            {
                frame=new OverlayCanvas("Remote camera frame",1600,1100,1,false,target.Device);
                frameTexture=new ShaderResourceView(target.Device,frame.Texture);
            }
            string key=remote.Width+"|"+remote.Height+"|"+remote.Hover;
            if(key!=painted) { Paint(frame,remote); frame.Upload(); painted=key; }
            var picture=PhysicalSurface.Quad(texture,remote.Pose,new VRageMath.RectangleF(-remote.Width/2,remote.Height/2,remote.Width,remote.Height),new Vector4(0,0,1,1),Vector4.One,view,projection);
            var border=PhysicalSurface.Quad(frameTexture,remote.Pose,new VRageMath.RectangleF(-(remote.Width+.06f)/2,remote.Height/2+.03f,remote.Width+.06f,remote.Height+.15f),new Vector4(0,0,1,1),Vector4.One,view,projection,.001f);
            NativeSprites.Draw(target,new[] {picture,border},handDepth:NativeHandLayer.Depth);
            if(remote.RayStart.HasValue && remote.RayEnd.HasValue)
            {
                var delta=remote.RayEnd.Value-remote.RayStart.Value;
                if(delta.LengthSquared()>.0001)
                    PhysicalSurface.Draw(target,new[] {new SurfaceView {Id="CameraRay",Style=SurfaceStyle.Pointer,Width=.002f,Height=(float)delta.Length(),
                        Pose=MatrixD.CreateWorld((remote.RayStart.Value+remote.RayEnd.Value)*.5,Vector3D.Normalize(delta),remote.Pose.Up)}},view,projection,PhysicalSurface.SceneDepth());
            }
        }
    }
}
