#if SEVR_DIAGNOSTICS
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DataBox=SharpDX.DataBox;
using DataStream=SharpDX.DataStream;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Player;
using VRage.FileSystem;
using VRageMath;
using Device=SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Diagnostics
{
    public static class CameraSightPreview
    {
        public static void Export(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output); UiTests.Initialize(game,Path.Combine(output,"data"));
            HoloSightTests.Run(log);
            using(var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))
            using(var frame=new OverlayCanvas("Camera pan toggle",1600,1100,1,false,device))
            using(var target=new OverlayCanvas("Sight fixture",900,600,1,false,device))
            using(var depth=new Texture2D(device,new Texture2DDescription {Width=900,Height=600,MipLevels=1,ArraySize=1,Format=Format.R32_Float,
                SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource}))
            using(var depthView=new ShaderResourceView(device,depth))
            {
                foreach(float width in new[] {.7f,1.2f}) foreach(int pan in new[] {-1,0,1})
                {
                    var view=new RemoteView.View {Width=width,Height=width*9/16,PanMode=pan,SignalMode=0};
                    UiTests.Render(frame,()=>RemoteFeed.Paint(frame,view));
                    UiTests.Save(frame.Texture,Path.Combine(output,"camera-"+width.ToString("0.0",CultureInfo.InvariantCulture)+"-"+pan+".png"));
                    var point=new Vector3(WindowFrame.AimX(width),WindowFrame.SignalsY(view.Height),0);
                    if(WindowFrame.Control(point,width,view.Height,.035f,true,false,false,true,false,pan>=0)!=(pan>=0 ? WindowFrame.PanHover:0))
                        throw new Exception("Camera Pan visibility and hit target disagree");
                    point.X=WindowFrame.SignalsX(width);
                    if(WindowFrame.Control(point,width,view.Height,.035f,true,false,false,true,false,pan>=0)!=WindowFrame.SignalsHover)
                        throw new Exception("Camera Pan overlaps signal control");
                }
                string fixtures=Path.Combine(output,"sight-scenes.csv");
                foreach(string line in File.ReadAllLines(fixtures))
                {
                    var f=line.Split(','); string name=f[0],item=f[1];
                    double Number(int i) => double.Parse(f[i],CultureInfo.InvariantCulture);
                    var eye=new Vector3D(Number(2),Number(3),Number(4));
                    var profile=WeaponProfile.All.Single(p=>p.Item==item);
                    var reticle=HoloSight.Build(item,Path.Combine(MyFileSystem.ContentPath,profile.Model),Path.Combine(output,"models",profile.Model),Path.Combine(output,"masks"));
                    var state=new HoloSight.View {Gun=MatrixD.Identity,Reticle=reticle};
                    var view=MatrixD.CreateTranslation(-eye); var projection=VrMath.Projection(-.15f,.15f,-.1f,.1f,.03);
                    byte[] bytes=File.ReadAllBytes(Path.Combine(output,name+"-depth.bin"));
                    using(var data=new DataStream(bytes.Length,true,true))
                    { data.Write(bytes,0,bytes.Length); data.Position=0; device.ImmediateContext.UpdateSubresource(new DataBox(data.DataPointer,900*4,0),depth); }
                    using(var background=new System.Drawing.Bitmap(Path.Combine(output,name+"-scene.png")))
                    foreach(bool before in new[] {true,false})
                    {
                        var rims=reticle.Rims;
                        if(before && item=="PreciseAutomaticRifleItem") reticle.Rims=Array.Empty<HoloSight.Rim>();
                        void Draw()
                        {
                            target.Clear(System.Drawing.Color.Black); target.Graphics.DrawImageUnscaled(background,0,0); target.Upload();
                            HoloSight.Draw(target.Texture,state,view,projection,depthView);
                        }
                        Draw(); SignalTests.WaitIcons(); Draw();
                        UiTests.Save(target.Texture,Path.Combine(output,name+(before ? "-before":"-after")+".png"));
                        target.Clear(System.Drawing.Color.Transparent); target.Upload();
                        HoloSight.Draw(target.Texture,state,view,projection,depthView);
                        UiTests.Save(target.Texture,Path.Combine(output,name+(before ? "-before":"-after")+"-reticle.png"));
                        reticle.Rims=rims;
                    }
                }
            }
            log("PASS camera frame production controls and sight sprites against installed-mesh inspection color/depth fixtures");
        }
    }
}
#endif
