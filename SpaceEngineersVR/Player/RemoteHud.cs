using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class RemoteHud
    {
        private const string queue="SEVR.CameraHud";
        [ThreadStatic] private static int capturing;
        private static readonly TextureCopy hud=new TextureCopy();
        private static long source;
        internal static RemoteView.View Fixture;
        internal static int SpriteCount { get; private set; }
        internal static string FixtureSprites;
        private static readonly Type renderer=AccessTools.TypeByName("VRageRender.MyRender11");
        private static readonly FieldInfo frameCounter=AccessTools.Field(renderer,"m_messageFrameCounter");
        private static int collectedFrame=int.MinValue;
        private static readonly Type managerType=AccessTools.TypeByName("VRage.Render11.Sprites.MySpritesManager");
        private static readonly FieldInfo manager=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"SpritesManager");
        private static readonly MethodInfo acquire=AccessTools.Method(managerType,"AcquireDrawMessages"),dispose=AccessTools.Method(managerType,"DisposeDrawMessages");
        private static readonly MethodInfo draw=renderer.GetMethods(BindingFlags.Static|BindingFlags.Public).Single(m=>m.Name=="DrawSpritesOffscreen" && m.GetParameters().Length==7);
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(Sandbox.Game.Gui.MyGuiScreenHudBase),"Draw"),new HarmonyMethod(typeof(RemoteHud),nameof(HudFrame)));
            foreach(string method in new[] {"DrawSprite","DrawSpriteExt","DrawSpriteAtlas","SpriteScissorPop","SpriteScissorPush","DrawString","DrawStringAligned"})
                harmony.Patch(AccessTools.Method(typeof(MyRenderProxy),method),new HarmonyMethod(typeof(RemoteHud),nameof(Route)));
            foreach(string type in new[] {"Sandbox.Game.GUI.MyHudCameraOverlay","Sandbox.Game.Gui.MyHudCrosshair","Sandbox.Game.Gui.MyHudTargetingMarkers"})
                harmony.Patch(AccessTools.Method(AccessTools.TypeByName(type),"Draw"),new HarmonyMethod(typeof(RemoteHud),nameof(Begin)),finalizer:new HarmonyMethod(typeof(RemoteHud),nameof(End)));
            var main=renderer.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="RenderMainSprites" && m.GetParameters().Length==5);
            harmony.Patch(main,new HarmonyMethod(typeof(RemoteHud),nameof(Collect)));
        }
        private static void HudFrame()
        {
            if(!Main.VrActive || Main.MenuOpen || RemoteView.Current==null) return;
            // An explicit empty HUD draw must clear old sprites; a camera-only batch must not.
            MyRenderProxy.SpriteScissorPush(new Rectangle(0,0,1,1),queue);
            MyRenderProxy.SpriteScissorPop(queue);
        }
        private static void Begin(out bool __state)
        {
            __state=Fixture!=null || Main.VrActive && !Main.MenuOpen && RemoteView.Current!=null;
            if(__state) capturing++;
        }
        private static void End(bool __state) { if(__state) capturing--; }
        private static void Route(ref string targetTexture)
        {
            // Only the game's camera filter, crosshair and targeting controls enter this queue.
            if(capturing>0) targetTexture=queue;
        }
        private static void Collect(object[] __args)
        {
            int frame=(int)frameCounter.GetValue(null);
            var remote=Fixture ?? (Main.VrActive && !Main.MenuOpen ? RenderFrameBridge.Remote:null);
            CollectFrame(frame,remote,(Vector2)__args[3]);
        }
        internal static void CollectFrame(int frame,RemoteView.View remote,Vector2 size)
        {
            if(frame==collectedFrame) return;
            // Screenshots may draw sprites again after the same message frame was consumed.
            collectedFrame=frame;
            if(remote==null || source!=remote.Source) Release();
            object sprites=manager.GetValue(null),messages=acquire.Invoke(sprites,new object[] {queue});
            if(messages==null) return;
            try
            {
                if(remote==null) return;
                SpriteCount=Convert.ToInt32(CockpitRender.Member(CockpitRender.Member(messages,"Messages"),"Count"));
                if(Fixture!=null)
                    FixtureSprites=string.Join(",",((System.Collections.IEnumerable)CockpitRender.Member(messages,"Messages")).Cast<object>().Select(m=>m.GetType().Name+":"+(AccessTools.Field(m.GetType(),"Texture")?.GetValue(m) ?? "")));
                var rendered=draw.Invoke(null,new object[] {messages,queue,(int)size.X,(int)size.Y,Format.R8G8B8A8_UNorm_SRgb,new RawColor4(0,0,0,0),null});
                if(rendered!=null)
                {
                    var borrowed=new BorrowedRtvTexture(rendered);
                    try { Store(borrowed.GetResource()); }
                    finally { borrowed.Release(); }
                }
                else Release();
                source=remote.Source;
            }
            finally { dispose.Invoke(sprites,new[] {messages}); }
        }
        internal static void Store(Texture2D rendered) => hud.Store(rendered);
        public static void Composite(Texture2D target,RemoteView.View view)
        {
            if(hud.View==null || source==0 || source!=view?.Source) return;
            NativeSprites.Draw(target,new[] {new NativeSprite(null,new RectangleF(0,0,target.Description.Width,target.Description.Height),Vector4.One) { Texture=hud.View }});
        }
        private static void Release() { hud.Dispose(); source=0; SpriteCount=0; }
        public static void Reset() { Release(); collectedFrame=int.MinValue; }
    }
}
