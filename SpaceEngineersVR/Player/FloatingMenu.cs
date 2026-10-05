using System;
using System.Linq;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRageMath;
using Matrix=VRageMath.Matrix;
using Snapshot=SpaceEngineersVR.Player.WindowFrame.Snapshot;

namespace SpaceEngineersVR.Player
{
    internal static class FloatingMenu
    {
        private static readonly MenuWindow window=new MenuWindow();
        private static readonly WindowInteraction interaction=new WindowInteraction(window);
        private static volatile Snapshot current;
        private static volatile bool recenter=true;
        private static volatile float aspect=9f/16;
        private static string screen;
        private static bool opened;
        private static readonly RenderRecovery recovery=new RenderRecovery("Floating menu");
        private static bool failed => recovery.Failed;
        private static int hover;
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
                Player.Headset.pose.isTracked && MenuPointer.Hand.pose.isTracked && !MenuKeyboard.IsOpen;
            if(!shown) { opened=false; current=null; window.Cancel(); interaction.Reset(); return; }
            if(!opened || screen!=key || recenter)
            {
                bool reset=recenter; recenter=false; screen=key; opened=true;
                window.Place(Player.Headset.pose.deviceToAbsolute.matrix);
                if(!reset) Restore(key);
                interaction.Reset();
            }
            if(Math.Abs(window.Aspect-aspect)>.0001f) { window.Cancel(); window.Aspect=aspect; interaction.Reset(); }
            hover=0;
            if(!available) { window.Cancel(); interaction.Reset(); }
            else
            {
                var hand=MenuPointer.Hand;
                Matrix aim=MenuHands.PointerTracking(false,hand);
                OwnsInput=interaction.Update(hand,aim,seconds);
                hover=interaction.Hover;
                if(interaction.Captured) MenuPointer.Release();
                if(interaction.Released) Save();
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
        internal static void Paint(OverlayCanvas canvas,Snapshot s) => WindowFrame.Paint(canvas,s);
        internal static void DrawPanel(Texture2D target,ShaderResourceView contents,Snapshot s,MatrixD view,MatrixD projection,ShaderResourceView handDepth=null)
        {
            DrawFrame(target,s,view,projection,handDepth);
            var sprite=PhysicalSurface.Quad(contents,s.Pose,new VRageMath.RectangleF(-s.Width/2,s.Height/2,s.Width,s.Height),
                new Vector4(0,0,1,1),Vector4.One,view,projection,.001f);
            sprite.Rounded=new Vector2(.014f/s.Width,.014f/s.Height);
            sprite.EncodeSrgb=NeedsSrgbEncoding(contents.Description.Format,target.Description.Format);
            NativeSprites.Draw(target,new[] {sprite},handDepth:handDepth);
        }
        internal static bool NeedsSrgbEncoding(SharpDX.DXGI.Format source,SharpDX.DXGI.Format target) =>
            (source==SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb || source==SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb) &&
            target!=SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb && target!=SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb;
        public static void DrawFrame(Texture2D target,Snapshot s,MatrixD view,MatrixD projection,ShaderResourceView handDepth=null)
        {
            if(!Available) return;
            try
            {
                WindowFrame.Draw(target,"Menu",s,view,projection,handDepth);
            }
            catch(Exception ex) { recovery.Fail(ex,"Floating menu frame disabled; native menu retained"); }
        }
    }
}
