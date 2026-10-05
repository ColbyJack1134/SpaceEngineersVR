using System;
using System.Linq;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using SpaceEngineersVR.Wrappers;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player.Components
{
    public static class VRGUIManager
    {
        private static ulong overlayHandle;
        internal static ulong OverlayHandle => overlayHandle;
        private static Texture2D blackTexture, desktopCopy;
        private static ShaderResourceView desktopView;
        private static bool visible, reportedMenu;
        private static volatile bool anchorPending = true;
        private static readonly object panelLock = new object();
        private static Matrix panelPose;
        private static float panelHeight,panelWidth=2.4f;
        public static bool TryPanelHit(Matrix hand, out Vector2 uv)
        {
            lock(panelLock)
            {
                uv=Vector2.Zero;
                var current=FloatingMenu.Current;
                return panelHeight>0 && (current!=null ? VrMath.PanelHit(hand,current.Pose,current.Width,current.Height,out uv) :
                    VrMath.PanelHit(hand,panelPose,panelWidth,panelHeight,out uv));
            }
        }
        public static float PointerDistance(Matrix aim)
        {
            lock(panelLock)
            {
                Matrix local=aim*Matrix.Invert(FloatingMenu.Current?.Pose ?? panelPose);
                if(panelHeight>0 && local.Translation.Z>0 && local.Forward.Z < -0.0001f)
                    return Math.Min(3f,-local.Translation.Z/local.Forward.Z);
                return 3f;
            }
        }
        private static readonly VRTextureBounds_t FullBounds = new VRTextureBounds_t { uMax=1, vMax=1 };
        private static void Check(EVROverlayError error)
        {
            if (error != EVROverlayError.None) throw new InvalidOperationException("Menu overlay: " + error);
        }
        static VRGUIManager()
        {
            Check(OpenVR.Overlay.CreateOverlay("sevr.prototype.menu","Space Engineers menu",ref overlayHandle));
            Check(OpenVR.Overlay.SetOverlayWidthInMeters(overlayHandle,2.4f));
            Check(OpenVR.Overlay.SetOverlayTexelAspect(overlayHandle,1f));
        }
        public static MyGuiScreenBase TopScreen => MyScreenManager.Screens.LastOrDefault(screen =>
            !(screen is MyGuiScreenGamePlay) && !(screen is MyGuiScreenHudSpace));
        public static bool IsAnyDialogOpen() => TopScreen != null;
        public static void RequestRecenter() { anchorPending = true; FloatingMenu.Recenter(); }
        private static Texture_t Texture(Texture2D value) => new Texture_t {
            eColorSpace=EColorSpace.Auto,eType=ETextureType.DirectX,handle=value.NativePointer };
        public static void Draw()
        {
            if ((Main.MenuOpen && !MenuKeyboard.Standalone) || Main.ShowDesktopPanel)
            {
                // Anchor once in tracking space, not to the user's face on every frame.
                var spatial=FloatingMenu.Current;
                if ((spatial!=null || !visible || anchorPending) && Player.Headset.renderPose.isTracked)
                {
                    Matrix panel = spatial?.Pose ?? Matrix.CreateTranslation(0,0,-2.2f) * VrMath.TrackingOrigin(Player.Headset.renderPose.deviceToAbsolute.matrix);
                    lock(panelLock) { panelPose=panel; panelWidth=spatial?.Width ?? 2.4f; }
                    Check(OpenVR.Overlay.SetOverlayWidthInMeters(overlayHandle,panelWidth));
                    var transform = new HmdMatrix34_t {
                        m0=panel.M11,m1=panel.M21,m2=panel.M31,m3=panel.M41,
                        m4=panel.M12,m5=panel.M22,m6=panel.M32,m7=panel.M42,
                        m8=panel.M13,m9=panel.M23,m10=panel.M33,m11=panel.M43 };
                    Check(OpenVR.Overlay.SetOverlayTransformAbsolute(overlayHandle,ETrackingUniverseOrigin.TrackingUniverseStanding,ref transform));
                    anchorPending = false;
                }
                var source=(Texture2D)MyRender11.GetBackbuffer().GetResource();
                var desc=source.Description;
                FloatingMenu.CaptureAspect((float)desc.Height/desc.Width);
                lock(panelLock) panelHeight=panelWidth*desc.Height/desc.Width;
                if (desktopCopy == null || desktopCopy.Description.Width != desc.Width || desktopCopy.Description.Height != desc.Height || desktopCopy.Description.Format != desc.Format)
                {
                    desktopView?.Dispose(); desktopCopy?.Dispose();
                    desc.Usage=ResourceUsage.Default; desc.CpuAccessFlags=CpuAccessFlags.None;
                    desc.OptionFlags=ResourceOptionFlags.None; desc.BindFlags=BindFlags.ShaderResource;
                    desktopCopy=new Texture2D(MyRender11.DeviceInstance,desc);
                    desktopView=new ShaderResourceView(MyRender11.DeviceInstance,desktopCopy);
                }
                MyRender11.DeviceInstance.ImmediateContext.CopyResource(source,desktopCopy);
                var texture=Texture(desktopCopy);
                Check(OpenVR.Overlay.SetOverlayTexture(overlayHandle,ref texture));
                // A compositor overlay always covers submitted stereo geometry.
                Check(MenuHands.Available && (Main.MenuOpen || !Main.WorldAvailable) ? OpenVR.Overlay.HideOverlay(overlayHandle) : OpenVR.Overlay.ShowOverlay(overlayHandle));
                if (!reportedMenu) { Logger.Info("NATIVE MENU ready: tracking-space panel, " + desc.Width + "x" + desc.Height + ", world stereo=" + Main.WorldAvailable); reportedMenu=true; }
                visible=true;
            }
            else { Hide(); }
        }
        internal static void DrawStereo(Texture2D target,EVREye eye,ShaderResourceView handDepth=null)
        {
            if((!Main.MenuOpen && !Main.ShowDesktopPanel) || desktopView==null) return;
            Matrix panel; float height,width;
            lock(panelLock) { panel=panelPose; height=panelHeight; width=panelWidth; }
            var current=FloatingMenu.Current;
            if(current!=null) { panel=current.Pose; height=current.Height; width=current.Width; }
            if(height<=0) return;
            Matrix view=Matrix.Invert(OpenVR.System.GetEyeToHeadTransform(eye).ToMatrix()*Player.Headset.renderPose.deviceToAbsolute.matrix);
            float l=0,r=0,t=0,b=0; OpenVR.System.GetProjectionRaw(eye,ref l,ref r,ref t,ref b);
            var projection=VrMath.Projection(l,r,t,b,.03);
            if(!MenuHands.Available) return;
            FloatingMenu.DrawPanel(target,desktopView,new WindowFrame.Snapshot {Pose=panel,Width=width,Height=height,Hover=current?.Hover ?? 0},view,projection,handDepth);
        }
        public static void SubmitMenuBackground()
        {
            if (MenuHands.Submit()) return;
            if (blackTexture == null)
            {
                blackTexture=new Texture2D(MyRender11.DeviceInstance,new Texture2DDescription {
                    Width=16,Height=16,MipLevels=1,ArraySize=1,Format=Format.R8G8B8A8_UNorm,
                    SampleDescription=new SampleDescription(1,0),Usage=ResourceUsage.Default,
                    BindFlags=BindFlags.RenderTarget|BindFlags.ShaderResource });
                using (var target=new RenderTargetView(MyRender11.DeviceInstance,blackTexture))
                    MyRender11.DeviceInstance.ImmediateContext.ClearRenderTargetView(target,new RawColor4(0,0,0,1));
            }
            var texture=Texture(blackTexture); var bounds=FullBounds;
            foreach (EVREye eye in new[] { EVREye.Eye_Left,EVREye.Eye_Right })
            {
                var error=OpenVR.Compositor.Submit(eye,ref texture,ref bounds,EVRSubmitFlags.Submit_Default);
                if (error != EVRCompositorError.None && error != EVRCompositorError.DoNotHaveFocus)
                    throw new InvalidOperationException("Menu background: "+error);
            }
        }
        public static void Hide()
        {
            if (overlayHandle != 0 && visible) Check(OpenVR.Overlay.HideOverlay(overlayHandle));
            visible=false;
            lock(panelLock) panelHeight=0;
        }
    }
}
