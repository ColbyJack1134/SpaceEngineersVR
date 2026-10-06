using System;
using System.Drawing;
using Color=System.Drawing.Color;
using System.IO;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class WindowGallery
    {
        internal static void Render(Device device,string output)
        {
            using(var source=new OverlayCanvas("Window contents",1920,1080,1,false,device))
            using(var target=new OverlayCanvas("Window gallery",1200,900,1,false,device))
            using(var texture=new ShaderResourceView(device,source.Texture))
            foreach(string name in new[] {"settings","keyboard","camera","menu"})
            {
                float width=name=="settings" ? .55f:name=="keyboard" ? .62f:name=="camera" ? 1.2f:2.4f;
                float height=width*(name=="settings" ? .31f/.55f:name=="keyboard" ? KeyboardWindow.Aspect:9f/16);
                var pose=Matrix.CreateTranslation(0,.04f,-width/.95f);
                var projection=VrMath.Projection(-.7f,.7f,-.525f,.525f,.03);
                var frame=new WindowFrame.Snapshot {Pose=pose,Width=width,Height=height,Zoom=name=="camera"};
                target.Clear(Color.FromArgb(8,14,21)); target.Upload();
                if(name=="settings" || name=="keyboard")
                {
                    var panel=name=="settings" ? FlightSettings.View("window-gallery-settings",pose,width,height,
                        FlightSettings.Layout(new Multiplayer.FlightTuning(),false,false,false,"Fighter Cockpit Flight Settings"),"Fighter Cockpit Flight Settings"):
                        new SurfaceView {Id="window-gallery-keyboard",Style=SurfaceStyle.Keyboard,Pose=pose,Width=width,Height=height,Text="Reactor",Keys=MenuKeyboard.MakeKeys(false)};
                    FloatingSurface.Draw(target.Texture,new[] {panel},MatrixD.Identity,projection);
                    WindowFrame.Draw(target.Texture,"Gallery",frame,MatrixD.Identity,projection);
                }
                else
                {
                    if(name=="menu")
                    {
                        string capture=Path.Combine(Path.GetDirectoryName(output),"physical-renderer","physical-sticks-options.png");
                        if(!File.Exists(capture)) continue;
                        using(var image=new Bitmap(capture)) source.Graphics.DrawImage(image,0,0,source.Width,source.Height);
                    }
                    else RemoteViewTests.PaintSource(source);
                    source.Upload();
                    if(name=="menu") FloatingMenu.DrawPanel(target.Texture,texture,frame,MatrixD.Identity,projection);
                    else RemoteFeed.DrawPanel(target.Texture,texture,new RemoteView.View {Pose=pose,Width=width,Height=height},MatrixD.Identity,projection);
                }
                UiTests.Save(target.Texture,Path.Combine(output,"window-gallery-"+name+".png"));
                using(var detail=new OverlayCanvas("Window handle detail",600,240,1,false,device))
                foreach(bool corner in new[] {false,true})
                {
                    detail.Clear(Color.FromArgb(8,14,21)); detail.Upload();
                    var eye=pose.Translation+new Vector3(corner ? width/2:0,-height/2-frame.BarOffset,.45f);
                    var closeView=MatrixD.Invert(MatrixD.CreateTranslation(eye));
                    float tangent=corner ? .2f:.5f;
                    WindowFrame.Draw(detail.Texture,"Gallery",frame,closeView,VrMath.Projection(-tangent,tangent,-tangent*.4f,tangent*.4f,.03));
                    UiTests.Save(detail.Texture,Path.Combine(output,"window-"+(corner ? "resize":"move")+"-"+name+".png"));
                }
            }
            WindowFrame.Reset("Gallery");
            RenderDesktop(device,output);
        }
        private static void RenderDesktop(Device device,string output)
        {
            using(var source=new OverlayCanvas("Desktop stand-in",1920,1080,1,false,device))
            using(var target=new OverlayCanvas("Desktop gallery",1200,900,1,false,device))
            using(var texture=new ShaderResourceView(device,source.Texture))
            {
                PaintDesktop(source); source.Upload();
                var projection=VrMath.Projection(-.7f,.7f,-.525f,.525f,.03);
                float width=1.6f,height=width*9/16;
                var pose=Matrix.CreateTranslation(0,.04f,-width/.95f);
                foreach(var state in new[] {"","-click"})
                {
                    target.Clear(Color.FromArgb(8,14,21)); target.Upload();
                    DesktopWindow.DrawPanel(target.Texture,texture,new DesktopWindow.View {Pose=pose,Width=width,Height=height,Monitor=2},MatrixD.Identity,projection,state=="" ? 0:1);
                    UiTests.Save(target.Texture,Path.Combine(output,"window-gallery-desktop"+state+".png"));
                }
                using(var detail=new OverlayCanvas("Desktop control detail",600,240,1,false,device))
                foreach(int hover in new[] {0,WindowFrame.CloseHover,WindowFrame.MonitorHover})
                {
                    detail.Clear(Color.FromArgb(8,14,21)); detail.Upload();
                    var eye=pose.Translation+new Vector3(-width/2+.09f,-height/2-.035f,.45f);
                    DesktopWindow.DrawPanel(detail.Texture,texture,new DesktopWindow.View {Pose=pose,Width=width,Height=height,Monitor=2,Hover=hover},
                        MatrixD.Invert(MatrixD.CreateTranslation(eye)),VrMath.Projection(-.2f,.2f,-.08f,.08f,.03),0);
                    UiTests.Save(detail.Texture,Path.Combine(output,"window-desktop-controls-"+hover+".png"));
                }
                WindowFrame.Reset("Desktop");
                DesktopCapture.ShowPreview(texture);
                try
                {
                    foreach(bool revealed in new[] {false,true})
                    {
                        target.Clear(Color.FromArgb(8,14,21)); target.Upload();
                        var wrist=new SurfaceView {Id="gallery-wrist-desktop",Style=SurfaceStyle.WristMenu,Width=.40f,Height=.225f,Desktop=true,
                            Pose=MatrixD.CreateTranslation(0,0,-.40f/.95f),Keys=WristPanel.DesktopKeys(revealed,2)};
                        PhysicalSurface.Draw(target.Texture,new[] {wrist},MatrixD.Identity,projection,null);
                        UiTests.Save(target.Texture,Path.Combine(output,"wrist-desktop-"+(revealed ? "revealed":"idle")+".png"));
                    }
                }
                finally { DesktopCapture.ShowPreview(null); }
            }
        }
        private static void PaintDesktop(OverlayCanvas canvas)
        {
            var g=canvas.Graphics;
            canvas.Clear(Color.FromArgb(32,33,36));
            using(var bar=new SolidBrush(Color.FromArgb(53,54,58))) { g.FillRectangle(bar,0,0,1920,40); g.FillRectangle(bar,120,50,1280,30); }
            using(var sky=new System.Drawing.Drawing2D.LinearGradientBrush(new System.Drawing.Rectangle(60,110,1280,720),Color.FromArgb(30,60,120),Color.FromArgb(150,150,180),90f))
                g.FillRectangle(sky,60,110,1280,720);
            using(var hills=new SolidBrush(Color.FromArgb(38,52,66)))
                g.FillPolygon(hills,new[] {new PointF(60,628),new PointF(342,384),new PointF(636,614),new PointF(854,441),new PointF(1340,672),new PointF(1340,830),new PointF(60,830)});
            using(var moon=new SolidBrush(Color.FromArgb(250,236,200))) g.FillEllipse(moon,930,210,128,128);
            using(var progress=new SolidBrush(Color.FromArgb(230,60,60))) g.FillRectangle(progress,60,824,470,6);
            using(var line=new SolidBrush(Color.FromArgb(70,72,76)))
                for(int i=0;i<7;i++) { g.FillRectangle(line,1380,110+i*135,200,112); g.FillRectangle(line,1595,118+i*135,265,18); }
        }
    }
}
