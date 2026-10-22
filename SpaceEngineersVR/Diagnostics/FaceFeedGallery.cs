#if SEVR_DIAGNOSTICS
using System;
using System.Drawing;
using System.IO;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRageMath;
using Color=System.Drawing.Color;

namespace SpaceEngineersVR.Diagnostics
{
    // Production feed placement over stand-in images: <output>/src/backdrop.png, feed_wide.png and feed_zoom.png.
    public static class FaceFeedGallery
    {
        // Steam Frame left eye.
        private const float Left=-1.664f,Right=1.210f,Top=-1.724f,Bottom=1.174f;
        private const float SourceFov=60;

        public static void Render(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output);
            UiTests.Initialize(game,Path.Combine(output,"data"));
            string src=Path.Combine(output,"src");
            var projection=VrMath.Projection(Left,Right,Top,Bottom,.03);
            using(var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))
            using(var eye=new OverlayCanvas("Face feed eye",1440,1440,1,false,device))
            using(var backdrop=new Picture(device,Path.Combine(src,"backdrop.png")))
            using(var wide=new Picture(device,Path.Combine(src,"feed_wide.png")))
            using(var zoom=new Picture(device,Path.Combine(src,"feed_zoom.png")))
            {
                void Scene(string name,Action draw)
                {
                    eye.Clear(Color.Black); eye.Upload();
                    float h=2*1000*(float)Math.Tan(MathHelper.ToRadians(SourceFov)/2);
                    NativeSprites.Draw(eye.Texture,new[] {PhysicalSurface.Quad(backdrop.View,MatrixD.CreateTranslation(0,0,-1000),
                        new VRageMath.RectangleF(-h*8/9,h/2,h*16/9,h),new Vector4(0,0,1,1),Vector4.One,MatrixD.Identity,projection)});
                    draw();
                    UiTests.Save(eye.Texture,Path.Combine(output,"face-"+name+".png"));
                }
                var head=MatrixD.Identity;
                foreach(int mode in new[] {0,1})
                    Scene("window-aim-"+mode,()=>RemoteFeed.DrawPanel(eye.Texture,wide.View,new RemoteView.View {
                        Pose=MatrixD.CreateTranslation(0,-.08,-1.5),Width=1.2f,Height=.675f,SignalMode=0,AimMode=mode},MatrixD.Identity,projection));
                Scene("aligned-wide",()=>RemoteFeed.DrawFace(eye.Texture,wide.View,head,head,RemoteFeed.FaceDistance,MatrixD.Identity,projection));
                Scene("frame",()=> {
                    RemoteFeed.DrawFace(eye.Texture,wide.View,head,head,RemoteFeed.FaceDistance,MatrixD.Identity,projection);
                    RemoteFeed.DrawPanel(eye.Texture,null,new RemoteView.View {Pose=MatrixD.CreateTranslation(.55,-.35,-1.5)*MatrixD.CreateRotationY(-.35),
                        Width=1.2f,Height=.675f,SignalMode=0,AimMode=2,Face=true},MatrixD.Identity,projection);
                });
                Scene("aligned-zoom",()=>RemoteFeed.DrawFace(eye.Texture,zoom.View,head,head,RemoteFeed.FaceDistance,MatrixD.Identity,projection));
                Scene("full-fov",()=> {
                    float h=2*RemoteFeed.FaceDistance*(float)Math.Tan(MathHelper.ToRadians(SourceFov)/2);
                    NativeSprites.Draw(eye.Texture,new[] {PhysicalSurface.Quad(wide.View,MatrixD.CreateTranslation(0,0,-RemoteFeed.FaceDistance),
                        new VRageMath.RectangleF(-h*8/9,h/2,h*16/9,h),new Vector4(0,0,1,1),new Vector4(1,1,1,RemoteFeed.FaceOpacity),MatrixD.Identity,projection)});
                });
                using(var detail=new OverlayCanvas("Face feed toggles",900,300,1,false,device))
                foreach(var (name,mode,hover) in new[] {("0",0,0),("1",1,0),("2",2,0),("hover",1,WindowFrame.AimHover)})
                {
                    detail.Clear(Color.FromArgb(8,14,21)); detail.Upload();
                    var pose=Matrix.CreateTranslation(0,0,-1.5f);
                    var view=MatrixD.Invert(MatrixD.CreateTranslation(-.5,.37,-.95));
                    WindowFrame.Draw(detail.Texture,"Remote",new WindowFrame.Snapshot {Pose=pose,Width=1.2f,Height=.675f,Zoom=true,SignalMode=0,Aim=mode,Hover=hover},
                        view,VrMath.Projection(-.3f,.3f,-.1f,.1f,.03));
                    UiTests.Save(detail.Texture,Path.Combine(output,"face-aim-icon-"+name+".png"));
                }
                WindowFrame.Reset("Remote");
            }
            log("PASS face feed gallery");
        }
        private sealed class Picture : IDisposable
        {
            private readonly OverlayCanvas canvas;
            public readonly ShaderResourceView View;
            public Picture(Device device,string path)
            {
                using(var image=new Bitmap(path))
                {
                    canvas=new OverlayCanvas(Path.GetFileName(path),image.Width,image.Height,1,false,device);
                    canvas.Graphics.DrawImage(image,0,0,image.Width,image.Height);
                }
                canvas.Upload(); View=new ShaderResourceView(device,canvas.Texture);
            }
            public void Dispose() { View.Dispose(); canvas.Dispose(); }
        }
    }
}
#endif
