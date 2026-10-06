using System;
using System.Linq;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DesktopTests
    {
        private static void Require(bool value,string label) { if(!value) throw new Exception(label); }
        public static void Run(Action<string> log)
        {
            var game=new IntPtr(11);
            var one=new DesktopCapture.Monitor {Name=@"\\.\DISPLAY1",Number=1,Handle=game};
            var two=new DesktopCapture.Monitor {Name=@"\\.\DISPLAY2",Number=2,Handle=new IntPtr(22)};
            var list=new[] {one,two};
            Require(DesktopCapture.Choose(list,null,game)==two,"Automatic choice mirrored the game's monitor");
            Require(DesktopCapture.Choose(list,one.Name,game)==one,"Remembered monitor ignored");
            Require(DesktopCapture.Choose(list,@"\\.\DISPLAY9",game)==two,"Unplugged remembered monitor did not fall back to automatic");
            Require(DesktopCapture.Choose(new[] {one},null,game)==one && DesktopCapture.Choose(new DesktopCapture.Monitor[0],null,game)==null,"Single or missing monitor choice");
            Require(DesktopCapture.Number(@"\\.\DISPLAY12",3)==12 && DesktopCapture.Number("Generic",3)==3,"Windows display number parsing");

            Require(Enumerable.Range(0,4).Aggregate(0,(mode,_)=>RemoteView.NextSignalMode(mode))==0,"Camera marker cycle does not return to Default");
            var original=RemoteHud.GlobalSignalMode;
            try
            {
                RemoteHud.Fixture=new RemoteView.View {SignalMode=3};
                RemoteHud.BeginCollection(out bool collection);
                try { var selected=original; RemoteHud.SignalMode(ref selected); Require(selected==Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.SignalMode.FullDisplay,"Camera collection depends on global marker mode"); }
                finally { RemoteHud.EndCollection(collection); }
                for(int mode=0;mode<4;mode++)
                {
                    RemoteHud.Fixture.SignalMode=mode; RemoteHud.BeginMarkers(out bool drawing);
                    try { var selected=original; RemoteHud.SignalMode(ref selected); Require((int)selected==mode,"Camera draw marker mode mismatch"); }
                    finally { RemoteHud.EndMarkers(drawing); }
                    Require(RemoteHud.GlobalSignalMode==original && Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.SignalDisplayMode==original,"Camera cycle changed global marker mode");
                }
            }
            finally { RemoteHud.Fixture=null; }
            const float width=1.6f,height=.9f,bar=.035f;
            float y=-height/2-bar;
            Require(WindowFrame.Control(new Vector3(WindowFrame.ZoomX(width,false),y,0),width,height,bar,false,true,true)==WindowFrame.CloseHover,"Close button hit");
            Require(WindowFrame.Control(new Vector3(WindowFrame.ZoomX(width,true),y,0),width,height,bar,false,true,true)==WindowFrame.MonitorHover,"Monitor button hit");
            Require(WindowFrame.Control(new Vector3(WindowFrame.ZoomX(width,true),y,0),width,height,bar,false,true,false)==0,"Hidden monitor button still hit");
            Require(WindowFrame.Control(new Vector3(WindowFrame.ZoomX(width,false),y,0),width,height,bar,true,false,false)==3 &&
                WindowFrame.Control(new Vector3(WindowFrame.ZoomX(width,true),y,0),width,height,bar,true,false,false)==4,"Camera zoom hits changed");
            Require(WindowFrame.Control(new Vector3(WindowFrame.ZoomX(width,false),0,0),width,height,bar,false,true,true)==0,"Picture hit as a frame control");

            Require(WindowFrame.Control(new Vector3(WindowFrame.SignalsX(width),WindowFrame.SignalsY(height),0),width,height,bar,true,false,false,true)==WindowFrame.SignalsHover,"Camera marker button hit");
            Require(WindowFrame.Control(new Vector3(WindowFrame.SignalsX(width),WindowFrame.SignalsY(height),0),width,height,bar,true,false,false)==0,"Hidden camera marker button still hit");

            foreach(float size in new[] {MenuWindow.MinWidth,1.2f,MenuWindow.MaxWidth})
            {
                var camera=new MenuWindow {Width=size,TopMargin=.08f};
                var icon=new Vector3(WindowFrame.SignalsX(size),WindowFrame.SignalsY(camera.Height),0);
                Require(camera.Handle(icon,true)==0 && WindowFrame.Control(icon,size,camera.Height,camera.BarOffset,true,false,false,true)==WindowFrame.SignalsHover,"Marker icon overlaps grab/resize");
                Require(icon.Y+.027f<camera.Height/2+camera.TopMargin && icon.Y-.027f>camera.Height/2,"Marker icon extends outside frame or into feed");
                Require(camera.Handle(new Vector3(0,-camera.Height/2-camera.BarOffset,0),true)==1,"Camera grab blocked by marker button");
            }

            foreach(bool revealed in new[] {false,true})
            {
                var keys=WristPanel.DesktopKeys(revealed,0);
                var face=new SurfaceView {Width=.40f,Height=.225f,Keys=keys};
                int back=Array.FindIndex(keys,k=>k.Label=="Back"),play=Array.FindIndex(keys,k=>k.Label=="Play/pause");
                Require(back>=0 && play==keys.Length-1 && keys[back].Invisible==!revealed && keys[play].Invisible,"Desktop wrist key visibility");
                Require(face.KeyAt(keys[back].Bounds.Center)==back && face.KeyAt(new Vector2(.5f,.5f))==play && face.KeyAt(new Vector2(.98f,.98f))==play,"Desktop wrist key priority");
                Require(keys.All(k=>k.Label!="Monitor"),"Monitor key shown with fewer than two monitors");
                var withMonitor=WristPanel.DesktopKeys(revealed,2);
                var monitorKey=withMonitor.Single(k=>k.Label=="Monitor");
                Require(monitorKey.Text=="2" && monitorKey.Invisible==!revealed && new SurfaceView {Width=.40f,Height=.225f,Keys=withMonitor}.KeyAt(monitorKey.Bounds.Center)==Array.IndexOf(withMonitor,monitorKey),"Monitor key number, reveal or priority");
            }

            try
            {
                WristPanel.Reset(); SpatialUi.Expand(); WristPanel.OpenDesktop();
                Require(!SpatialUi.CollapseIfOpen(),"B folded the desktop mirror without pointing at it");
                WristPanel.Show(2);
                Require(SpatialUi.CollapseIfOpen(),"B no longer folds other tablet pages");
                Require(!DesktopWindow.CloseIfPointed(),"Closed desktop window consumed B");
            }
            finally { WristPanel.Reset(); SpatialUi.Collapse(); }
            log("PASS desktop mirror: automatic and remembered monitor choice, display numbers, close/monitor hit areas beside unchanged zoom, wrist key priority and reveal, B pass-through unless pointed.");
        }
    }
}
