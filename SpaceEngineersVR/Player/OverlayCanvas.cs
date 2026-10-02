using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Wrappers;
using Valve.VR;
using VRageMath;
using Device = SharpDX.Direct3D11.Device;

namespace SpaceEngineersVR.Player
{
    // Render-thread owner for small, infrequently updated HUD and selector textures.
    internal sealed class OverlayCanvas : IDisposable
    {
        private readonly ulong handle;
        private readonly Device device;
        private readonly Bitmap bitmap;
        private readonly Texture2D texture;
        internal Texture2D Texture => texture;
        internal int Width => bitmap.Width;
        internal int Height => bitmap.Height;
        private bool visible;
        public readonly Graphics Graphics;
        private readonly List<NativeSprite> icons = new List<NativeSprite>();
        public void Clear(System.Drawing.Color color) { Graphics.Clear(color); icons.Clear(); }
        internal void Sprite(NativeSprite sprite) => icons.Add(sprite);
        public void Icon(string path, float x, float y, float size, bool enabled = true)
        { icons.Add(new NativeSprite(path,new VRageMath.RectangleF(x,y,size,size),enabled ? Vector4.One : new Vector4(0.4f,0.4f,0.4f,0.8f))); }
        public void Icon(string path,float x,float y,float size,System.Drawing.Color tint)
        { icons.Add(new NativeSprite(path,new VRageMath.RectangleF(x,y,size,size),new Vector4(tint.R/255f,tint.G/255f,tint.B/255f,tint.A/255f))); }
        public void Icon(string path,float x,float y,float width,float height,Vector4 uv,System.Drawing.Color tint)
        { icons.Add(new NativeSprite(path,new VRageMath.RectangleF(x,y,width,height),new Vector4(tint.R/255f,tint.G/255f,tint.B/255f,tint.A/255f)) { UV=uv }); }

        internal void DrawAt(float x,float y,float sx,float sy,Action paint)
        {
            var state=Graphics.Save(); int first=icons.Count;
            try
            {
                Graphics.TranslateTransform(x,y); Graphics.ScaleTransform(sx,sy); paint();
                for(int i=first;i<icons.Count;i++)
                {
                    var icon=icons[i]; var b=icon.Bounds;
                    icon.Bounds=new VRageMath.RectangleF(x+b.X*sx,y+b.Y*sy,b.Width*sx,b.Height*sy); icons[i]=icon;
                }
            }
            finally { Graphics.Restore(state); }
        }

        public OverlayCanvas(string name, int width, int height, float metres, bool overlay = true, Device device = null, bool mipMaps = false)
        {
            this.device = device ?? MyRender11.DeviceInstance;
            if (overlay)
            {
                Check(OpenVR.Overlay.CreateOverlay("sevr." + name, name, ref handle));
                Check(OpenVR.Overlay.SetOverlayWidthInMeters(handle, metres));
            }
            bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Graphics = Graphics.FromImage(bitmap);
            Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            texture = new Texture2D(this.device, new Texture2DDescription {
                Width = width, Height = height, MipLevels = mipMaps ? 0 : 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
                OptionFlags = mipMaps ? ResourceOptionFlags.GenerateMipMaps : ResourceOptionFlags.None });
        }

        public void Position(Matrix pose, bool headRelative)
        {
            var transform = new HmdMatrix34_t {
                m0=pose.M11,m1=pose.M21,m2=pose.M31,m3=pose.M41,
                m4=pose.M12,m5=pose.M22,m6=pose.M32,m7=pose.M42,
                m8=pose.M13,m9=pose.M23,m10=pose.M33,m11=pose.M43 };
            Check(headRelative ? OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(handle, OpenVR.k_unTrackedDeviceIndex_Hmd, ref transform)
                : OpenVR.Overlay.SetOverlayTransformAbsolute(handle, ETrackingUniverseOrigin.TrackingUniverseStanding, ref transform));
        }

        public void Upload()
        {
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { device.ImmediateContext.UpdateSubresource(new DataBox(data.Scan0, data.Stride, 0), texture); }
            finally { bitmap.UnlockBits(data); }
            NativeSprites.Draw(texture,icons);
            if (handle == 0) return;
            var input = new Texture_t { eColorSpace = EColorSpace.Gamma, eType = ETextureType.DirectX, handle = texture.NativePointer };
            Check(OpenVR.Overlay.SetOverlayTexture(handle, ref input));
            Check(OpenVR.Overlay.ShowOverlay(handle));
            visible = true;
        }

        internal void Show(float alpha=1)
        {
            if(handle==0) return;
            Check(OpenVR.Overlay.SetOverlayAlpha(handle,alpha));
            if(!visible) Check(OpenVR.Overlay.ShowOverlay(handle));
            visible=true;
        }

        public void Hide()
        {
            if (visible) Check(OpenVR.Overlay.HideOverlay(handle));
            visible = false;
        }
        private static void Check(EVROverlayError result)
        {
            if (result != EVROverlayError.None) throw new InvalidOperationException("VR canvas: " + result);
        }
        public void Dispose()
        {
            if (handle != 0) OpenVR.Overlay.DestroyOverlay(handle);
            texture.Dispose(); Graphics.Dispose(); bitmap.Dispose();
        }
    }
}
