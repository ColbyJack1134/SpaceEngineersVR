using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    // Renders camera block views into LCD textures, one screen per refresh in turn.
    internal static class LcdFeed
    {
        private static readonly FieldInfo fileTextures=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"FileTextures");
        private static readonly Type userTexture=AccessTools.TypeByName("VRage.Render11.Resources.IUserGeneratedTexture");
        private static readonly MethodInfo tryGet=fileTextures==null || userTexture==null ? null : fileTextures.FieldType.GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .FirstOrDefault(m=>m.Name=="TryGetTexture" && m.GetParameters().Length==2 && m.GetParameters()[1].ParameterType==userTexture.MakeByRefType());
        private static int next,wait;
        private static bool unavailable;
        internal static Vector2I Resolution => Size(Common.Config?.LcdCameraScale ?? Config.PluginConfig.DefaultLcdCameraScale);
        // One square size for every screen keeps a single set of scene resources; the copy stretches it to the screen's aspect.
        internal static Vector2I Size(float scale)
        {
            int side=Math.Max(64,(int)Math.Round(1024*scale/16)*16);
            return new Vector2I(side,side);
        }
        public static void Render(LcdCameras.View[] displays)
        {
            if(displays==null || displays.Length==0) { Reset(); return; }
            // Screens keep their last picture behind menus, the terminal included.
            if(Main.MenuOpen || --wait>0) return;
            wait=Common.Config?.LcdCameraInterval ?? Config.PluginConfig.DefaultLcdCameraInterval;
            next=(next+1)%displays.Length;
            var display=displays[next];
            if(!TryTarget(display.Texture,out var target,out var mips)) return;
            var size=Resolution;
            var rendered=MyManagers.RwTexturesPool.BorrowRtv("SEVR.Lcd",size.X,size.Y,Format.R8G8B8A8_UNorm_SRgb);
            var matrices=MyRender11.Environment_Matrices;
            var snapshot=matrices.Capture();
            var scene=SceneCamera.Current();
            var position=scene.Position;
            object ambient=null;
            long started=FeatureTiming.Start();
            GpuTiming.Begin(GpuTiming.Area.LcdCamera);
            try
            {
                using(var resources=MyRender11.Resolution==size ? null:new EyeResolution.Scene(size,EyeResolution.Scene.Lcd))
                using(var exposure=new RemoteExposure(RemoteExposure.Lcd))
                using(var isolated=new RemoteScene(FeedShadows.Lcd,display.Character,display.FirstPersonMaterials))
                {
                    var world=MatrixD.Invert(display.Camera);
                    float aspect=display.SurfaceSize.Y>0 ? display.SurfaceSize.X/display.SurfaceSize.Y : 1,tangent=(float)Math.Tan(display.Fov/2);
                    matrices.SetCamera(display.Camera,world,-tangent*aspect,tangent*aspect,-tangent,tangent,Math.Max(.03,matrices.NearClipping));
                    scene.Position=world.Translation;
                    MyRender11.DrawGameScene(rendered,out ambient);
                }
                Copy((Texture2D)rendered.GetResource(),target,mips,display);
            }
            finally
            {
                matrices.Restore(snapshot);
                scene.Position=position;
                if(ambient!=null) new BorrowedRtvTexture(ambient).Release();
                rendered.Release();
                FeatureTiming.End(FeatureTiming.Area.LcdCamera,started);
                GpuTiming.End(GpuTiming.Area.LcdCamera);
            }
        }
        // Alpha stays at the panel's background alpha like native content: screen materials read it as metalness.
        private static void Copy(Texture2D source,Texture2D target,ShaderResourceView mips,LcdCameras.View display)
        {
            var texture=target.Description;
            var surface=Vector2.Min(display.SurfaceSize,new Vector2(texture.Width,texture.Height));
            using(var view=new ShaderResourceView(source.Device,source))
                NativeSprites.Draw(target,new[] {new NativeSprite(null,new RectangleF((texture.Width-surface.X)/2,(texture.Height-surface.Y)/2,surface.X,surface.Y),new Vector4(1,1,1,display.Alpha))
                    { Texture=view,Replace=true,EncodeSrgb=texture.Format!=Format.R8G8B8A8_UNorm_SRgb }});
            if(texture.MipLevels>1) target.Device.ImmediateContext.GenerateMips(mips);
        }
        private static bool TryTarget(string name,out Texture2D texture,out ShaderResourceView mips)
        {
            texture=null; mips=null;
            if(unavailable || name==null) return false;
            try
            {
                if(tryGet==null) throw new MissingMethodException("MyFileTextureManager","TryGetTexture");
                var arguments=new object[] {name,null};
                if(!(bool)tryGet.Invoke(fileTextures.GetValue(null),arguments)) return false;
                var generated=arguments[1];
                // The panel's first sprite render creates the GPU texture and marks it loaded.
                if(!(bool)CockpitRender.Member(generated,"IsLoaded") || CockpitRender.Member(generated,"Rtv")==null) return false;
                texture=CockpitRender.Member(generated,"Resource") as Texture2D;
                mips=CockpitRender.Member(generated,"Srv") as ShaderResourceView;
                return texture!=null && mips!=null;
            }
            catch(Exception ex) { unavailable=true; Logger.Warning(ex,"LCD camera textures unavailable; camera displays disabled"); return false; }
        }
        public static void Reset()
        {
            wait=0;
            FeedShadows.Lcd.Release();
            RemoteExposure.Reset(RemoteExposure.Lcd);
        }
    }
}
