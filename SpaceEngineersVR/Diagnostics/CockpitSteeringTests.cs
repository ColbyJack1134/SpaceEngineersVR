using System;
using System.IO;
using HarmonyLib;
using VRageRender;
using VRageRender.Import;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitSteeringTests
    {
        internal static void Run(Action<string> log)
        {
            foreach(string subtype in new[] {"RoverCockpit","BuggyCockpit","SpeederCockpit","SpeederCockpitCompact"})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                var state=new CockpitSteering();
                var l=wheel.Contact(true,0); var r=wheel.Contact(false,0);
                Near(state.Update(wheel,true,l,false,r,.016f),0,"capture");
                l=wheel.Contact(true,.6f);
                Near(state.Update(wheel,true,l,false,r,.016f),.6f,"left-hand steering");
                r=wheel.Contact(false,.6f);
                Near(state.Update(wheel,true,l,true,r,.016f),.6f,"second-hand join");
                l=wheel.Contact(true,.8f); r=wheel.Contact(false,.8f);
                Near(state.Update(wheel,true,l,true,r,.016f),.8f,"two-hand steering");
                Near(state.Update(wheel,false,l,true,r,.016f),.8f,"handoff");
                r=wheel.Contact(false,1);
                Near(state.Update(wheel,false,l,true,r,.016f),1,"right-hand steering");
                Near(state.Update(wheel,false,l,true,r+wheel.Axis*.3f,.016f),1,"axis translation");
                Near(state.Update(wheel,false,l,false,r,.016f),0,"release command");
                if(state.Position<=0 || state.Position>=1) throw new Exception("Steering does not return smoothly: "+subtype);
                state.Reset(); Near(state.Position,0,"reset");
                state.Update(wheel,true,wheel.Contact(true,0),false,r,.016f);
                Near(state.Update(wheel,true,new Vector3(float.NaN,0,0),false,r,.016f),0,"invalid tracking");
            }
            var buggy=CockpitRig.Find("BuggyCockpit").Wheel;
            foreach(bool left in new[] {true,false})
            {
                var palm=buggy.Palm(left,0);
                if(palm.Right.Y>=-.25f || Vector3.Distance(Vector3.Transform(TrackedArms.BarFingerCavity,palm),buggy.Contact(left,0))>.0001f)
                    throw new Exception("Buggy wrist pitch loses the rim contact or points above the wheel");
            }
            foreach(string subtype in new[] {"SpeederCockpit","SpeederCockpitCompact"})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                var grip=wheel.Palm(false,0);
                var turn=new WristKnob.Turn(wheel.RightShaft,wheel.ThrottleRange,1);
                turn.Begin(grip,0);
                foreach(float steering in new[] {-.8f,.3f,1f})
                {
                    Matrix tracked=grip*wheel.Visual(steering);
                    turn.Move(tracked*Matrix.Invert(wheel.Visual(steering)));
                    Near(turn.Value,0,"steering must not open throttle");
                }
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,wheel.ThrottleRange*.5f));
                Near(turn.Value,.5f,"half throttle");
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,wheel.ThrottleRange*1.5f));
                Near(turn.Value,1,"throttle stop");
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,wheel.ThrottleRange));
                Near(turn.Value,.5f,"throttle reversal");
                turn.Move(grip*Matrix.CreateFromAxisAngle(wheel.RightShaft,-wheel.ThrottleRange));
                Near(turn.Value,0,"closed throttle stop");
                if(wheel.Palm(true,.6f,0)!=wheel.Palm(true,.6f,1)) throw new Exception("Throttle moves left grip");
                if(Vector3.Distance(wheel.Palm(false,0,1).Translation,grip.Translation)<.001f)
                    throw new Exception("Throttle does not carry the attached palm");
            }
            if(!CockpitStickMath.GripAligned(Matrix.Identity,Matrix.CreateRotationX(MathHelper.ToRadians(60))) ||
                CockpitStickMath.GripAligned(Matrix.Identity,Matrix.CreateRotationX(MathHelper.Pi)))
                throw new Exception("Steering grip permits an upside-down wrist or rejects normal reach");
            if(HarmonyLib.AccessTools.Field(typeof(Sandbox.Game.Entities.Blocks.MyTextPanelComponent),"m_previousTextureID")?.FieldType!=typeof(string))
                throw new Exception("Native cockpit LCD texture source changed");
            ScreenVisibility();
            ContinuousSteering();
            BarTilt();
            BarThrottle();
            CapturedThrottle();
            BarMount();
            BarFeedback();
            VehicleInput();
            VehicleSettings();
            log("PASS live moving LCD hide flags survive visibility refresh and release");
            log("PASS motorcycle throttle steering isolation, stops, reversal, right-only palm motion and steering grip orientation");
            log("PASS physical steering one-hand motion, two-hand join, handoff, axis rejection, release and tracking loss");
            log("PASS continuous steering stops, wrap crossing, immediate reversal, singular rejection, valid-hand averaging and visual smoothing");
            log("PASS two-hand bar pitch/roll capture, physical travel, yaw independence, visual return, tracking interruption and wrist throttle isolation");
            log("PASS captured throttle: tilted neutral, both stops, immediate reversal, common-roll release/rejoin continuity and steering after handoff");
            log("PASS anchored bar mount and attached palm contacts across combined yaw, pitch, roll and full throttle");
            log("PASS bar detents: neutral capture, center exit/return, limits, clamped motion silence, interruption reset and independent yaw/throttle cooldowns");
            log("PASS vehicle input driving gyro isolation, flight yaw curve, analog throttle, thumb and bar axes, ownership release and settings bounds");
        }
        private static void ContinuousSteering()
        {
            foreach(string subtype in new[] {"RoverCockpit","BuggyCockpit","SpeederCockpit","SpeederCockpitCompact"})
            foreach(bool left in new[] {true,false})
            foreach(float direction in new[] {-1f,1f})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                var state=new CockpitSteering();
                Func<float,Vector3> point=angle=>Vector3.Transform(left ? wheel.LeftContact:wheel.RightContact,
                    CockpitStickMath.Around(wheel.Pivot,Matrix.CreateFromAxisAngle(wheel.Axis,angle)));
                Func<Vector3,float> sample=p=>state.Update(wheel,left,p,!left,p,.01f);
                sample(point(0));
                float travelled=0;
                for(int i=1;i<=60;i++)
                {
                    travelled=direction*i*.12f;
                    Near(sample(point(travelled)),direction*Math.Min(i*.12f/wheel.Range,1),"continuous stop through wrap");
                }
                Near(sample(point(travelled-direction*.1f)),direction*(1-.1f/wheel.Range),"immediate reversal after excess travel");
                float stopped=state.Position;
                state.Update(wheel,true,wheel.Contact(true,stopped),true,wheel.Contact(false,stopped),.01f);
                Near(state.Position,stopped,"join at excess travel");
                state.Update(wheel,!left,wheel.Contact(true,stopped),left,wheel.Contact(false,stopped),.01f);
                Near(state.Position,stopped,"handoff at stop");
                Near(state.Update(wheel,false,Vector3.Zero,false,Vector3.Zero,.12f),0,"released raw command");
                Near(state.VisualPosition,0,"released visual return");
            }
            var rig=CockpitRig.Find("RoverCockpit").Wheel;
            var singular=new CockpitSteering();
            singular.Update(rig,true,rig.LeftContact,true,rig.RightContact,.01f);
            Near(singular.Update(rig,true,rig.Contact(true,.25f),true,rig.Pivot,.01f),.25f,"singular hand must not dilute valid hand");
            Near(singular.Update(rig,true,rig.Contact(true,.25f),true,rig.RightContact,.01f),.25f,"singular hand reacquisition");
            singular.Reset();
            singular.Update(rig,true,rig.LeftContact,false,Vector3.Zero,.01f);
            Vector3 opposite=Vector3.Transform(rig.LeftContact,CockpitStickMath.Around(rig.Pivot,Matrix.CreateFromAxisAngle(rig.Axis,MathHelper.Pi)));
            Near(singular.Update(rig,true,opposite,false,Vector3.Zero,.01f),0,"large tracking discontinuity");
            Near(singular.Update(rig,true,rig.LeftContact,false,Vector3.Zero,.01f),0,"discontinuity reacquisition");
            Near(singular.Update(rig,true,rig.Contact(true,.2f),false,Vector3.Zero,.01f),.2f,"motion after rejected discontinuity");
            var single=new CockpitSteering(); var divided=new CockpitSteering();
            single.Update(rig,true,rig.LeftContact,false,Vector3.Zero,0);
            divided.Update(rig,true,rig.LeftContact,false,Vector3.Zero,0);
            Near(single.Update(rig,true,rig.Contact(true,.7f),false,Vector3.Zero,.025f),.7f,"visual filter must not delay command");
            for(int i=0;i<5;i++) divided.Update(rig,true,rig.Contact(true,.7f),false,Vector3.Zero,.005f);
            Near(single.VisualPosition,divided.VisualPosition,"visual smoothing frame independence");
            if(single.VisualPosition<=0 || single.VisualPosition>=single.Position) throw new Exception("Steering visual does not smooth toward raw position");
            single.Reset(); Near(single.Position,0,"raw reset"); Near(single.VisualPosition,0,"visual reset");
        }
        private static void BarTilt()
        {
            var tuning=new FlightTuning {Smoothing=0};
            var tilt=new CockpitBarTilt();
            Vector3 left=new Vector3(-.31f,.7f,-.4f),right=new Vector3(.31f,.7f,-.4f);
            Near(tilt.Update(true,left,right,tuning,.016f).Length(),0,"bar neutral capture");
            if(!tilt.Active) throw new Exception("Valid hand pair did not activate bar tilt");
            Near(tilt.Update(true,left+Vector3.Up*.004f,right+Vector3.Up*.004f,tuning,.016f).Length(),0,"bar pitch deadzone");
            var raised=tilt.Update(true,left+Vector3.Up*.0275f,right+Vector3.Up*.0275f,tuning,.016f);
            Near(raised.X,-.5f,"both hands raised pitch up"); Near(raised.Y,0,"pitch has no roll");
            Near(tilt.Raw.X,.0275f,"raw pitch remains physical travel");
            var lowered=tilt.Update(true,left-Vector3.Up*.05f,right-Vector3.Up*.05f,tuning,.016f);
            Near(lowered.X,1,"both hands lowered pitch down");
            tilt.Reset(); tilt.Update(true,left,right,tuning,0);
            float halfBank=(tuning.BarRollTravel+tuning.BarRollDeadzone)*.5f;
            float height=.31f*(float)Math.Tan(halfBank);
            var banked=tilt.Update(true,left+Vector3.Up*height,right-Vector3.Up*height,tuning,.025f);
            Near(banked.X,0,"opposed hand roll has no pitch"); Near(banked.Y,.5f,"right hand lower rolls right");
            Near(tilt.Raw.Y,halfBank,"raw bank remains physical angle");
            float visualBefore=tilt.Visual.Y;
            Near(tilt.Update(false,left,right,tuning,.001f).Length(),0,"one-hand release neutralizes added axes");
            if(tilt.Active || tilt.Visual.Y<=0 || tilt.Visual.Y>=visualBefore) throw new Exception("Bar release does not retain a smooth visual return");
            var rejoined=tilt.Update(true,left+Vector3.Up*.03f,right-Vector3.Up*.03f,tuning,.001f);
            Near(rejoined.Length(),0,"rejoin while visual returns establishes neutral");
            tilt.Update(false,left,right,tuning,.12f);
            Near(tilt.Visual.Length(),0,"bar visual returns fully after release");
            foreach(float yaw in new[] {-.6f,0,.6f})
            {
                tilt.Reset(); tilt.Update(true,left,right,tuning,0);
                var turn=Matrix.CreateRotationY(yaw);
                Vector3 l=Vector3.Transform(left+Vector3.Up*height,turn),r=Vector3.Transform(right-Vector3.Up*height,turn);
                // Step yaw at the installed steering range so displacement rejection remains meaningful.
                for(int i=1;i<=4;i++)
                {
                    turn=Matrix.CreateRotationY(yaw*i/4);
                    l=Vector3.Transform(left+Vector3.Up*height,turn); r=Vector3.Transform(right-Vector3.Up*height,turn);
                    banked=tilt.Update(true,l,r,tuning,.016f);
                }
                Near(banked.Y,.5f,"roll elevation remains invariant under bar yaw"); Near(banked.X,0,"bar yaw has no pitch");
            }
            tilt.Reset(); tilt.Update(true,left,right,tuning,0);
            Near(tilt.Update(true,new Vector3(float.NaN,0,0),right,tuning,.016f).Length(),0,"invalid pair tracking");
            Near(tilt.Update(true,left,right,tuning,.016f).Length(),0,"invalid tracking cannot reactivate held pair");
            if(tilt.Active) throw new Exception("Invalid pair tracking did not require grip release");
            tilt.Update(false,left,right,tuning,.016f);
            Near(tilt.Update(true,left,right,tuning,.016f).Length(),0,"pair recovery neutral");
            Near(tilt.Update(true,left+Vector3.Up*.25f,right,tuning,.016f).Length(),0,"large hand discontinuity rejected");
            if(tilt.Active) throw new Exception("Large hand jump retained pair control");
            tilt.Reset();
            Near(tilt.Update(true,Vector3.Zero,Vector3.Up,tuning,.016f).Length(),0,"collapsed horizontal pair rejected");
            if(tilt.Active) throw new Exception("Vertical or crossed pair accepted without horizontal span");
            tilt.Reset(); tilt.Update(true,left,right,tuning,0); tuning.Smoothing=.05f;
            var partial=tilt.Update(true,left+Vector3.Up*.0275f,right+Vector3.Up*.0275f,tuning,.01f);
            if(partial.X>=0 || partial.X<=-.5f) throw new Exception("Bar commands ignore configured input smoothing");
            tilt.Reset(); Near(tilt.Visual.Length(),0,"bar reset clears visual"); Near(tilt.Command.Length(),0,"bar reset clears filter");
            var single=new CockpitBarTilt(); var divided=new CockpitBarTilt(); tuning.Smoothing=0;
            single.Update(true,left,right,tuning,0); divided.Update(true,left,right,tuning,0);
            single.Update(true,left+Vector3.Up*.05f,right+Vector3.Up*.05f,tuning,.025f);
            for(int i=0;i<5;i++) divided.Update(true,left+Vector3.Up*.05f,right+Vector3.Up*.05f,tuning,.005f);
            Near(single.Visual.X,divided.Visual.X,"bar visual smoothing frame independence");
            single.Update(false,left,right,tuning,.016f);
            if(single.Visual.X<=0) throw new Exception("Physical pitch visual returned faster than normalized 120 ms travel");
        }
        private static void BarMount()
        {
            foreach(string subtype in new[] {"SpeederCockpit","SpeederCockpitCompact"})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                foreach(float yaw in new[] {-1f,0,1f})
                foreach(float pitch in new[] {-.05f,0,.05f})
                foreach(float roll in new[] {-MathHelper.ToRadians(10),0,MathHelper.ToRadians(10)})
                {
                    var tilt=new Vector2(pitch,roll); var visual=wheel.Visual(yaw,tilt);
                    Near(Vector3.Distance(Vector3.Transform(wheel.TiltPivot,visual),wheel.TiltPivot),0,"bar stem remains at native mount");
                    foreach(bool left in new[] {true,false})
                    {
                        var palm=wheel.Palm(left,yaw,1,tilt);
                        var contact=Vector3.Transform(left ? wheel.LeftContact:wheel.RightContact,visual);
                        Near(Vector3.Distance(Vector3.Transform(TrackedArms.BarFingerCavity,palm),contact),0,"bar feedback retains palm contact");
                    }
                }
                var raised=wheel.Visual(0,new Vector2(.05f,0));
                if(Vector3.Transform(wheel.LeftContact,raised).Y<=wheel.LeftContact.Y ||
                    Vector3.Transform(wheel.RightContact,raised).Y<=wheel.RightContact.Y)
                    throw new Exception("Anchored pitch feedback does not raise both grips");
                var banked=wheel.Visual(0,new Vector2(0,MathHelper.ToRadians(10)));
                if(Vector3.Transform(wheel.LeftContact,banked).Y<=wheel.LeftContact.Y ||
                    Vector3.Transform(wheel.RightContact,banked).Y>=wheel.RightContact.Y)
                    throw new Exception("Anchored right-roll feedback reverses the grips");
            }
        }
        private static void BarThrottle()
        {
            var tuning=new FlightTuning {Smoothing=0};
            foreach(string subtype in new[] {"SpeederCockpit","SpeederCockpitCompact"})
            foreach(float initialYaw in new[] {-.5f,0,.5f})
            foreach(float yaw in new[] {-.3f,0,.3f})
            foreach(float bank in new[] {-.15f,0,.15f})
            foreach(float leftTwist in new[] {0,.4f})
            foreach(float rightTwist in new[] {0,.3f})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                var pair=new CockpitBarTilt();
                Matrix startFrame=Matrix.CreateRotationY(initialYaw);
                Matrix left=Matrix.CreateFromYawPitchRoll(.2f,-.1f,.3f)*startFrame;
                Matrix right=Matrix.CreateFromYawPitchRoll(-.3f,.2f,-.1f)*startFrame;
                left.Translation=wheel.LeftContact; right.Translation=wheel.RightContact;
                pair.Update(true,left,right,wheel,tuning,0,startFrame);
                var knob=new WristKnob.Turn(wheel.RightShaft,wheel.ThrottleRange,1);
                knob.Begin(right*Matrix.Transpose(startFrame),.2f);
                Matrix common=Matrix.CreateRotationZ(-bank)*Matrix.CreateRotationY(yaw);
                Vector3 leftAxis=Vector3.TransformNormal(wheel.LeftShaft,startFrame),rightAxis=Vector3.TransformNormal(wheel.RightShaft,startFrame);
                Matrix l=left*Matrix.CreateFromAxisAngle(leftAxis,leftTwist)*common;
                Matrix r=right*Matrix.CreateFromAxisAngle(rightAxis,rightTwist)*common;
                Matrix frame=pair.ThrottleFrame(l,r,wheel),expected=startFrame*common;
                Near(Vector3.Distance(frame.Right,expected.Right),0,"bar frame ignores independent left/right twists");
                Near(Vector3.Distance(frame.Up,expected.Up),0,"bar frame retains captured yaw and rigid roll");
                knob.Move(r*Matrix.Transpose(frame));
                Near(knob.Value,.2f+rightTwist/wheel.ThrottleRange,"production bar compensation isolates right throttle");
                float value=knob.Value;
                pair.Update(false,l,r,wheel,tuning,.016f,startFrame);
                Matrix singleFrame=wheel.Visual(.4f).GetOrientation();
                knob.Begin(r*Matrix.Transpose(singleFrame),value);
                knob.Move(r*Matrix.Transpose(singleFrame));
                Near(knob.Value,value,"pair release preserves throttle across frame rebase");
                pair.Reset(); pair.Update(true,left,right,wheel,tuning,0,startFrame);
                knob.Begin(right*Matrix.Transpose(startFrame),.2f);
                l=left; r=right; l.Translation+=Vector3.Up*.04f; r.Translation-=Vector3.Up*.04f;
                pair.Update(true,l,r,wheel,tuning,.016f,startFrame);
                frame=pair.ThrottleFrame(l,r,wheel);
                Near(Vector3.Distance(frame.Up,startFrame.Up),0,"position-only hand roll does not invent wrist rotation");
                knob.Move(r*Matrix.Transpose(frame)); Near(knob.Value,.2f,"position-only hand roll cannot open throttle");
            }
        }
        private static void CapturedThrottle()
        {
            foreach(string subtype in new[] {"SpeederCockpit","SpeederCockpitCompact"})
            {
                var wheel=CockpitRig.Find(subtype).Wheel;
                var throttle=new CockpitThrottle();
                Matrix neutral=Matrix.CreateFromYawPitchRoll(.3f,-.2f,.1f);
                Func<float,Matrix> wrist=value=>neutral*Matrix.CreateFromAxisAngle(wheel.RightShaft,value*wheel.ThrottleRange);
                Matrix initial=wrist(.8f);
                Near(throttle.Update(true,false,initial,Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),0,"tilted throttle grab starts closed");
                Near(throttle.Update(true,false,initial,Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),0,"unchanged captured wrist stays closed");
                Near(throttle.Update(true,false,wrist(1.3f),Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),.5f,"half throttle relative to grab");
                Near(throttle.Update(true,false,wrist(2.3f),Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),1,"relative throttle full stop");
                Near(throttle.Update(true,false,wrist(2.1f),Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),.8f,"excess stop travel permits immediate reversal");
                Near(throttle.Update(true,false,wrist(-.4f),Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),0,"opposite throttle stop");
                throttle.Update(false,false,neutral,Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange);
                Near(throttle.Update(true,false,wrist(.4f),Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),0,"new wrist grab captures closed gas");
                foreach(float gas in new[] {0,.4f})
                foreach(float startYaw in new[] {-.4f,.4f})
                {
                    throttle.Reset();
                    Matrix yaw=Matrix.CreateRotationY(startYaw),common=Matrix.CreateRotationZ(-MathHelper.ToRadians(10))*yaw;
                    throttle.Update(true,true,neutral*yaw,yaw,wheel.RightShaft,wheel.ThrottleRange);
                    Near(throttle.Update(true,true,wrist(gas)*yaw,yaw,wheel.RightShaft,wheel.ThrottleRange),gas,"paired throttle at initial yaw");
                    Near(throttle.Update(true,true,wrist(gas)*common,common,wheel.RightShaft,wheel.ThrottleRange),gas,"common roll does not become throttle");
                    Near(throttle.Update(true,false,wrist(gas)*common,yaw,wheel.RightShaft,wheel.ThrottleRange),gas,"left release preserves captured throttle");
                    yaw=Matrix.CreateRotationY(startYaw+.2f); common=Matrix.CreateRotationZ(-MathHelper.ToRadians(10))*yaw;
                    Near(throttle.Update(true,false,wrist(gas)*common,yaw,wheel.RightShaft,wheel.ThrottleRange),gas,"ordinary steering still cancels after handoff");
                    Near(throttle.Update(true,false,wrist(gas+.2f)*common,yaw,wheel.RightShaft,wheel.ThrottleRange),gas+.2f,"right throttle remains responsive after handoff");
                    Near(throttle.Update(true,true,wrist(gas+.2f)*common,yaw,wheel.RightShaft,wheel.ThrottleRange),gas+.2f,"left rejoin cannot recenter throttle");
                    Matrix extra=Matrix.CreateRotationZ(.05f);
                    Near(throttle.Update(true,true,wrist(gas+.2f)*common*extra,yaw*extra,wheel.RightShaft,wheel.ThrottleRange),gas+.2f,"new paired common motion preserves transition correction");
                    throttle.Update(false,false,neutral,Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange);
                    Near(throttle.Update(true,false,neutral,Matrix.Identity,wheel.RightShaft,wheel.ThrottleRange),0,"new right grip clears previous compensation");
                }
            }
        }
        private static void BarFeedback()
        {
            var pair=(CockpitFeedback.StickPulse)AccessTools.Field(typeof(CockpitControls),"barDetent").GetValue(null);
            var yaw=(CockpitFeedback.StickPulse)AccessTools.Field(typeof(CockpitControls),"leftDetent").GetValue(null);
            var throttle=(CockpitFeedback.StickPulse)AccessTools.Field(typeof(CockpitControls),"rightDetent").GetValue(null);
            var start=new DateTime(2026,1,1);
            try
            {
                pair.Sample(false,Vector3.Zero,start); yaw.Sample(false,Vector3.Zero,start); throttle.Sample(false,Vector3.Zero,start);
                var tilt=new CockpitBarTilt(); var tuning=new FlightTuning {Smoothing=0};
                Vector3 left=new Vector3(-.31f,.7f,-.4f),right=new Vector3(.31f,.7f,-.4f);
                Func<float,double,int> sample=(height,milliseconds)=>
                {
                    var command=tilt.Update(true,left+Vector3.Up*height,right+Vector3.Up*height,tuning,.016f);
                    return pair.Sample(true,new Vector3(command.X,0,command.Y),start.AddMilliseconds(milliseconds));
                };
                Near(sample(0,0),0,"pair neutral has no detent");
                Near(sample(.02f,100),1,"pair center exit detent");
                Near(sample(0,250),1,"pair center return detent");
                yaw.Sample(true,Vector3.Zero,start); throttle.Sample(true,Vector3.Zero,start);
                Near(yaw.Sample(true,Vector3.UnitX,start.AddMilliseconds(350)),2,"independent yaw limit cue");
                Near(throttle.Sample(true,Vector3.UnitY,start.AddMilliseconds(350)),2,"independent throttle limit cue");
                Near(sample(.05f,400),2,"independent hand cooldowns cannot suppress shared bar limit");
                Near(sample(.08f,550),0,"no repeated bar limit while clamped");
                Near(sample(.12f,700),0,"excess physical travel has no repeated detent");
                var motion=new CockpitFeedback.MotionPulse();
                Near(motion.Sample(true,-Vector3.UnitX,start),0,"motion initial capture mute");
                Near(motion.Sample(true,-Vector3.UnitX,start.AddMilliseconds(100)),0,"clamped hold has no repeated motion rumble");
                Near(motion.Sample(true,-Vector3.UnitX,start.AddMilliseconds(500)),0,"clamped travel remains silent");
                Near(pair.Sample(false,Vector3.Zero,start.AddMilliseconds(800)),0,"pair interruption has no detent");
                Near(pair.Sample(true,Vector3.Zero,start.AddMilliseconds(900)),0,"pair recapture neutral has no detent");
                Near(pair.Sample(true,new Vector3(0,0,.5f),start.AddMilliseconds(1000)),1,"roll center exit detent");
                Near(pair.Sample(true,Vector3.UnitZ,start.AddMilliseconds(1150)),2,"roll limit detent");
                Near(pair.Sample(true,Vector3.UnitZ,start.AddMilliseconds(1300)),0,"roll limit does not repeat");
            }
            finally
            {
                pair.Sample(false,Vector3.Zero,start); yaw.Sample(false,Vector3.Zero,start); throttle.Sample(false,Vector3.Zero,start);
            }
        }
        private static void VehicleInput()
        {
            var tuning=new FlightTuning();
            foreach(float steering in new[] {-1f,-.5f,0,.5f,1f})
            {
                Vector3 move=new Vector3(.3f,.4f,.7f); Vector2 rotate=new Vector2(2,-3); float roll=4;
                CockpitStickMath.ApplySteering(tuning,false,true,true,true,true,steering,.9f,Vector2.One,Vector2.One,10,1,ref move,ref rotate,ref roll);
                Near(move.X,Math.Sign(steering)*steering*steering,"driving wheel response"); Near(move.Y,.4f,"driving vertical retention"); Near(move.Z,.7f,"driving native throttle retention");
                Near(rotate.X,2,"driving pitch isolation"); Near(rotate.Y,-3,"driving yaw isolation at stop"); Near(roll,4,"driving roll isolation");
            }
            Vector3 flightMove=new Vector3(.3f,.4f,.7f); Vector2 flightRotate=Vector2.Zero; float flightRoll=0;
            CockpitStickMath.ApplySteering(tuning,true,true,true,true,false,.5f,.35f,Vector2.Zero,new Vector2(.5f,-.5f),10,1,
                ref flightMove,ref flightRotate,ref flightRoll);
            Near(flightMove.X,0,"flight wheel strafe suppression"); Near(flightMove.Z,-.35f,"analog flight throttle");
            Near(flightRotate.Y,2.5f,"flight single response curve"); Near(flightRotate.X,2.5f,"flight thumb pitch"); Near(flightRoll,2.5f,"flight thumb roll");
            flightMove.Z=-.8f;
            CockpitStickMath.ApplySteering(tuning,true,true,true,true,false,0,.35f,Vector2.Zero,Vector2.Zero,10,1,
                ref flightMove,ref flightRotate,ref flightRoll);
            Near(flightMove.Z,-.8f,"stronger native forward thrust");
            tuning.PitchSensitivity=2; tuning.RollSensitivity=.5f;
            CockpitStickMath.ApplySteering(tuning,true,true,true,false,true,0,0,new Vector2(.25f,-.5f),Vector2.Zero,10,1,
                ref flightMove,ref flightRotate,ref flightRoll);
            Near(flightRotate.X,2.5f,"bar pitch sensitivity"); Near(flightRoll,-.625f,"bar roll sensitivity");
            CockpitStickMath.ApplySteering(tuning,true,true,false,false,true,1,1,Vector2.One,Vector2.One,10,1,
                ref flightMove,ref flightRotate,ref flightRoll);
            Near(flightRotate.Length(),0,"released flight rotation"); Near(flightRoll,0,"released bar roll");
            flightRotate=new Vector2(2,3); flightRoll=4;
            CockpitStickMath.ApplySteering(tuning,true,true,true,false,false,.5f,0,Vector2.Zero,Vector2.Zero,10,1,
                ref flightMove,ref flightRotate,ref flightRoll);
            Near(flightRotate.X,2,"left-only yaw grip preserves free right pitch"); Near(flightRoll,4,"left-only yaw grip preserves free right roll");
            flightMove=new Vector3(.3f,.4f,.7f); flightRotate=new Vector2(2,3); flightRoll=4;
            CockpitStickMath.ApplySteering(tuning,true,false,false,false,true,1,1,Vector2.One,Vector2.One,10,1,
                ref flightMove,ref flightRotate,ref flightRoll);
            if(flightMove!=new Vector3(.3f,.4f,.7f) || flightRotate!=new Vector2(2,3) || flightRoll!=4)
                throw new Exception("Unowned vehicle controls changed native movement");
        }
        private static void VehicleSettings()
        {
            foreach(string subtype in new[] {"RoverCockpit","BuggyCockpit","SpeederCockpit","SpeederCockpitCompact"})
            foreach(bool personal in new[] {false,true})
            for(int page=0;page<3;page++)
            {
                var keys=FlightSettings.Layout(new FlightTuning {FlightMode=true,BarTiltEnabled=true,WheelMotion=true},personal,false,false,"Vehicle Settings",false,subtype,page);
                foreach(var key in keys)
                    if(key.Bounds.X<0 || key.Bounds.Y<0 || key.Bounds.Right>1 || key.Bounds.Bottom>1)
                        throw new Exception("Vehicle settings outside panel: "+subtype+" / "+page);
                for(int i=0;i<keys.Length;i++) for(int j=i+1;j<keys.Length;j++)
                {
                    var a=keys[i].Bounds; var b=keys[j].Bounds;
                    if(a.Right>b.X && b.Right>a.X && a.Bottom>b.Y && b.Bottom>a.Y)
                        throw new Exception("Vehicle settings overlap: "+subtype+" / "+page);
                }
            }
        }
        private static void ScreenVisibility()
        {
            var active=AccessTools.Field(typeof(CockpitRender),"activeRig");
            var check=AccessTools.Field(typeof(CockpitRender),"verification");
            object previousRig=active.GetValue(null),previousCheck=check.GetValue(null);
            var rig=CockpitRig.Find("RoverCockpit");
            string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyModelImporter).Assembly.Location),"..","Content"));
            try
            {
                active.SetValue(null,rig);
                check.SetValue(null,new CockpitRender.Verification(123,rig.Geometry(content),new string[0]));
                RenderFlags add=RenderFlags.Visible,remove=CockpitRender.Hidden;
                CockpitRender.PreserveScreenVisibility(123,"CockpitScreen_04",ref add,ref remove);
                if((add&CockpitRender.Hidden)!=CockpitRender.Hidden || (remove&CockpitRender.Hidden)!=0)
                    throw new Exception("Live LCD update exposes the original moving screen");
                var converter=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyProxiesFactory"),"GetRenderableProxyFlags");
                object added=converter.Invoke(null,new object[] {add}),removed=converter.Invoke(null,new object[] {remove});
                long hidden=Convert.ToInt64(Enum.Parse(added.GetType(),"SkipInMainView, SkipInDepth, SkipInForward"));
                if((Convert.ToInt64(added)&~Convert.ToInt64(removed)&hidden)!=hidden)
                    throw new Exception("Native flag conversion clears moving screen suppression");
                foreach(var item in new[] {Tuple.Create(124u,"CockpitScreen_04"),Tuple.Create(123u,"CockpitScreen_01"),Tuple.Create(123u,(string)null)})
                {
                    add=RenderFlags.Visible; remove=0;
                    CockpitRender.PreserveScreenVisibility(item.Item1,item.Item2,ref add,ref remove);
                    if(add!=RenderFlags.Visible || remove!=0) throw new Exception("LCD suppression affects another actor or fixed screen");
                }
                check.SetValue(null,null);
                add=RenderFlags.Visible; remove=RenderFlags.Visible|CockpitRender.Hidden;
                CockpitRender.PreserveScreenVisibility(123,"CockpitScreen_04",ref add,ref remove);
                if(add!=RenderFlags.Visible || remove!=(RenderFlags.Visible|CockpitRender.Hidden))
                    throw new Exception("Released LCD cannot restore native visibility");
            }
            finally { active.SetValue(null,previousRig); check.SetValue(null,previousCheck); }
        }
        private static void Near(float value,float expected,string name)
        {
            if(!float.IsNaN(value) && Math.Abs(value-expected)<.0001f) return;
            throw new Exception("Steering "+name+": "+value+" != "+expected);
        }
    }
}
