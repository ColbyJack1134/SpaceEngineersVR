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
            Persistence(log);
            Yoke(log);
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
                var shared=new MenuWindow {Width=width,MinimumWidth=.3f}; shared.Pose=Matrix.CreateTranslation(0,0,-1);
                var handles=new WindowInteraction(shared);
                var aim=Matrix.CreateTranslation(0,-shared.Height/2-shared.BarOffset,0);
                handles.Sample(aim,false,true,true,Vector2.One,.05f);
                Require(handles.Captured && shared.Drag==1,"Shared ray did not capture move bar");
                handles.Sample(aim,false,false,true,Vector2.One,.05f);
                Require(shared.Pose.Translation==new Vector3(0,0,-1),"Held stick moved window before neutral rearm");
                handles.Sample(aim,false,false,true,Vector2.Zero,.05f);
                handles.Sample(aim,false,false,true,Vector2.UnitY,.05f);
                Require(Math.Abs(shared.Pose.Translation.Z+1.04f)<.0001f,"Shared ray drag ignored thumbstick depth");
                float priorWidth=shared.Width; var bar=shared.Pose.Translation-shared.Pose.Up*shared.Height/2;
                handles.Sample(aim,false,false,true,Vector2.UnitX,.05f);
                Require(Math.Abs(shared.Width-priorWidth*(float)Math.Exp(.6*.05))<.0001f && Vector3.Distance(shared.Pose.Translation-shared.Pose.Up*shared.Height/2,bar)<.0001f,"Shared bar resize moved its anchor or ignored horizontal input");
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
        private static void Yoke(Action<string> log)
        {
            var tuning=new FlightTuning {Smoothing=0};
            var yoke=new CockpitYoke();
            Vector3 axis=Vector3.Normalize(new Vector3(0,.5f,.8660254f));
            Vector3 left=new Vector3(-.2f,0,0),right=new Vector3(.2f,0,0);
            Require(yoke.Update(true,true,left,false,right,axis,tuning,.001f)==0,"Yoke initial grip changed pitch");
            left+=axis*.0225f;
            float half=yoke.Update(true,true,left,false,right,axis,tuning,.001f);
            Require(Math.Abs(half+.5f)<.0001f && yoke.Visual<.1f,"Yoke test did not separate raw travel and visual lag");
            right+=axis*.08f;
            Require(Math.Abs(yoke.Update(true,true,left,true,right,axis,tuning,.001f)-half)<.0001f,"Second yoke hand rebased from visual instead of raw travel");
            left+=axis*.01f;
            float asymmetric=yoke.Update(true,true,left,true,right,axis,tuning,.001f);
            Require(Math.Abs(asymmetric+(.0275f-.005f)/.035f)<.0001f,"Yoke did not average hand travel");
            Require(Math.Abs(yoke.Update(true,false,left,true,right,axis,tuning,.001f)-asymmetric)<.0001f,"Yoke release stepped to the remaining hand's individual travel");
            right-=axis*.0275f;
            Require(Math.Abs(yoke.Update(true,false,left,true,right,axis,tuning,.001f))<.0001f,"Yoke handoff did not preserve its measured neutral displacement");
            right+=axis*.0225f; tuning.PitchSensitivity=2;
            yoke.Update(true,false,left,true,right,axis,tuning,1);
            Require(Math.Abs(yoke.Visual-1)<.0001f,"Yoke visual did not follow final sensitivity and response curve");
            Require(yoke.Update(true,false,left,false,right,axis,tuning,.016f)==0 && !yoke.Active && yoke.Visual>0,
                "Yoke release leaked pitch or snapped the returning visual");
            Require(yoke.Update(true,true,left,false,right,axis,tuning,.001f)==0,"Yoke recapture used a returning visual as neutral");
            Require(yoke.Update(true,true,left+axis*.25f,false,right,axis,tuning,.016f)==0 && !yoke.Active,"Large yoke tracking jump was accepted");
            Require(yoke.Update(true,true,left,true,right,axis,tuning,.016f)==0 && !yoke.Active,"Joining hand bypassed yoke discontinuity release gate");
            yoke.Update(true,false,left,false,right,axis,tuning,.016f);
            Require(yoke.Update(true,false,left,true,right,axis,tuning,.016f)==0 && yoke.Active,"Yoke did not rearm after both grips released");
            yoke.Update(true,false,left,true,new Vector3(float.NaN,0,0),axis,tuning,.016f);
            Require(!yoke.Active && yoke.Command==0,"Invalid yoke hand retained control");
            yoke.Reset(); Require(!yoke.Active && yoke.Command==0 && yoke.Visual==0,"Yoke reset retained state");

            tuning=new FlightTuning();
            Vector3 move=new Vector3(.7f,.4f,-.6f); Vector2 rotate=Vector2.Zero; float roll=0;
            CockpitStickMath.ApplyYoke(tuning,true,true,true,.5f,-.5f,new Vector2(.25f,.9f),10,1,ref move,ref rotate,ref roll);
            Require(move==new Vector3(0,.4f,-.6f) && Math.Abs(rotate.X+2.5f)<.0001f && Math.Abs(rotate.Y-.625f)<.0001f && Math.Abs(roll-2.5f)<.0001f,
                "Yoke wheel roll, pull pitch or thumb yaw mapped to the wrong native axis");
            CockpitStickMath.ApplyYoke(tuning,true,false,false,1,1,Vector2.One,10,1,ref move,ref rotate,ref roll);
            Require(rotate==Vector2.Zero && roll==0,"Released yoke retained rotational input");
            move=new Vector3(.7f,.4f,-.6f); rotate=new Vector2(2,3); roll=4;
            CockpitStickMath.ApplyYoke(tuning,false,false,false,1,1,Vector2.One,10,1,ref move,ref rotate,ref roll);
            Require(move==new Vector3(.7f,.4f,-.6f) && rotate==new Vector2(2,3) && roll==4,"Unowned yoke changed ordinary controller movement");
            log("PASS yoke input: raw-travel hand joins/releases, averaged pull, processed visual pitch, tracking release gate, wheel roll/thumb yaw mapping and neutral release.");
        }
        private static void Persistence(Action<string> log)
        {
            var tuning=new FlightTuning {Rotation=1.5f,Translation=.5f,TwistDeadzone=.15f,FlightMode=true,BarTiltEnabled=true,
                ThrottleSensitivity=1.25f,PitchSensitivity=.75f,RollSensitivity=1.75f,BarPitchTravel=.04f,BarPitchDeadzone=.015f,
                BarRollTravel=MathHelper.ToRadians(8),BarRollDeadzone=MathHelper.ToRadians(3),WheelMotion=true,
                WheelPitchTravel=.05f,WheelPitchDeadzone=.012f,WheelRollTravel=MathHelper.ToRadians(45)};
            var record=new CockpitMemory.Record {Toolbar="saved assignments",Covers=new[] {true,false},Flight=tuning};
            var restored=CockpitMemory.Decode(CockpitMemory.Encode(record));
            Require(restored.Flight.Encode()==tuning.Encode() && restored.Toolbar==record.Toolbar && restored.Covers.SequenceEqual(record.Covers) &&
                restored.LayoutVersion==record.LayoutVersion,"Flight persistence changed cockpit assignments or tuning");
            Require(FlightTuning.Decode(tuning.Encode()).Encode()==tuning.Encode(),"Network tuning roundtrip changed flight settings");
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                writer.Write(1); writer.Write(9L); writer.Write("legacy toolbar"); writer.Write(1); writer.Write(true);
                var old=CockpitMemory.Decode(Convert.ToBase64String(stream.ToArray()));
                Require(old.Flight==null && old.Toolbar=="legacy toolbar" && old.Covers[0],"Existing cockpit record did not migrate");
            }
            byte[] legacy;
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                foreach(float value in new[] {1.5f,.5f,1f,2f,.1f,.15f,.025f}) writer.Write(value);
                legacy=stream.ToArray();
            }
            var migrated=FlightTuning.Decode(Convert.ToBase64String(legacy));
            Require(migrated.Rotation==1.5f && migrated.Translation==.5f && migrated.RotationCurve==1 && migrated.Smoothing==.025f &&
                !migrated.FlightMode && !migrated.BarTiltEnabled && migrated.ThrottleSensitivity==1 && migrated.PitchSensitivity==1 &&
                migrated.RollSensitivity==1 && migrated.BarPitchTravel==.05f && migrated.BarPitchDeadzone==.005f &&
                Math.Abs(migrated.BarRollTravel-MathHelper.ToRadians(10))<.000001f && Math.Abs(migrated.BarRollDeadzone-MathHelper.ToRadians(2))<.000001f,
                "Legacy tuning migration lost values or safe defaults");
            byte[] body;
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                writer.Write(legacy); writer.Write(true); writer.Write(true);
                foreach(float value in new[] {1.25f,.75f,1.75f,.25f,.035f}) writer.Write(value);
                body=stream.ToArray();
            }
            var bodyMigrated=FlightTuning.Decode(Convert.ToBase64String(body));
            Require(bodyMigrated.FlightMode && !bodyMigrated.BarTiltEnabled && bodyMigrated.Rotation==1.5f && bodyMigrated.Translation==.5f &&
                bodyMigrated.ThrottleSensitivity==1.25f && bodyMigrated.PitchSensitivity==.75f && bodyMigrated.RollSensitivity==1.75f &&
                bodyMigrated.BarPitchTravel==.05f && bodyMigrated.BarPitchDeadzone==.005f && bodyMigrated.BarRollTravel==migrated.BarRollTravel &&
                bodyMigrated.BarRollDeadzone==migrated.BarRollDeadzone,"Body-lean migration enabled the hand gesture or lost existing flight tuning");
            byte[] bars;
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
            {
                writer.Write(legacy); writer.Write(true); writer.Write(true);
                foreach(float value in new[] {1.25f,.75f,1.75f,.04f,.01f,MathHelper.ToRadians(8),MathHelper.ToRadians(3)}) writer.Write(value);
                bars=stream.ToArray();
            }
            var barMigrated=FlightTuning.Decode(Convert.ToBase64String(bars));
            Require(barMigrated.FlightMode && barMigrated.BarTiltEnabled && barMigrated.BarPitchTravel==.04f &&
                barMigrated.BarPitchDeadzone==.01f && barMigrated.BarRollTravel==MathHelper.ToRadians(8) &&
                barMigrated.BarRollDeadzone==MathHelper.ToRadians(3) && barMigrated.ThrottleSensitivity==1.25f,
                "Prior bar settings lost enabled state or tuning during yoke migration");
            foreach(var old in new[] {migrated,bodyMigrated,barMigrated})
                Require(!old.WheelMotion && old.WheelPitchTravel==.04f && old.WheelPitchDeadzone==.005f &&
                    Math.Abs(old.WheelRollTravel-MathHelper.ToRadians(30))<.000001f,"Older tuning enabled yoke motion or lost safe defaults");
            foreach(int version in new[] {2,3,4,5})
            foreach(bool hasTuning in new[] {false,true})
            {
                using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream))
                {
                    writer.Write(version); writer.Write(17L); writer.Write("legacy assignments"); writer.Write(2); writer.Write(false); writer.Write(true);
                    writer.Write(hasTuning); if(hasTuning) writer.Write(version==5 ? bars:version==4 ? body:legacy);
                    if(version>=3) writer.Write(7);
                    var old=CockpitMemory.Decode(Convert.ToBase64String(stream.ToArray()));
                    Require(old.Revision==17 && old.Toolbar=="legacy assignments" && old.Covers.SequenceEqual(new[] {false,true}) &&
                        old.LayoutVersion==(version>=3 ? 7:0) && (hasTuning ? old.Flight?.Encode()==(version==5 ? barMigrated:version==4 ? bodyMigrated:migrated).Encode():old.Flight==null),
                        "Legacy tuning consumed the following cockpit layout or changed assignments");
                }
            }
            foreach(Action<FlightTuning> corrupt in new Action<FlightTuning>[] {v=>v.Rotation=float.NaN,v=>v.ThrottleSensitivity=float.PositiveInfinity,
                v=>v.ThrottleSensitivity=.24f,v=>v.PitchSensitivity=2.01f,v=>v.RollSensitivity=-1,v=>v.BarPitchTravel=.019f,
                v=>v.BarPitchTravel=.051f,v=>v.BarPitchDeadzone=-.001f,v=>v.BarPitchDeadzone=.021f,v=>{v.BarPitchTravel=.02f; v.BarPitchDeadzone=.02f;},
                v=>v.BarRollTravel=float.NaN,v=>v.BarRollTravel=MathHelper.ToRadians(11),v=>v.BarRollTravel=MathHelper.ToRadians(4),
                v=>v.BarRollDeadzone=MathHelper.ToRadians(6),v=>{v.BarRollTravel=(float)(Math.PI/36); v.BarRollDeadzone=v.BarRollTravel;},
                v=>v.WheelPitchTravel=.019f,v=>v.WheelPitchTravel=.061f,v=>v.WheelPitchDeadzone=-.001f,v=>v.WheelPitchDeadzone=.016f,
                v=>v.WheelRollTravel=float.NaN,v=>v.WheelRollTravel=MathHelper.ToRadians(14),v=>v.WheelRollTravel=MathHelper.ToRadians(61)})
            {
                var invalid=tuning.Copy(); corrupt(invalid); bool rejected=false;
                try {invalid.Encode();} catch(InvalidDataException) {rejected=true;}
                Require(rejected,"Invalid flight settings accepted on write");
            }
            byte[] malformed=Convert.FromBase64String(tuning.Encode());
            Array.Copy(BitConverter.GetBytes(float.NaN),0,malformed,30,4);
            bool invalidRead=false;
            try {FlightTuning.Decode(Convert.ToBase64String(malformed));} catch(InvalidDataException) {invalidRead=true;}
            Require(invalidRead,"Nonfinite throttle sensitivity accepted from network");
            malformed=Convert.FromBase64String(tuning.Encode());
            Array.Copy(BitConverter.GetBytes(float.PositiveInfinity),0,malformed,67,4); invalidRead=false;
            try {FlightTuning.Decode(Convert.ToBase64String(malformed));} catch(InvalidDataException) {invalidRead=true;}
            Require(invalidRead,"Nonfinite yoke roll range accepted from network");
            foreach(float invalid in new[] {float.NaN,.04f,.31f})
            {
                var invalidBody=(byte[])body.Clone(); Array.Copy(BitConverter.GetBytes(invalid),0,invalidBody,42,4);
                bool rejected=false;
                try {FlightTuning.Decode(Convert.ToBase64String(invalidBody));} catch(InvalidDataException) {rejected=true;}
                Require(rejected,"Invalid retired body settings bypassed validation during migration");
            }
            foreach(int length in new[] {27,29,49,51,57,59,70,72})
            {
                bool rejected=false;
                try {FlightTuning.Decode(Convert.ToBase64String(new byte[length]));} catch(InvalidDataException) {rejected=true;}
                Require(rejected,"Invalid tuning payload length accepted");
            }
            log("PASS vehicle settings persistence: bar/yoke network/storage roundtrip, v1-v5 migration, safe opt-in defaults, following layout alignment and malformed value/length rejection.");
        }
    }
}
