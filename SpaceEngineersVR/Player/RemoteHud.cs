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
        [ThreadStatic] private static int collecting;
        [ThreadStatic] private static int markerDrawing;
        // Drops desktop sprites from native HUD passes run only for their state.
        [ThreadStatic] internal static int Discarding;
        // Sends every native sprite on this thread to a named offscreen queue.
        [ThreadStatic] internal static string Redirect;
        [ThreadStatic] private static int markerMode;
        private static readonly FieldInfo signalMode=AccessTools.Field(typeof(Sandbox.Game.GUI.HudViewers.MyHudMarkerRender),"<SignalDisplayMode>k__BackingField");
        internal static Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.SignalMode GlobalSignalMode => (Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.SignalMode)signalMode.GetValue(null);
        private static readonly TextureCopy hud=new TextureCopy();
        private static long source;
        internal static RemoteView.View Fixture { get; set; }
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
            var marker=typeof(Sandbox.Game.GUI.HudViewers.MyHudMarkerRender);
            harmony.Patch(AccessTools.Method(typeof(Sandbox.Game.Gui.MyGuiScreenHudBase),"DrawTexts"),prefix:new HarmonyMethod(typeof(RemoteHud),nameof(BeginMarkers)),finalizer:new HarmonyMethod(typeof(RemoteHud),nameof(EndMarkers)));
            harmony.Patch(AccessTools.PropertyGetter(marker,"SignalDisplayMode"),postfix:new HarmonyMethod(typeof(RemoteHud),nameof(SignalMode)));
            harmony.Patch(AccessTools.Method(typeof(Sandbox.Game.Gui.MyGuiScreenHudSpace),"AsyncUpdate"),prefix:new HarmonyMethod(typeof(RemoteHud),nameof(BeginCollection)),finalizer:new HarmonyMethod(typeof(RemoteHud),nameof(EndCollection)));
            harmony.Patch(AccessTools.Method(typeof(Sandbox.Game.Gui.MyGuiScreenHudSpace),"DrawAsync"),prefix:new HarmonyMethod(typeof(RemoteHud),nameof(BeginCollection)),finalizer:new HarmonyMethod(typeof(RemoteHud),nameof(EndCollection)));
            foreach(string property in new[] {"MinimalHud","IsHudMinimal"})
                harmony.Patch(AccessTools.PropertyGetter(typeof(Sandbox.Game.Gui.MyHud),property),postfix:new HarmonyMethod(typeof(RemoteHud),nameof(MinimalHud)));
            harmony.Patch(AccessTools.Method(marker,"Draw"),prefix:new HarmonyMethod(typeof(RemoteHud),nameof(BeginMarkers)),finalizer:new HarmonyMethod(typeof(RemoteHud),nameof(EndMarkers)));
            harmony.Patch(AccessTools.Method(marker,"DrawTargetIndicatorRender"),prefix:new HarmonyMethod(typeof(RemoteHud),nameof(Begin)),finalizer:new HarmonyMethod(typeof(RemoteHud),nameof(End)));
            harmony.Patch(AccessTools.Method(typeof(Sandbox.Graphics.GUI.MyGuiControlImage),"Draw"),prefix:new HarmonyMethod(typeof(RemoteHud),nameof(BeginHit)),finalizer:new HarmonyMethod(typeof(RemoteHud),nameof(End)));
            var main=renderer.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="RenderMainSprites" && m.GetParameters().Length==5);
            harmony.Patch(main,new HarmonyMethod(typeof(RemoteHud),nameof(Collect)));
        }
        private static bool CameraActive => Fixture!=null || Main.VrActive && !Main.MenuOpen && RemoteView.Current!=null;
        private static void BeginHit(Sandbox.Graphics.GUI.MyGuiControlImage __instance,out bool __state)
        {
            __state=CameraActive && ReferenceEquals(__instance,RemoteCombat.HitControl);
            if(__state) capturing++;
        }
        internal static void BeginCollection(out bool __state)
        { __state=CameraActive; if(__state) collecting++; }
        internal static void EndCollection(bool __state) { if(__state) collecting--; }
        internal static void BeginMarkers(out bool __state)
        { __state=CameraActive; if(__state) { if(markerDrawing==0) markerMode=(Fixture ?? RemoteView.Current)?.SignalMode ?? 0; capturing++; markerDrawing++; } }
        internal static void EndMarkers(bool __state) { if(__state) { capturing--; markerDrawing--; } }
        private static void MinimalHud(ref bool __result) { if(collecting>0) __result=false; }
        internal static void SignalMode(ref Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.SignalMode __result)
        {
            if(markerDrawing>0) __result=(Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.SignalMode)markerMode;
            else if(collecting>0) __result=Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.SignalMode.FullDisplay;
        }
        private static readonly Rectangle drawMark=new Rectangle(0,0,1,1);
        private static void HudFrame()
        {
            if(!Main.VrActive || Main.MenuOpen || RemoteView.Current==null) return;
            // Marks a message frame that ran the full HUD draw; an empty one still clears old sprites.
            MyRenderProxy.SpriteScissorPush(drawMark,queue);
            MyRenderProxy.SpriteScissorPop(queue);
        }
        // Some message frames carry only part of the HUD (no camera overlay, vignette or text), many per second when controlling a
        // turret on foot; those keep the last full image instead of replacing it.
        private static bool FullDraw(System.Collections.IEnumerable list) => list.Cast<object>().Any(m=>m is MyRenderMessageSpriteScissorPush push && push.ScreenRectangle==drawMark);
        private static void Begin(out bool __state)
        {
            __state=Fixture!=null || Main.VrActive && !Main.MenuOpen && RemoteView.Current!=null;
            if(__state) capturing++;
        }
        private static void End(bool __state) { if(__state) capturing--; }
        private static bool Route(ref string targetTexture)
        {
            if(Redirect!=null) { targetTexture=Redirect; return true; }
            // Capture only the selected native controls and camera marker renderer.
            if(Discarding>0 || markerDrawing>0 && markerMode==3) return false;
            if(capturing>0) targetTexture=queue;
            return true;
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
            object messages=AcquireSprites(queue);
            if(messages==null) return;
            try
            {
                if(remote==null) return;
                var list=CockpitRender.Member(messages,"Messages");
                if(Fixture==null && !FullDraw((System.Collections.IEnumerable)list)) return;
                SpriteCount=Convert.ToInt32(CockpitRender.Member(list,"Count"));
                if(Fixture!=null)
                    FixtureSprites=string.Join(",",((System.Collections.IEnumerable)list).Cast<object>().Select(m=>m.GetType().Name+":"+(AccessTools.Field(m.GetType(),"Texture")?.GetValue(m) ?? "")));
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
            finally { DisposeSprites(messages); }
        }
        internal static void Store(Texture2D rendered) => hud.Store(rendered);
        internal static object AcquireSprites(string name) => acquire.Invoke(manager.GetValue(null),new object[] {name});
        internal static void DisposeSprites(object messages) => dispose.Invoke(manager.GetValue(null),new[] {messages});
        public static void Composite(Texture2D target,RemoteView.View view)
        {
            if(hud.View==null || source==0 || source!=view?.Source) return;
            NativeSprites.Draw(target,new[] {new NativeSprite(null,new RectangleF(0,0,target.Description.Width,target.Description.Height),Vector4.One) { Texture=hud.View }});
        }
        private static void Release() { hud.Dispose(); source=0; SpriteCount=0; }
        public static void Reset() { Release(); RemoteCombat.Reset(); collectedFrame=int.MinValue; }
    }
}
