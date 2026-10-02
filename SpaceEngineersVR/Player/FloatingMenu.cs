using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Plugin;
using VRageMath;
using Color=System.Drawing.Color;
using Matrix=VRageMath.Matrix;

namespace SpaceEngineersVR.Player
{
    internal static class FloatingMenu
    {
        internal sealed class Snapshot
        {
            public Matrix Pose;
            public float Width,Height,BarOffset=.085f;
            public int Hover;
        }
        private static readonly MenuWindow window=new MenuWindow();
        private static readonly InputGate press=new InputGate();
        private static volatile Snapshot current;
        private static volatile bool recenter=true;
        private static volatile float aspect=9f/16;
        private static string screen;
        private static bool opened,failed;
        private static int hover;
        private static OverlayCanvas frame;
        private static ShaderResourceView frameView;
        private static string painted;
        private static long lastUpdate;
        public static bool Available => !failed && MenuHands.Available;
        public static bool OwnsInput { get; private set; }
        public static Snapshot Current => current;
        public static void CaptureAspect(float value) { if(value>.1f && value<4) aspect=value; }
        public static void Recenter() { recenter=true; }
        public static void Update()
        {
            long now=System.Diagnostics.Stopwatch.GetTimestamp();
            float seconds=lastUpdate==0 ? 0 : (float)((now-lastUpdate)/(double)System.Diagnostics.Stopwatch.Frequency);
            lastUpdate=now;
            // Capture owns scrolling through release/cancel; neutral is required
            // before the thumbstick can scroll native contents again.
            if(window.Drag!=0) Controls.Static.MenuNavigate.BlockUntilRelease();
            OwnsInput=false;
            bool shown=(Main.MenuOpen && !MenuKeyboard.Standalone) || Main.ShowDesktopPanel;
            string key=MyScreenManager.Screens.FirstOrDefault(s=>!(s is MyGuiScreenGamePlay) && !(s is MyGuiScreenHudSpace))?.GetType().Name ?? "Desktop";
            bool available=shown && Available && InputRouter.Mode==InputMode.Menu && MenuPointer.GameFocused &&
                Player.Headset.pose.isTracked && Player.HandR.pose.isTracked && !MenuKeyboard.IsOpen;
            if(!shown) { opened=false; current=null; window.Cancel(); press.Block(); return; }
            if(!opened || screen!=key || recenter)
            {
                bool reset=recenter; recenter=false; screen=key; opened=true;
                window.Place(Player.Headset.pose.deviceToAbsolute.matrix);
                if(!reset) Restore(key);
                press.Block();
            }
            if(Math.Abs(window.Aspect-aspect)>.0001f) { window.Cancel(); window.Aspect=aspect; press.Block(); }
            press.Update(available,Controls.Static.Primary.RawPressed); hover=0;
            if(!available) window.Cancel();
            else
            {
                Matrix aim=MenuHands.PointerTracking();
                if(window.Drag!=0)
                {
                    OwnsInput=true;
                    if(!Controls.Static.Primary.RawPressed) { window.Stop(); Save(); }
                    else
                    {
                        if(window.Pointer(aim,out var point,true) || window.Drag==1)
                            window.Move(aim,point,Controls.Static.MenuNavigate.RawPosition,seconds);
                        Controls.Static.Primary.BlockUntilRelease();
                    }
                }
                else if(window.Pointer(aim,out var point))
                {
                    hover=window.Handle(point); OwnsInput=hover!=0;
                    if(hover!=0 && press.Pressed)
                    {
                        Controls.Static.Primary.BlockUntilRelease(); press.Block(); MenuPointer.Release();
                        window.Begin(hover,aim,point);
                        Controls.Static.MenuNavigate.BlockUntilRelease();
                        Player.HandR.Vibrate(0,.022f,100,.28f);
                    }
                }
            }
            current=new Snapshot { Pose=window.Pose,Width=window.Width,Height=window.Height,Hover=window.Drag!=0 ? window.Drag : hover };
        }
        private static void Restore(string key)
        {
            var saved=Common.Config.MenuWindows?.FirstOrDefault(s=>s.Screen==key);
            if(saved==null) return;
            var p=new Vector3(saved.X,saved.Y,saved.Z); var q=new Quaternion(saved.QX,saved.QY,saved.QZ,saved.QW);
            if(!p.IsValid() || p.Length()<.35f || p.Length()>4 || p.Z>-.25f || !q.LengthSquared().IsValid() || q.LengthSquared()<.9f || q.LengthSquared()>1.1f ||
                float.IsNaN(saved.Width) || saved.Width<MenuWindow.MinWidth || saved.Width>MenuWindow.MaxWidth) return;
            q.Normalize(); var pose=Matrix.CreateFromQuaternion(q); pose.Translation=p;
            window.Pose=pose*VrMath.TrackingOrigin(Player.Headset.pose.deviceToAbsolute.matrix); window.Width=saved.Width;
        }
        private static void Save()
        {
            Matrix local=window.Pose*Matrix.Invert(VrMath.TrackingOrigin(Player.Headset.pose.deviceToAbsolute.matrix));
            var p=local.Translation; var q=Quaternion.CreateFromRotationMatrix(local.GetOrientation());
            var entry=new MenuWindowSetting { Screen=screen,Width=window.Width,X=p.X,Y=p.Y,Z=p.Z,QX=q.X,QY=q.Y,QZ=q.Z,QW=q.W };
            Common.Config.MenuWindows=(Common.Config.MenuWindows ?? new MenuWindowSetting[0]).Where(s=>s.Screen!=screen).Take(31).Concat(new[] { entry }).ToArray();
        }
        internal static void Paint(OverlayCanvas canvas,Snapshot s)
        {
            canvas.Clear(Color.Transparent);
            var g=canvas.Graphics; var state=g.Save();
            try
            {
                float w=s.Width+.06f,h=s.Height+.20f+s.BarOffset-.085f;
                g.ScaleTransform(1600/w,1100/h); g.TranslateTransform(w/2,.03f+s.Height/2);
                // Native menu contents retain their own texture, without a tint.
                float bottom=s.Height/2+s.BarOffset;
                using(var pen=new Pen(s.Hover==1 ? Color.Cyan : Color.White,.010f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
                    g.DrawLine(pen,-.15f,bottom,.15f,bottom);
                using(var pen=new Pen(s.Hover==2 ? Color.Cyan : Color.White,.006f) { StartCap=LineCap.Round,EndCap=LineCap.Round })
                {
                    g.DrawLine(pen,s.Width/2-.025f,bottom+.024f,s.Width/2+.024f,bottom+.024f);
                    g.DrawLine(pen,s.Width/2+.024f,bottom+.024f,s.Width/2+.024f,bottom-.025f);
                }
            }
            finally { g.Restore(state); }
        }
        internal static void DrawPanel(Texture2D target,ShaderResourceView contents,Snapshot s,MatrixD view,MatrixD projection,ShaderResourceView handDepth=null)
        {
            DrawFrame(target,s,view,projection,handDepth);
            var sprite=PhysicalSurface.Quad(contents,s.Pose,new VRageMath.RectangleF(-s.Width/2,s.Height/2,s.Width,s.Height),
                new Vector4(0,0,1,1),Vector4.One,view,projection,.001f);
            sprite.Rounded=new Vector2(.014f/s.Width,.014f/s.Height);
            NativeSprites.Draw(target,new[] {sprite},handDepth:handDepth);
        }
        public static void DrawFrame(Texture2D target,Snapshot s,MatrixD view,MatrixD projection,ShaderResourceView handDepth=null)
        {
            if(!Available) return;
            try
            {
                if(frame==null) { frame=new OverlayCanvas("Menu frame",1600,1100,1,false,target.Device,true); frameView=new ShaderResourceView(target.Device,frame.Texture); }
                string key=s.Width+"|"+s.Height+"|"+s.Hover;
                if(key!=painted) { Paint(frame,s); frame.Upload(); target.Device.ImmediateContext.GenerateMips(frameView); painted=key; }
                NativeSprites.Draw(target,new[] { PhysicalSurface.Quad(frameView,s.Pose,
                    new VRageMath.RectangleF(-s.Width/2-.03f,s.Height/2+.03f,s.Width+.06f,s.Height+.20f),
                    new Vector4(0,0,1,1),Vector4.One,view,projection) },handDepth:handDepth);
            }
            catch(Exception ex) { failed=true; Logger.Warning(ex,"Floating menu frame disabled; native menu retained"); }
        }
    }
}
