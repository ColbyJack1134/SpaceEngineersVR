using System;
using System.IO;
using System.Linq;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class FlightControlTests
    {
        private static void Require(bool value,string message) {if(!value) throw new Exception(message);}
        internal static void Run(Action<string> log)
        {
            var capture=new GripCapture();
            capture.Update(true,false,true,true,true,0);
            Require(capture.Update(true,true,true,true,true,.1),"Tap did not acquire stick");
            capture.Update(true,false,false,true,true,.2);
            Require(capture.Held && capture.Consumed,"Short tap did not retain ownership");
            capture.Update(true,false,false,true,true,1);
            Require(capture.Held,"Latched stick expired");
            capture.Update(true,true,false,true,true,1.1);
            Require(!capture.Held && capture.Consumed,"Release tap leaked its squeeze");
            capture.Update(true,true,true,true,true,1.2);
            Require(!capture.Held,"Release tap regrabbed without release");
            capture.Update(true,false,true,true,true,1.3);
            capture.Update(true,true,true,true,true,1.4);
            capture.Update(true,false,true,true,true,1.8);
            Require(!capture.Held,"Long hold latched after release");
            capture.Update(true,true,true,true,true,2);
            capture.Update(true,false,true,true,true,2.1);
            capture.Update(false,false,true,true,true,2.2);
            Require(!capture.Held,"Lost tracking retained latched stick");
            var lever=new CockpitTouch.Hand();
            var released=new InteractionInput(true,0,0,true,true);
            var squeezed=new InteractionInput(true,1,0,true,true);
            lever.Sample(true,released,"lever",0,tapHold:true);
            lever.Sample(true,squeezed,"lever",0,tapHold:true);
            Require(lever.Committed,"Analog tap did not capture");
            lever.Sample(true,released,null,-1,retainSqueeze:true,tapHold:true);
            Require(lever.Committed && lever.Consumed && lever.Surface=="lever","Analog latch lost ownership outside target");
            lever.Sample(true,squeezed,"other",0,retainSqueeze:true,tapHold:true);
            Require(lever.Surface==null && lever.Consumed,"Analog release tap leaked to another control");
            lever.Sample(true,squeezed,"other",0,tapHold:true);
            Require(lever.Surface==null,"Analog release squeeze recaptured");
            lever.Sample(true,released,"lever",0,tapHold:true);
            lever.Sample(true,squeezed,"lever",0,tapHold:true);
            lever.Sample(true,released,null,-1,retainSqueeze:true,tapHold:true);
            lever.Sample(false,released,null,-1,tapHold:true);
            Require(lever.Surface==null,"Analog latch survived context loss");
            lever.Sample(true,released,"lever",0,tapHold:true);
            lever.Sample(true,new InteractionInput(true,0,1,true,true),"lever",0,tapHold:true);
            Require(lever.Committed,"Trigger could not acquire analog handle");
            lever.Sample(true,released,null,-1,retainSqueeze:true,tapHold:true);
            Require(lever.Surface==null,"Trigger incorrectly latched analog handle");
            var tuning=new FlightTuning {Rotation=1.5f,Translation=.5f,TwistDeadzone=.15f};
            var record=new CockpitMemory.Record {Toolbar="saved assignments",Covers=new[] {true,false},Flight=tuning};
            var restored=CockpitMemory.Decode(CockpitMemory.Encode(record));
            Require(restored.Flight.Rotation==1.5f && restored.Flight.Translation==.5f && restored.Toolbar==record.Toolbar && restored.Covers[0],"Flight persistence changed cockpit assignments");
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(9L); writer.Write("legacy toolbar"); writer.Write(1); writer.Write(true);
                var old=CockpitMemory.Decode(Convert.ToBase64String(stream.ToArray()));
                Require(old.Flight==null && old.Toolbar=="legacy toolbar" && old.Covers[0],"Existing cockpit record did not migrate");
            }
            tuning.Rotation=float.NaN; bool rejected=false;
            try {tuning.Encode();} catch(InvalidDataException) {rejected=true;}
            Require(rejected,"Nonfinite flight settings accepted");
            Matrix rest=Matrix.Identity,turn=Matrix.CreateRotationY(.04f);
            var independent=CockpitStickMath.Rotation(rest,turn,0,true,1,null,.15f);
            Require(independent.Y==0,"Twist ignored separate deadzone");
            var visual=CockpitStickMath.Rotation(rest,Matrix.CreateRotationX(.2f),0,true);
            var command=CockpitStickMath.Rotation(rest,Matrix.CreateRotationX(.2f),.08f,true,2);
            Require(Math.Abs(visual.X)<Math.Abs(command.X),"Visual trial did not separate raw angle from sensitivity");
            Require(CockpitStickMath.ReturnVisual(Vector3.One,.12f)==Vector3.Zero,"Visual return did not finish");
            var filter=new CockpitStickMath.Filter();
            Require(Math.Abs(filter.Update(true,new Vector3(.5f),.01f,.05f).X-.1f)<.0001f,"VTOL smoothing rate differs");
            Require(filter.Update(false,Vector3.One,.01f,.05f)==Vector3.Zero,"Visual return leaked flight input");
            foreach(float width in new[] {.55f,.62f,1.2f,2.4f})
            {
                var shared=new MenuWindow {Width=width}; shared.Pose=Matrix.CreateTranslation(0,0,-1);
                var handles=new WindowInteraction(shared);
                var aim=Matrix.CreateTranslation(0,-shared.Height/2-shared.BarOffset,0);
                handles.Sample(aim,false,true,true,Vector2.One,.05f);
                Require(handles.Captured && shared.Drag==1,"Shared ray did not capture move bar");
                handles.Sample(aim,false,false,true,Vector2.One,.05f);
                Require(shared.Pose.Translation==new Vector3(0,0,-1),"Held stick moved window before neutral rearm");
                handles.Sample(aim,false,false,true,Vector2.Zero,.05f);
                handles.Sample(aim,false,false,true,Vector2.UnitY,.05f);
                Require(Math.Abs(shared.Pose.Translation.Z+1.04f)<.0001f,"Shared ray drag ignored thumbstick depth");
                handles.Sample(aim,false,false,true,Vector2.UnitX,.05f);
                Require(Math.Abs(shared.Pose.Translation.X-.04f)<.0001f,"Shared ray drag ignored thumbstick lateral movement");
                handles.Sample(aim,false,false,false,Vector2.UnitX,.05f);
                var stopped=shared.Pose;
                handles.Sample(aim,false,false,false,Vector2.UnitX,.05f);
                Require(!handles.Active && shared.Pose==stopped,"Shared window moved after release");
                var corner=Matrix.CreateTranslation(shared.Width/2,-shared.Height/2-shared.BarOffset,.5f)*shared.Pose;
                handles.Sample(corner,false,true,true,Vector2.Zero,.05f);
                Require(shared.Drag==2,"Shared resize corner did not capture");
                corner.Translation+=Vector3.Right*.1f;
                handles.Sample(corner,false,false,true,Vector2.Zero,.05f);
                Require(shared.Width>width || width==shared.MaximumWidth,"Shared resize corner did not resize");
                handles.Reset(); Require(!handles.Active && shared.Drag==0,"Shared capture survived reset");
            }
            var window=new MenuWindow {Width=.55f,Aspect=.31f/.55f,BarOffset=.035f,MinimumWidth=.42f,MaximumWidth=1.2f};
            Require(window.Handle(new Vector3(window.Width/2-.02f,-window.Height/2+.015f,0),true)==0,"Settings resize handle overlaps Apply");
            Require(window.Handle(new Vector3(window.Width/2,-window.Height/2-.035f,0),true)==2 &&
                window.Handle(new Vector3(0,-window.Height/2-.035f,0),true)==1,"Settings window handles unreachable");
            foreach(bool personal in new[] {false,true})
            {
                var keys=FlightSettings.Layout(new FlightTuning(),personal,false,false,"Test Flight Settings");
                foreach(var key in keys) Require(key.Bounds.X>=0 && key.Bounds.Y>=0 && key.Bounds.Right<=1 && key.Bounds.Bottom<=1,"Flight control outside panel");
                for(int i=0;i<keys.Length;i++) for(int j=i+1;j<keys.Length;j++) {var a=keys[i].Bounds; var b=keys[j].Bounds; Require(a.Right<=b.X || b.Right<=a.X || a.Bottom<=b.Y || b.Bottom<=a.Y,"Flight controls overlap");}
                Require(keys.Count(k=>k.Slider.HasValue)==(personal ? 0:5),"Flight slider count changed");
            }
            log("PASS flight settings: short/long/toggle/release/interrupt capture, shared tuning and legacy cockpit persistence, separate deadzones, raw visual angles, immediate input release, VTOL blend and panel bounds.");
        }
    }
}
