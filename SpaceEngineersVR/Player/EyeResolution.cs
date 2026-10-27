using System;
using System.Reflection;
using System.Linq;
using System.Threading;
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
        // The eye size at the configured scale; HUD pixel sizes follow it so dynamic resolution does not resize them.
        public static Vector2I Hud { get; private set; }
        private static Vector2I pending,pendingHud;
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
            var requested=Size(Recommended,BaseScale*DynamicResolution.Scale);
            var hud=Size(Recommended,BaseScale);
            if(requested!=pending || hud!=pendingHud) { pending=requested; pendingHud=hud; changed=now; }
            if(Current.X==0 || (now-changed).TotalSeconds>=.5) { Current=pending; Hud=pendingHud; }
            return Current;
        }

        // Dynamic resolution works down from SteamVR's size; the Headset resolution setting applies only while it is off.
        internal static float BaseScale => Common.Config.DynamicResolution ? 1:Common.Config.EyeRenderScale;
        internal static int HudWidth(int fallback) => Hud.X>0 ? Hud.X:fallback;
        // Converts a pixel size measured on this eye target into HUD pixels while drawing an eye.
        internal static float PixelScale(int targetWidth) => StereoRenderState.PhysicalEye && Hud.X>0 && targetWidth>0 ? (float)Hud.X/targetWidth:1;

        // Each scene size keeps its depth, lighting tiles and AO resources together.
        internal sealed class Scene : IDisposable
        {
            private static readonly Type renderer=AccessTools.TypeByName("VRageRender.MyRender11");
            private static readonly PropertyInfo viewport=AccessTools.Property(renderer,"ViewportResolution");
            private static readonly FieldInfo fullViewport=AccessTools.Field(renderer,"FullResViewport");
            private static readonly FieldInfo mainBuffer=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Resources.MyGBuffer"),"Main");
            private static readonly MethodInfo remove=AccessTools.Method(renderer,"RemoveScreenResources");
            private static readonly FieldInfo[] fields=new[] {mainBuffer}.Concat(
                new[] {"m_tileIndices","m_tilesNum","m_tilesX","m_tilesY","m_isLightPreparedAfterResize","m_lastFrameVisiblePointlights"}
                    .Select(n=>AccessTools.Field(AccessTools.TypeByName("VRage.Render11.LightingStage.MyLightsRendering"),n))).Concat(
                new[] {"m_fullResViewDepthTarget","m_fullResNormalTexture","m_fullResAOZTexture","m_fullResAOZTexture2","m_quarterResViewDepthTextureArray","m_quarterResAOTextureArray"}
                    .Select(n=>AccessTools.Field(AccessTools.TypeByName("VRageRender.MyHBAO"),n))).ToArray();
            private sealed class Resources
            {
                internal object[] Values;
                internal object Parent;
                internal Vector2I Size;
                internal MyAntialiasingMode Antialiasing;
            }
            private static readonly Resources[] retained=new Resources[2];
            public static int Allocations { get; private set; }
            private sealed class DesktopViewport
            {
                internal Vector2I Size;
                internal Thread RenderThread;
            }
            private static volatile DesktopViewport desktopViewport;
            internal static bool DesktopFor(Thread thread,out Vector2I size)
            {
                var desktop=desktopViewport;
                size=desktop?.Size ?? Vector2I.Zero;
                return desktop!=null && desktop.RenderThread!=thread;
            }
            private readonly Vector2I resolution;
            private readonly object oldViewport,oldFullViewport;
            private readonly bool publishesDesktop;
            private readonly object[] previous;
            private readonly bool drs;
            private readonly int slot;
            private bool disposed;
            private static object[] Capture() => fields.Select(f=>f.GetValue(null)).ToArray();
            private static void Restore(object[] values)
            {
                for(int i=0;i<fields.Length;i++) fields[i].SetValue(null,values[i]);
            }
            public Scene(Vector2I size,bool camera=false)
            {
                slot=camera ? 1:0;
                resolution=MyRender11.Resolution;
                oldViewport=viewport.GetValue(null); oldFullViewport=fullViewport.GetValue(null);
                previous=Capture();
                var settings=MyRender11.Settings; drs=settings.User.DRScaling;
                settings.User.DRScaling=false; MyRender11.Settings=settings;
                publishesDesktop=desktopViewport==null;
                if(publishesDesktop) desktopViewport=new DesktopViewport {Size=(Vector2I)oldViewport,RenderThread=Thread.CurrentThread};
                try
                {
                    MyRender11.Resolution=size;
                    var current=retained[slot];
                    if(current==null || !ReferenceEquals(current.Parent,previous[0]) || current.Size!=size || current.Antialiasing!=settings.User.AntialiasingMode)
                    {
                        var timer=System.Diagnostics.Stopwatch.StartNew();
                        Release(slot);
                        foreach(var field in fields) if(!field.FieldType.IsValueType) field.SetValue(null,null);
                        try { MyRender11.CreateScreenResources(); }
                        catch { remove.Invoke(null,null); throw; }
                        retained[slot]=new Resources {Values=Capture(),Parent=previous[0],Size=size,Antialiasing=settings.User.AntialiasingMode};
                        Allocations++;
                        Logger.Info("VR scene resources: "+size.X+"x"+size.Y+" in "+timer.Elapsed.TotalMilliseconds.ToString("F1")+" ms; desktop "+resolution.X+"x"+resolution.Y);
                    }
                    else Restore(current.Values);
                    viewport.SetValue(null,size);
                    fullViewport.SetValue(null,new MyViewport(size));
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if(disposed) return; disposed=true;
                if(retained[slot]!=null && ReferenceEquals(mainBuffer.GetValue(null),retained[slot].Values[0])) retained[slot].Values=Capture();
                MyRender11.Resolution=resolution;
                Restore(previous);
                viewport.SetValue(null,oldViewport); fullViewport.SetValue(null,oldFullViewport);
                if(publishesDesktop) desktopViewport=null;
                var settings=MyRender11.Settings; settings.User.DRScaling=drs; MyRender11.Settings=settings;
            }
            public static void RestoreNative() { Release(0); Release(1); }
            private static void Release(int index)
            {
                var resources=retained[index];
                if(resources==null) return;
                retained[index]=null;
                var previous=Capture(); var settings=MyRender11.Settings;
                var antialiasing=settings.User.AntialiasingMode;
                settings.User.AntialiasingMode=resources.Antialiasing; MyRender11.Settings=settings;
                try { Restore(resources.Values); remove.Invoke(null,null); }
                finally { Restore(previous); settings.User.AntialiasingMode=antialiasing; MyRender11.Settings=settings; }
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
