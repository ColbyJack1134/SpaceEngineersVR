using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using HarmonyLib;
using SharpDX.Direct3D11;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI.HudViewers;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using Sandbox.Graphics;
using Sandbox.Game.Localization;
using VRage;
using VRage.Utils;
using SpaceEngineersVR.Player;
using VRage.Game;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class LeadIndicatorTests
    {
        private sealed class Frame
        {
            internal string Scenario;
            internal MatrixD Projection;
            internal NativeLead.View Lead;
            internal Vector2 Size;
            internal RectangleF Line;
            internal Color Color;
            internal float Rotation;
            internal bool HasLine;
        }
        private static readonly MethodInfo nativeLine=AccessTools.Method(AccessTools.Inner(typeof(MyHudMarkerRender),"MyTargetLeadRender"),"DrawLine");
        private static readonly MethodInfo nativeMarker=AccessTools.Method(typeof(MyHudMarkerRenderBase),"AddTexturedQuad",new[] {typeof(string),typeof(Vector2),typeof(Color),typeof(float),typeof(float),typeof(bool)});
        private static Frame capturing;
        private static volatile Frame current;
        private static readonly Dictionary<string,Frame> saved=new Dictionary<string,Frame>();
        internal static bool Enabled => Environment.GetEnvironmentVariable("SEVR_NATIVE_LEAD")=="1";
        internal static string Output => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SEVRPrototype","Reports","lead-comparison");
        internal static void Install(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(MyRenderProxy),"DrawSprite"),new HarmonyMethod(typeof(LeadIndicatorTests),nameof(Sprite)));
        }
        private static void Sprite(string texture,ref RectangleF destination,Color color,float rotation)
        {
            if(capturing==null || !texture.EndsWith("TargetingLine.dds",StringComparison.OrdinalIgnoreCase)) return;
            capturing.Line=destination; capturing.Color=color; capturing.Rotation=rotation; capturing.HasLine=true;
        }
        internal static void DrawNative(MyHudMarkerRender renderer,string scenario)
        {
            var target=new Vector3D(80,0,-700);
            var velocity=scenario.Contains("overlap") ? Vector3D.Zero:scenario.Contains("diagonal") ? new Vector3D(65,45,0):new Vector3D(scenario.Contains("left") ? -80:80,0,0);
            var predicted=MarkerTests.Predict(target,velocity);
            bool inRange=!scenario.Contains("range");
            var color=MyHudMarkerRender.MyTargetIndicatorRender.GetTargetingColor(Sandbox.Game.GUI.MyStatControlTargetingProgressBar.ProgressBarTargetType.Enemy,inRange);
            var size=MyGuiManager.GetSafeFullscreenRectangle();
            var frame=new Frame {Scenario=scenario,Size=new Vector2(size.Width,size.Height),Projection=MySector.MainCamera.ProjectionMatrix,
                Lead=new NativeLead.View {Target=target,Position=predicted,Color=color.ToVector4(),InRange=inRange,CircleSize=NativeSignalProbe.Circle().Size,RangeTextSize=inRange ? Vector2.Zero:NativeLead.MeasureRangeText()}};
            MyHudMarkerRender.TryComputeScreenPoint(target,out var start,out _);
            MyHudMarkerRender.TryComputeScreenPoint(predicted,out var end,out _);
            var circle=NativeSignalProbe.Circle();
            circle.SetTargetType(MyRelationsBetweenPlayerAndBlock.Enemies); circle.StatCurrent=1;
            circle.Position=MyHudMarkerRender.MyTargetIndicatorRender.CalculateCircularBarPosition(start,circle);
            var previous=MySession.Static;
            try
            {
                MySession.Static=(MySession)FormatterServices.GetUninitializedObject(typeof(MySession));
                circle.Draw(1);
            }
            finally { MySession.Static=previous; }
            nativeMarker.Invoke(renderer,new object[] {@"Textures\GUI\TargetingPredictionMarker.dds",end*MyGuiManager.GetHudSize(),color,circle.Size.X*.4f,circle.Size.Y*.4f,false});
            if(!inRange)
            {
                var label=MyGuiManager.GetNormalizedCoordinateFromScreenCoordinate(circle.Position+new Vector2(circle.Size.X/2,circle.Size.Y+10+circle.Size.Y/4));
                AccessTools.Method(typeof(MyHudMarkerRender.MyTargetIndicatorRender),"DrawText").Invoke(null,new object[] {
                    MyTexts.GetString(MySpaceTexts.LeadIndicator_OutOfWeaponRange),(MyFontEnum)"White",.7f,label,color,MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER});
            }
            capturing=frame;
            try { nativeLine.Invoke(null,new object[] {start,end,circle.Size,new Vector2(64),color,3f,renderer}); }
            finally { capturing=null; }
            current=frame;
        }
        internal static void Render()
        {
            var frame=current;
            if(frame==null || saved.ContainsKey(frame.Scenario)) return;
            Directory.CreateDirectory(Output);
            NativeSprites.Poll();
            using(var canvas=new OverlayCanvas("Lead comparison",(int)frame.Size.X,(int)frame.Size.Y,1,false))
            {
                var background=frame.Scenario.EndsWith("bright") ? System.Drawing.Color.FromArgb(225,225,215):System.Drawing.Color.FromArgb(7,12,20);
                canvas.Clear(background); canvas.Upload();
                var description=canvas.Texture.Description; description.Format=SharpDX.DXGI.Format.B8G8R8A8_UNorm_SRgb;
                using(var target=new Texture2D(canvas.Texture.Device,description))
                {
                    target.Device.ImmediateContext.CopyResource(canvas.Texture,target);
                    var sprites=new List<NativeSprite>();
                    NativeSignalProbe.Ring("Enemy",1).AddNative(sprites,frame.Lead.Target,MatrixD.Identity,frame.Projection,(int)frame.Size.X);
                    NativeSprites.Draw(target,sprites);
                    NativeLead.Draw(target,frame.Lead,MatrixD.Identity,MatrixD.Identity,frame.Projection);
                    if(NativeSprites.Pending) return;
                    UiTests.Save(target,Path.Combine(Output,"vr-"+frame.Scenario+".png"));
                    if(frame.HasLine)
                    {
                        foreach(string variant in new[] {"straight","premultiplied","native-color"})
                        {
                            target.Device.ImmediateContext.CopyResource(canvas.Texture,target);
                            var line=Line(frame,variant);
                            NativeSprites.Draw(target,new[] {line});
                            if(NativeSprites.Pending) return;
                            UiTests.Save(target,Path.Combine(Output,variant+"-"+frame.Scenario+".png"));
                        }
                    }
                    File.WriteAllText(Path.Combine(Output,frame.Scenario+".txt"),"Target="+frame.Lead.Target+"\nPrediction="+frame.Lead.Position+"\nNative line="+frame.Line+"\nRotation="+frame.Rotation+"\nColor="+frame.Color+"\n");
                    saved.Add(frame.Scenario,frame);
                }
            }
        }
        internal static void Verify(int expected)
        {
            if(saved.Count!=expected) throw new InvalidOperationException("Lead comparison did not render every VR fixture: "+saved.Count+"/"+expected);
            foreach(var frame in saved.Values)
            {
                if(!frame.HasLine) continue;
                using(var native=new System.Drawing.Bitmap(Path.Combine(Output,"vanilla-"+frame.Scenario+".png")))
                using(var actual=new System.Drawing.Bitmap(Path.Combine(Output,"native-color-"+frame.Scenario+".png")))
                {
                    var center=frame.Line.Center;
                    var direction=new Vector2((float)Math.Cos(frame.Rotation),(float)Math.Sin(frame.Rotation));
                    int radius=(int)Math.Ceiling(frame.Line.Width/2)+4,channels=0;
                    long difference=0;
                    for(int y=(int)center.Y-radius;y<=(int)center.Y+radius;y++)
                    for(int x=(int)center.X-radius;x<=(int)center.X+radius;x++)
                    {
                        var delta=new Vector2(x+.5f,y+.5f)-center;
                        float along=Vector2.Dot(delta,direction)/frame.Line.Width+.5f;
                        if(along<=.08f || along>=.88f || Math.Abs(Vector2.Dot(delta,new Vector2(-direction.Y,direction.X)))>=3) continue;
                        var a=native.GetPixel(x,y); var b=actual.GetPixel(x,y);
                        difference+=Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B); channels+=3;
                    }
                    double error=channels==0 ? double.PositiveInfinity:(double)difference/channels;
                    if(error>4) throw new InvalidOperationException("Native lead blend mismatch: "+frame.Scenario+" mean channel error="+error);
                    SpaceEngineersVR.Plugin.Logger.Info("PASS native lead blend: "+frame.Scenario+" mean channel error="+error.ToString("F3"));
                }
            }
            SpaceEngineersVR.Plugin.Logger.Info("PASS lead comparison: native line method and sprite renderer, production VR lead and matched-geometry alpha references across "+expected+" scenarios.");
        }
        private static NativeSprite Line(Frame frame,string variant)
        {
            var b=frame.Line; var center=b.Center;
            var right=new Vector2((float)Math.Cos(frame.Rotation),(float)Math.Sin(frame.Rotation));
            var down=new Vector2(-right.Y,right.X);
            Func<float,float,Vector4> corner=(x,y)=>
            {
                var p=center+right*x*b.Width*.5f+down*y*b.Height*.5f;
                return new Vector4(p.X*2/frame.Size.X-1,1-p.Y*2/frame.Size.Y,0,1);
            };
            var line=variant=="native-color" ? NativeLead.LineSprite(frame.Color.ToVector4()):
                new NativeSprite(@"Textures\GUI\TargetingLine.dds",default(RectangleF),frame.Color.ToVector4()) {Projected=true,Premultiplied=variant=="premultiplied"};
            line.TopLeft=corner(-1,-1); line.TopRight=corner(1,-1); line.BottomLeft=corner(-1,1); line.BottomRight=corner(1,1);
            return line;
        }
    }
}
