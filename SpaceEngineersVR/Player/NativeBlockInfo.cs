using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Graphics;
using Sandbox.Graphics.GUI;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class NativeBlockInfo
    {
        private const string queue="SEVR.BlockInfo";
        internal const int TextureWidth=1296;
        private static MethodInfo offscreen,style;
        private static FieldInfo lossShown;
        private static readonly TextureCopy copy=new TextureCopy();
        private static MyGuiControlContextHelp help;
        private static MyGuiControlBlockInfo block;
        private static MyHudBlockInfo shown;
        private static bool pending;
        private static volatile bool awaiting;
        private static DateTime drawn;
        private static volatile Request request;
        private static string target;
        private static volatile int revision;
        internal static ShaderResourceView View => copy.View;
        internal static int Revision => revision;
        internal static string Captured { get; private set; }
#if SEVR_DIAGNOSTICS
        internal static Action<Texture2D> Saved;
#endif
        private static volatile bool available;
        internal static bool Available { get => available; private set => available=value; }
        private sealed class Request { public Vector2 Area; public string Target; }
        internal static void Install(Harmony harmony)
        {
            var renderer=AccessTools.TypeByName("VRageRender.MyRender11");
            // Viewport-scaled overload: desktop GUI sprites fill a larger target.
            offscreen=renderer.GetMethods(BindingFlags.Static|BindingFlags.Public).Single(m=>m.Name=="DrawSpritesOffscreen" && m.GetParameters().Length==6);
            style=AccessTools.Method(typeof(MyGuiScreenHudSpace),"GetDefaultStyle") ?? throw new MissingMethodException("MyGuiScreenHudSpace.GetDefaultStyle");
            lossShown=AccessTools.Field(typeof(MyHudBlockInfo),"m_componentLossNotificationAdded") ?? throw new MissingFieldException("MyHudBlockInfo.m_componentLossNotificationAdded");
            // The style comes from an uninitialized screen; fail at install rather than in the HUD draw.
            Style();
            harmony.Patch(AccessTools.Method(typeof(MyGuiScreenHudSpace),"Draw"),new HarmonyMethod(typeof(NativeBlockInfo),nameof(Draw)));
            var main=renderer.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="RenderMainSprites" && m.GetParameters().Length==5);
            harmony.Patch(main,new HarmonyMethod(typeof(NativeBlockInfo),nameof(Collect)));
            Available=true;
        }
        internal static MyGuiControlBlockInfo.MyControlBlockInfoStyle Style() =>
            (MyGuiControlBlockInfo.MyControlBlockInfoStyle)style.Invoke(FormatterServices.GetUninitializedObject(typeof(MyGuiScreenHudSpace)),null);
        internal static void Show(MyHudBlockInfo info,string key) { shown=info; target=key; pending=true; }
        internal static void Hide() { shown=null; pending=false; }
        // Title-only context help plus MyGuiControlBlockInfo.RecalculateSize, in 1080p GUI pixels.
        internal static Vector2 Pixels(int components) =>
            new Vector2(.225f*1440,(components>0 ? .1f+.006f+.12f+.037f*(components-2):.1f)*1080);
        private static void Disable(Exception ex)
        {
            if(!Available) return;
            Available=false; shown=null; pending=false; awaiting=false;
            Logger.Warning(ex,"Native block info failed; grip block inspection disabled");
        }
        internal static void Draw()
        {
            if(!pending || shown==null) return;
            try { DrawCapture(); }
            catch(Exception ex) { Disable(ex); }
        }
        private static void DrawCapture()
        {
            // One capture in flight keeps sprites and request paired; the timeout covers a dropped frame.
            if(awaiting && DateTime.UtcNow<drawn.AddMilliseconds(500)) return;
            pending=false;
            // MyHudBlockInfo.Version only compares within one instance.
            if(help==null || help.BlockInfo!=shown)
            {
                var defaults=Style();
                help=new MyGuiControlContextHelp(defaults) {ShowJustTitle=true,OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP};
                block=new MyGuiControlBlockInfo(defaults) {OriginAlign=MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP};
            }
            bool components=shown.Components.Count>0;
            help.BlockInfo=shown; help.ShowBuildInfo=components;
            block.BlockInfo=components ? shown:null;
            var origin=MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(Vector2.Zero);
            help.Position=origin;
            block.Position=origin+new Vector2(0,help.Size.Y+.006f);
            var pixels=Pixels(shown.Components.Count);
            request=new Request {Area=MyGuiManager.GetScreenSizeFromNormalizedSize(new Vector2(.225f,pixels.Y/1080)),Target=target};
            // The desktop HUD owns the shared loss notification; this copy must not add or remove it.
            lossShown.SetValue(shown,shown.ShowComponentLoss && shown.Components.Any(c=>c.DeconstructDegrades));
            awaiting=true; drawn=DateTime.UtcNow;
            RemoteHud.Redirect=queue;
            try
            {
                help.Draw(1,1);
                if(components) block.Draw(1,1);
            }
            finally { RemoteHud.Redirect=null; }
        }
        private static void Collect()
        {
            object messages=RemoteHud.AcquireSprites(queue);
            if(messages==null) return;
            try
            {
                var request=NativeBlockInfo.request; var area=request?.Area ?? Vector2.Zero;
                if(area.X<1 || area.Y<1) return;
                int width=TextureWidth,height=(int)Math.Ceiling(TextureWidth*area.Y/area.X);
                var borrowed=MyManagers.RwTexturesPool.BorrowRtv("SEVR.BlockInfo",width,height,Format.R8G8B8A8_UNorm_SRgb);
                try
                {
                    object scale=new Vector2(area.X/width,area.Y/height);
                    offscreen.Invoke(null,new[] {MyRender11.RawRC,borrowed.Instance,messages,scale,(RawColor4?)new RawColor4(0,0,0,0),null});
                    var texture=borrowed.GetResource();
                    copy.Store(texture);
                    Captured=request.Target;
#if SEVR_DIAGNOSTICS
                    Saved?.Invoke(texture);
#endif
                    revision++;
                    awaiting=false;
                }
                finally { borrowed.Release(); }
            }
            catch(Exception ex) { Disable(ex); }
            finally { RemoteHud.DisposeSprites(messages); }
        }
    }
}
