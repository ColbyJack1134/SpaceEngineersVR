using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI.Ingame;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    [HarmonyPatch(typeof(MyTextSurfaceScriptFactory),nameof(MyTextSurfaceScriptFactory.LoadScripts))]
    internal static class TouchAppRegistration
    {
        private static void Postfix()
        {
            if(Plugin.Common.Config?.DeveloperTools==true)
                MyTextSurfaceScriptFactory.Instance.RegisterFromAssembly(typeof(TouchAppRegistration).Assembly);
        }
    }

    // These use the ordinary LCD Script selector and native sprite frames. The
    // optional mod supplies its existing screen/PB interface; we only add VR input.
    public abstract class VrTouchApp : MyTSSCommon
    {
        private readonly IMyTextSurface surface;
        private readonly IMyCubeBlock block;
        private object touchScreen;
        private EventInfo updateEvent;
        private readonly Action update;
        private Vector2 click;
        private bool pending;
        protected abstract string Title { get; }
        protected abstract void Press(Vector2 uv);
        protected abstract void Content(MySpriteDrawFrame frame);
        public override ScriptUpdate NeedsUpdate => ScriptUpdate.Update10;
        protected VrTouchApp(IMyTextSurface surface,IMyCubeBlock block,Vector2 size) : base(surface,block,size)
        { this.surface=surface; this.block=block; update=Input; }
        private void Input()
        {
            // Sample at simulation frequency; the LCD refreshes less frequently.
            if(TouchScreenBridge.Tap(touchScreen,out var point))
            { click=(point-(surface.TextureSize-surface.SurfaceSize)*.5f)/surface.SurfaceSize; pending=true; }
        }
        public override void Run()
        {
            base.Run();
            if(touchScreen==null)
            {
                touchScreen=TouchScreenBridge.Create(surface,block);
                if(touchScreen!=null)
                {
                    updateEvent=touchScreen.GetType().GetEvent("UpdateAtSimulationEvent");
                    updateEvent?.AddEventHandler(touchScreen,update);
                }
            }
            if(pending) { pending=false; Press(click); }
            using(var frame=surface.DrawFrame())
            {
                Rect(frame,new Vector2(.5f),Vector2.One,surface.ScriptBackgroundColor);
                Label(frame,Title,new Vector2(.5f,.08f),.60f);
                Content(frame);
                if(touchScreen==null) Label(frame,"Enable TouchScreenAPI to interact",new Vector2(.5f,.89f),.36f);
                else Label(frame,"Touch or point + trigger",new Vector2(.5f,.93f),.34f);
            }
        }
        protected void Label(MySpriteDrawFrame frame,string text,Vector2 position,float scale)
        {
            var sprite=MySprite.CreateText(text,"Debug",surface.ScriptForegroundColor,scale*Math.Min(surface.SurfaceSize.X,surface.SurfaceSize.Y)/512,TextAlignment.CENTER);
            sprite.Position=(surface.TextureSize-surface.SurfaceSize)*.5f+position*surface.SurfaceSize;
            frame.Add(sprite);
        }
        protected void Rect(MySpriteDrawFrame frame,Vector2 centre,Vector2 size,Color color)
        {
            frame.Add(new MySprite(SpriteType.TEXTURE,"SquareSimple",(surface.TextureSize-surface.SurfaceSize)*.5f+centre*surface.SurfaceSize,size*surface.SurfaceSize,color));
        }
        protected void Button(MySpriteDrawFrame frame,string text,float x)
        {
            Rect(frame,new Vector2(x,.72f),new Vector2(.40f,.18f),new Color(43,75,86));
            Label(frame,text,new Vector2(x,.685f),.55f);
        }
        public override void Dispose()
        {
            if(touchScreen!=null)
            {
                updateEvent?.RemoveEventHandler(touchScreen,update);
                TouchScreenBridge.Remove(surface,block); touchScreen=null;
            }
            base.Dispose();
        }
    }

    [MyTextSurfaceScript("SEVR_TouchTest","VR Touch Test")]
    public sealed class VrTouchTest : VrTouchApp
    {
        private int taps;
        private Vector2 last;
        protected override string Title => "TOUCH TEST";
        public VrTouchTest(IMyTextSurface surface,IMyCubeBlock block,Vector2 size) : base(surface,block,size) { }
        protected override void Press(Vector2 uv) { if(uv.Y>.62f && uv.Y<.82f && uv.X>.52f) taps=0; else { taps++; last=uv; } }
        protected override void Content(MySpriteDrawFrame frame)
        {
            Label(frame,taps+" taps",new Vector2(.5f,.31f),1.0f);
            Label(frame,"X "+last.X.ToString("0.00")+"   Y "+last.Y.ToString("0.00"),new Vector2(.5f,.47f),.55f);
            Button(frame,"TAP",.26f); Button(frame,"RESET",.74f);
        }
    }

    [MyTextSurfaceScript("SEVR_Stopwatch","VR Stopwatch")]
    public sealed class VrStopwatch : VrTouchApp
    {
        private readonly Stopwatch watch=new Stopwatch();
        protected override string Title => "STOPWATCH";
        public VrStopwatch(IMyTextSurface surface,IMyCubeBlock block,Vector2 size) : base(surface,block,size) { }
        protected override void Press(Vector2 uv)
        {
            if(uv.Y<.62f || uv.Y>.82f) return;
            if(uv.X>=.06f && uv.X<=.46f) { if(watch.IsRunning) watch.Stop(); else watch.Start(); }
            else if(uv.X>=.54f && uv.X<=.94f) watch.Reset();
        }
        protected override void Content(MySpriteDrawFrame frame)
        {
            Label(frame,watch.Elapsed.ToString(@"hh\:mm\:ss"),new Vector2(.5f,.34f),1.05f);
            Button(frame,watch.IsRunning ? "PAUSE" : "START",.26f); Button(frame,"RESET",.74f);
        }
    }
}
