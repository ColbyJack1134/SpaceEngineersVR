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
        }
    }
}
