using System;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using Valve.VR;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Player
{
    internal static class EyeResolution
    {
        public static Vector2I Recommended { get; private set; }
        public static Vector2I Current { get; private set; }
        private static Vector2I pending;
        private static DateTime queried,changed;
        public static void Recommend(uint width,uint height) => Recommended=new Vector2I((int)width,(int)height);
        internal static Vector2I Size(Vector2I recommended,float scale)
        {
            if(recommended.X<=0 || recommended.Y<=0) throw new InvalidOperationException("SteamVR returned an invalid eye target size");
            double factor=float.IsNaN(scale) || float.IsInfinity(scale) ? 1 : MathHelper.Clamp(scale,.5f,1.5f);
            factor=Math.Min(factor,Math.Min(8192d/Math.Max(recommended.X,recommended.Y),
                Math.Sqrt(33554432d/((double)recommended.X*recommended.Y))));
            return new Vector2I(Math.Max(64,(int)Math.Round(recommended.X*factor)),Math.Max(64,(int)Math.Round(recommended.Y*factor)));
        }
        public static Vector2I Update()
        {
            var now=DateTime.UtcNow;
            if((now-queried).TotalSeconds>=2)
            {
                uint width=0,height=0; OpenVR.System.GetRecommendedRenderTargetSize(ref width,ref height);
                if(width>0 && height>0) Recommend(width,height);
                queried=now;
            }
            var requested=Size(Recommended,Common.Config.EyeRenderScale);
            if(requested!=pending) { pending=requested; changed=now; }
            if(Current.X==0 || (now-changed).TotalSeconds>=.5) Current=pending;
            return Current;
        }

        // Retain an eye G-buffer beside the native desktop buffer. Lighting/AO
        // resources stay at eye size until the next resize or desktop fallback.
        internal sealed class Scene : IDisposable
        {
            private static readonly Type renderer=AccessTools.TypeByName("VRageRender.MyRender11");
            private static readonly PropertyInfo viewport=AccessTools.Property(renderer,"ViewportResolution");
            private static readonly FieldInfo fullViewport=AccessTools.Field(renderer,"FullResViewport");
            private static readonly FieldInfo mainBuffer=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main");
            private static object retained;
            private static object desktop;
            private static bool nativeResourcesChanged;
            private static MyAntialiasingMode retainedAntialiasing;
            private static readonly MethodInfo release=AccessTools.Method(mainBuffer.FieldType,"Release");
            private static Vector2I retainedSize;
            public static int Allocations { get; private set; }
            private readonly Vector2I resolution;
            private readonly object oldViewport,oldFullViewport;
            private readonly object oldBuffer;
            private readonly bool drs;
            private bool disposed;
            public Scene(Vector2I size)
            {
                resolution=MyRender11.Resolution;
                oldViewport=viewport.GetValue(null); oldFullViewport=fullViewport.GetValue(null);
                oldBuffer=mainBuffer.GetValue(null);
                var settings=MyRender11.Settings; drs=settings.User.DRScaling;
                settings.User.DRScaling=false; MyRender11.Settings=settings;
                try
                {
                    MyRender11.Resolution=size;
                    if(retained==null || !ReferenceEquals(desktop,oldBuffer) || retainedSize!=size)
                    {
                        ReleaseRetained();
                        mainBuffer.SetValue(null,null);
                        nativeResourcesChanged=true;
                        retainedAntialiasing=settings.User.AntialiasingMode;
                        MyRender11.CreateScreenResources();
                        retained=mainBuffer.GetValue(null); desktop=oldBuffer; retainedSize=size; Allocations++;
                        Logger.Info("VR scene resources: "+size.X+"x"+size.Y+"; desktop "+resolution.X+"x"+resolution.Y);
                    }
                    else mainBuffer.SetValue(null,retained);
                    viewport.SetValue(null,size);
                    fullViewport.SetValue(null,new MyViewport(size));
                }
                catch
                {
                    if(retained==null) retained=mainBuffer.GetValue(null);
                    Dispose(); RestoreNative(); throw;
                }
            }
            public void Dispose()
            {
                if(disposed) return; disposed=true;
                MyRender11.Resolution=resolution;
                mainBuffer.SetValue(null,oldBuffer);
                viewport.SetValue(null,oldViewport); fullViewport.SetValue(null,oldFullViewport);
                var settings=MyRender11.Settings; settings.User.DRScaling=drs; MyRender11.Settings=settings;
            }
            public static void RestoreNative()
            {
                if(!nativeResourcesChanged) return;
                ReleaseRetained();
                desktop=null; retainedSize=Vector2I.Zero;
                MyRender11.CreateScreenResources();
                nativeResourcesChanged=false;
            }
            private static void ReleaseRetained()
            {
                if(retained==null) return;
                var buffer=retained; retained=null;
                var settings=MyRender11.Settings;
                var antialiasing=settings.User.AntialiasingMode;
                // Native Release uses the global AA mode to distinguish aliased depth buffers.
                settings.User.AntialiasingMode=retainedAntialiasing; MyRender11.Settings=settings;
                try { release.Invoke(buffer,null); }
                finally { settings.User.AntialiasingMode=antialiasing; MyRender11.Settings=settings; }
            }
        }

        internal static RectangleF MirrorBounds(Vector2I source,Vector2I target,bool preserveAspect)
        {
            float scale=preserveAspect ? Math.Min((float)target.X/source.X,(float)target.Y/source.Y) : 0;
            float width=preserveAspect ? source.X*scale : target.X,height=preserveAspect ? source.Y*scale : target.Y;
            return new RectangleF((target.X-width)*.5f,(target.Y-height)*.5f,width,height);
        }
        public static void Mirror(Texture2D source,Texture2D target,bool preserveAspect=true)
        {
            var a=source.Description; var b=target.Description;
            using(var rtv=new RenderTargetView(target.Device,target))
                target.Device.ImmediateContext.ClearRenderTargetView(rtv,new SharpDX.Mathematics.Interop.RawColor4(0,0,0,1));
            using(var texture=new ShaderResourceView(source.Device,source))
                NativeSprites.Draw(target,new[] {new NativeSprite(null,MirrorBounds(new Vector2I(a.Width,a.Height),new Vector2I(b.Width,b.Height),preserveAspect),Vector4.One)
                    { Texture=texture,Opaque=true,EncodeSrgb=b.Format!=SharpDX.DXGI.Format.R8G8B8A8_UNorm_SRgb }});
        }
    }
}
