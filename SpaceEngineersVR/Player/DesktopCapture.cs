using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Plugin;
using VRageMath;
using Color=System.Drawing.Color;
using Device=SharpDX.Direct3D11.Device;
using Resource=SharpDX.DXGI.Resource;

namespace SpaceEngineersVR.Player
{
    // Desktop Duplication only works for outputs of the game device's adapter.
    internal static class DesktopCapture
    {
        internal sealed class Monitor
        {
            public string Name;
            public int Number;
            public IntPtr Handle;
        }
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll")] private static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
        private const byte MediaPlayPause=0xB3;
        private static volatile Monitor[] monitors=new Monitor[0];
        private static volatile Monitor active;
        private static long requested,badge;
        private static OutputDuplication duplication;
        private static string duplicated;
        private static Texture2D texture;
        private static OverlayCanvas badgeCanvas;
        private static ShaderResourceView badgeView;
        private static DateTime enumerated;
        private static IntPtr gameWindow;
        private static readonly RenderRecovery recovery=new RenderRecovery("Desktop mirror");
        internal static Monitor[] Monitors => monitors;
        internal static Monitor Active => active;
        // Render thread only.
        internal static ShaderResourceView View { get; private set; }
        internal static void ShowPreview(ShaderResourceView view) => View=view;
        internal static int DisplayedNumber => monitors.Length>1 ? active?.Number ?? 0 : 0;
        internal static float Aspect { get; private set; }=9f/16;
        internal static void Request() => System.Threading.Interlocked.Exchange(ref requested,DateTime.UtcNow.Ticks);
        internal static void PlayPause()
        {
            keybd_event(MediaPlayPause,0,1,UIntPtr.Zero);
            keybd_event(MediaPlayPause,0,3,UIntPtr.Zero);
            System.Threading.Interlocked.Exchange(ref badge,DateTime.UtcNow.Ticks);
        }
        internal static float BadgeAlpha(DateTime now)
        {
            double age=(now.Ticks-System.Threading.Interlocked.Read(ref badge))/(double)TimeSpan.TicksPerSecond;
            return age<0 || age>.55 ? 0 : (float)Math.Min(1,(.55-age)/.25);
        }
        internal static void NextMonitor()
        {
            var list=monitors;
            if(list.Length<2) return;
            int index=Array.FindIndex(list,m=>m.Name==active?.Name);
            Common.Config.DesktopMonitor=list[(index+1)%list.Length].Name;
        }
        internal static int Number(string name,int fallback)
        {
            int digits=name?.Length ?? 0;
            while(digits>0 && char.IsDigit(name[digits-1])) digits--;
            return name!=null && digits<name.Length && int.TryParse(name.Substring(digits),out int number) ? number : fallback;
        }
        internal static Monitor Choose(Monitor[] list,string saved,IntPtr game)
        {
            if(list.Length==0) return null;
            return list.FirstOrDefault(m=>m.Name==saved) ?? list.FirstOrDefault(m=>m.Handle!=game) ?? list[0];
        }
        internal static void Update(Device device)
        {
            var now=DateTime.UtcNow;
            if(now.Ticks-System.Threading.Interlocked.Read(ref requested)>TimeSpan.TicksPerSecond || recovery.Failed) { Release(); return; }
            try
            {
                if(monitors.Length==0 || now>enumerated) Enumerate(device,now);
                var choice=Choose(monitors,Common.Config.DesktopMonitor,GameMonitor());
                active=choice;
                if(choice==null) { Release(); return; }
                if(duplication==null || duplicated!=choice.Name) Duplicate(device,choice.Name);
                if(duplication!=null) Acquire(device);
                recovery.Succeeded();
            }
            catch(Exception ex) { Release(); recovery.Fail(ex,"Desktop mirror paused"); }
        }
        private static IntPtr GameMonitor()
        {
            if(gameWindow==IntPtr.Zero) gameWindow=Process.GetCurrentProcess().MainWindowHandle;
            return gameWindow==IntPtr.Zero ? IntPtr.Zero : MonitorFromWindow(gameWindow,2);
        }
        private static void Enumerate(Device device,DateTime now)
        {
            enumerated=now.AddSeconds(3);
            using(var dxgi=device.QueryInterface<SharpDX.DXGI.Device>())
            using(var adapter=dxgi.Adapter)
            {
                var found=new System.Collections.Generic.List<Monitor>();
                for(int i=0;i<adapter.GetOutputCount();i++)
                    using(var output=adapter.GetOutput(i))
                    {
                        var d=output.Description;
                        if(d.IsAttachedToDesktop) found.Add(new Monitor {Name=d.DeviceName,Number=Number(d.DeviceName,i+1),Handle=d.MonitorHandle});
                    }
                monitors=found.OrderBy(m=>m.Number).ToArray();
            }
        }
        private static void Duplicate(Device device,string name)
        {
            ReleaseDuplication();
            using(var dxgi=device.QueryInterface<SharpDX.DXGI.Device>())
            using(var adapter=dxgi.Adapter)
                for(int i=0;i<adapter.GetOutputCount();i++)
                    using(var output=adapter.GetOutput(i))
                    {
                        if(output.Description.DeviceName!=name) continue;
                        using(var output1=output.QueryInterface<Output1>()) duplication=output1.DuplicateOutput(device);
                        duplicated=name;
                        return;
                    }
        }
        private static void Acquire(Device device)
        {
            var result=duplication.TryAcquireNextFrame(0,out var info,out Resource frame);
            if(result.Code==SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code) return;
            if(result.Code==SharpDX.DXGI.ResultCode.AccessLost.Result.Code) { ReleaseDuplication(); return; }
            result.CheckError();
            try
            {
                if(info.LastPresentTime==0 && View!=null) return;
                using(var source=frame.QueryInterface<Texture2D>())
                {
                    var d=source.Description;
                    if(texture==null || texture.Description.Width!=d.Width || texture.Description.Height!=d.Height) Allocate(device,d.Width,d.Height);
                    device.ImmediateContext.CopySubresourceRegion(source,0,null,texture,0);
                    device.ImmediateContext.GenerateMips(View);
                }
            }
            finally { frame.Dispose(); duplication.ReleaseFrame(); }
        }
        private static void Allocate(Device device,int width,int height)
        {
            View?.Dispose(); texture?.Dispose();
            // Desktop pixels are sRGB-encoded; the sRGB view keeps mip generation and blending linear.
            texture=new Texture2D(device,new Texture2DDescription {Width=width,Height=height,MipLevels=0,ArraySize=1,Format=Format.B8G8R8A8_Typeless,
                SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,BindFlags=BindFlags.ShaderResource|BindFlags.RenderTarget,
                OptionFlags=ResourceOptionFlags.GenerateMipMaps});
            View=new ShaderResourceView(device,texture,new ShaderResourceViewDescription {Format=Format.B8G8R8A8_UNorm_SRgb,
                Dimension=SharpDX.Direct3D.ShaderResourceViewDimension.Texture2D,Texture2D={MostDetailedMip=0,MipLevels=-1}});
            Aspect=(float)height/width;
            Logger.Info("Desktop mirror: "+duplicated+" "+width+"x"+height);
        }
        private static void ReleaseDuplication() { duplication?.Dispose(); duplication=null; duplicated=null; }
        internal static void Release()
        {
            ReleaseDuplication();
            View?.Dispose(); texture?.Dispose(); View=null; texture=null;
        }
        internal static NativeSprite Picture(ShaderResourceView picture,MatrixD pose,float width,float height,MatrixD view,MatrixD projection,SharpDX.DXGI.Format target,float z)
        {
            var sprite=PhysicalSurface.Quad(picture,pose,new VRageMath.RectangleF(-width/2,height/2,width,height),new Vector4(0,0,1,1),Vector4.One,view,projection,z);
            sprite.Opaque=true; sprite.Sharpen=true;
            sprite.EncodeSrgb=FloatingMenu.NeedsSrgbEncoding(picture.Description.Format,target);
            return sprite;
        }
        // Shows both symbols: the media key's resulting state is unknown.
        internal static bool Badge(Device device,MatrixD pose,float diameter,MatrixD view,MatrixD projection,float z,float alpha,out NativeSprite sprite)
        {
            sprite=default(NativeSprite);
            if(alpha<=0) return false;
            if(badgeCanvas==null)
            {
                badgeCanvas=new OverlayCanvas("Desktop badge",256,256,1,false,device,mipMaps:true);
                var g=badgeCanvas.Graphics; badgeCanvas.Clear(Color.Transparent);
                g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var disc=new SolidBrush(Color.FromArgb(170,8,14,21))) g.FillEllipse(disc,4,4,248,248);
                g.FillPolygon(Brushes.White,new[] {new PointF(62,82),new PointF(62,174),new PointF(123,128)});
                g.FillRectangle(Brushes.White,143,82,18,92); g.FillRectangle(Brushes.White,180,82,18,92);
                badgeCanvas.Upload(); badgeView=new ShaderResourceView(device,badgeCanvas.Texture); device.ImmediateContext.GenerateMips(badgeView);
            }
            sprite=PhysicalSurface.Quad(badgeView,pose,new VRageMath.RectangleF(-diameter/2,diameter/2,diameter,diameter),new Vector4(0,0,1,1),new Vector4(1,1,1,alpha),view,projection,z);
            return true;
        }
    }
}
