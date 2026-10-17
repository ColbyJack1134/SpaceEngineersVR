using System;
using Sandbox.Game.Entities;
using SharpDX.Direct3D11;
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
        private static volatile bool recenter;
        private static volatile float aspect=9f/16;
        private static string spot;
        private static bool opened;
        private static readonly RenderRecovery recovery=new RenderRecovery("Floating menu");
        private static bool failed => recovery.Failed;
        private static int hover;
        private static long lastUpdate;
        public static bool Available => !failed && MenuHands.Available;
        public static bool OwnsInput { get; private set; }
        public static Snapshot Current => current;
        public static void CaptureAspect(float value) { if(value>.1f && value<4) aspect=value; }
        internal static float Aspect => aspect;
        public static void Recenter() { recenter=true; }
        internal static void Follow(Matrix oldOrigin,Matrix newOrigin)
        { if(opened && Seat!=null) { window.Cancel(); window.Pose=WindowSpots.Follow(window.Pose,oldOrigin,newOrigin); } }
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
            bool available=shown && Available && InputRouter.Mode==InputMode.Menu && MenuPointer.GameFocused &&
                Player.Headset.pose.isTracked && MenuPointer.Hand.pose.isTracked && !MenuKeyboard.IsOpen;
            if(!shown) { opened=false; current=null; window.Cancel(); interaction.Reset(); return; }
            var seat=Seat;
            string key=Spot(seat);
            if(!opened || spot!=key || recenter)
            {
                bool reset=recenter; recenter=false; spot=key; opened=true;
                Fit(aspect,MenuWindow.DefaultHeight);
                if(reset || !Restore(seat)) Place(seat);
                if(reset) Save();
                interaction.Reset();
            }
            if(Math.Abs(window.Aspect-aspect)>.0001f) { window.Cancel(); Fit(aspect,window.Height); interaction.Reset(); }
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
        private static MyCockpit Seat => Main.WorldAvailable && SeatFit.Eligible(SeatFit.Seat) ? SeatFit.Seat:null;
        private static string Spot(MyCockpit seat) => seat!=null ? "Menu/"+seat.BlockDefinition.Id.SubtypeName:"Menu";
        private static MatrixD TrackingToWorld => SpatialUi.DeviceWorld(Matrix.Identity);
        private static MatrixD ToSeat(MyCockpit seat) => TrackingToWorld*seat.PositionComp.WorldMatrixNormalizedInv;
        // Menus keep their height when the monitor shape changes, so a wider monitor only adds width.
        private static void Fit(float value,float height)
        {
            window.Aspect=value;
            window.MinimumWidth=MenuWindow.MinHeight/value; window.MaximumWidth=MenuWindow.MaxHeight/value;
            window.Width=MathHelper.Clamp(height/value,window.MinimumWidth,window.MaximumWidth);
        }
        private static void Place(MyCockpit seat)
        {
            var head=Player.Headset.pose.deviceToAbsolute.matrix;
            if(seat!=null && CockpitScreens.Main(seat,SpatialUi.DeviceWorld(head).Translation,out var screen,out var area))
            {
                window.Stop(); window.Width=CockpitScreens.Fit(area,window.Aspect,window.MinimumWidth);
                window.Pose=(Matrix)(screen*MatrixD.Invert(TrackingToWorld));
            }
            else window.Place(head);
        }
        private static bool Restore(MyCockpit seat)
        {
            if(!WindowSpots.Find(spot,out var local,out var saved) || !saved.Height.IsValid() || saved.Height<MenuWindow.MinHeight || saved.Height>MenuWindow.MaxHeight) return false;
            var pose=seat!=null ? (Matrix)((MatrixD)local*MatrixD.Invert(ToSeat(seat))):local;
            if(seat==null && !WindowSpots.Keep(pose,Player.Headset.pose.deviceToAbsolute.matrix)) return false;
            window.Stop(); window.Pose=pose; Fit(window.Aspect,saved.Height);
            return true;
        }
        private static void Save()
        {
            var seat=Seat;
            if(Spot(seat)!=spot) return;
            var local=seat!=null ? (Matrix)((MatrixD)window.Pose*ToSeat(seat)):window.Pose;
            WindowSpots.Store(spot,local,window.Width,window.Height);
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
