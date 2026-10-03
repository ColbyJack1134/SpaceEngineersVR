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
        private static readonly TextureCopy image=new TextureCopy();
        private static ShaderResourceView frameTexture;
        private static OverlayCanvas frame;
        private static string painted;
        private static long source;
        private static bool skip;
        internal static readonly Vector2I Resolution=new Vector2I(1600,900);
        public static void Render(RemoteView.View view)
        {
            if(view==null) { image.Dispose(); source=0; return; }
            // Refresh every second headset frame; a new camera renders immediately.
            if(source==view.Source && (skip=!skip)) return;
            source=0;
            var feed=MyManagers.RwTexturesPool.BorrowRtv("SEVR.Remote",Resolution.X,Resolution.Y,Format.R8G8B8A8_UNorm_SRgb);
            object ambient=null;
            long started=FeatureTiming.Start();
            GpuTiming.Begin(GpuTiming.Area.RemoteFeed);
            try
            {
                using(var resources=MyRender11.Resolution==Resolution ? null:new EyeResolution.Scene(Resolution,true))
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
            if(remote==null || image.View==null || source!=remote.Source) return;
            DrawPanel(target,image.View,remote,view,projection,NativeHandLayer.Depth);
        }
        internal static void DrawPanel(Texture2D target,ShaderResourceView image,RemoteView.View remote,MatrixD view,MatrixD projection,ShaderResourceView hands=null)
        {
            if(frame==null)
            {
                frame=new OverlayCanvas("Remote camera frame",1600,1100,1,false,target.Device);
                frameTexture=new ShaderResourceView(target.Device,frame.Texture);
            }
            string key=remote.Width+"|"+remote.Height+"|"+remote.Hover;
            if(key!=painted) { Paint(frame,remote); frame.Upload(); painted=key; }
            var picture=PhysicalSurface.Quad(image,remote.Pose,new VRageMath.RectangleF(-remote.Width/2,remote.Height/2,remote.Width,remote.Height),new Vector4(0,0,1,1),Vector4.One,view,projection);
            var border=PhysicalSurface.Quad(frameTexture,remote.Pose,new VRageMath.RectangleF(-(remote.Width+.06f)/2,remote.Height/2+.03f,remote.Width+.06f,remote.Height+.15f),new Vector4(0,0,1,1),Vector4.One,view,projection,.001f);
            NativeSprites.Draw(target,new[] {picture,border},handDepth:hands);
            if(remote.RayStart.HasValue && remote.RayEnd.HasValue)
            {
                var delta=remote.RayEnd.Value-remote.RayStart.Value;
                if(delta.LengthSquared()>.0001)
                    PhysicalSurface.Draw(target,new[] {new SurfaceView {Id="CameraRay",Style=SurfaceStyle.Pointer,Width=.002f,Height=(float)delta.Length(),
                        Pose=MatrixD.CreateWorld((remote.RayStart.Value+remote.RayEnd.Value)*.5,Vector3D.Normalize(delta),remote.Pose.Up)}},view,projection,null);
            }
        }
    }
}
