using System;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitCaptureTests
    {
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }
        public static void Run(Action<string> log)
        {
            var sources=new InteractionPress();
            sources.Update(true,false,false,false,true);
            Require(!sources.Update(true,false,true,false,true),"Grip activated a distant ray target");
            Require(!sources.Update(true,true,true,false,true),"Held grip activated on entering near range");
            sources.Update(true,true,false,false,true);
            Require(sources.Update(true,true,true,false,true),"Fresh near grip was lost");
            Require(!sources.Update(true,true,true,true,true),"Trigger repeated a held near grip action");
            sources.Update(true,false,false,false,true);
            Require(sources.Update(true,false,false,true,true),"Fresh ray trigger was lost");
            sources.Block();
            Require(!sources.Update(true,false,false,true,true),"Focus return reused a held trigger");
            var modality=new CockpitTouch.Hand();
            modality.Sample(true,new InteractionInput(false,true,0,false,true),"Panel",0);
            modality.Sample(true,new InteractionInput(true,true,1,true,true),"Panel",0);
            Require(!modality.Captured,"Changing source captured an already held grip");
            modality.Sample(true,new InteractionInput(true,true,0,false,true),"Panel",0);
            modality.Sample(true,new InteractionInput(true,true,1,true,true),"Panel",0);
            Require(modality.Pressed,"Fresh grip did not rearm after source change");
            var hands=new[] {new CockpitTouch.Hand(),new CockpitTouch.Hand()};
            foreach(var hand in hands)
            {
                hand.Sample(true,1,true,"Seat",5);
                Require(hand.Held<0,"Held trigger captured on entry");
                hand.Sample(true,0,false,"Seat",5);
                for(int i=0;i<20;i++) hand.Sample(true,0,false,"Seat",i%13);
                Require(!hand.Pressed && hand.Held<0,"Hover or poke activated a button");
                hand.Sample(true,.2f,false,"Seat",5);
                Require(hand.Captured && hand.Consumed && !hand.Committed && !hand.Pressed,"Light squeeze failed to capture without activating");
                hand.Sample(true,1,true,"Seat",5);
                Require(hand.Pressed && hand.Held==5 && hand.Consumed,"Fresh trigger did not capture seat button");
                for(int i=0;i<180;i++)
                {
                    var seatMotion=new Vector3(i*.002f,i*.001f,-i*.0005f);
                    var raw=new Vector3(.12f,.03f,.04f)+seatMotion;
                    var compensated=CockpitTouch.Compensate(raw,seatMotion,Vector3.Zero);
                    Require(Vector3.Distance(compensated,new Vector3(.12f,.03f,.04f))<.0001f,"Seat movement became hand movement");
                    hand.Sample(true,1,true,i%2==0 ? "Seat" : "CockpitControl0",9,reachable:compensated.Length()<.28f);
                    Require(!hand.Pressed && hand.Surface=="Seat" && hand.Held==5,"Seat capture slid onto power or another surface");
                }
                hand.Sample(true,1,true,null,-1);
                Require(hand.Held==5,"Ordinary drift released captured button");
                hand.Sample(true,.2f,false,"Seat",9);
                Require(hand.Held<0 && hand.Consumed,"Partial release retained button or freed firing ownership");
                hand.Sample(true,1,true,"Seat",9);
                Require(!hand.Pressed && hand.Held<0,"Partial release allowed another click");
                hand.Sample(true,0,false,"Seat",9);
                hand.Sample(true,1,true,"Seat",9);
                Require(hand.Pressed && hand.Held==9,"Full release failed to rearm");
                hand.Sample(true,1,true,"Seat",5,reachable:false);
                Require(hand.Held<0 && hand.Consumed,"Large pullaway retained attachment or resumed firing");
                hand.Sample(true,1,true,"Seat",5);
                Require(!hand.Pressed && hand.Held<0,"Returning after pullaway reacquired held squeeze");
                hand.Sample(false,1,true,"Seat",5);
                hand.Sample(true,1,true,"Seat",5);
                Require(!hand.Pressed && hand.Consumed,"Focus/tracking return acquired held squeeze");
                hand.Sample(true,0,false,null,-1);
                hand.Sample(true,1,true,null,-1);
                hand.Sample(true,1,true,"CockpitCover0",0);
                Require(hand.Held<0 && !hand.Consumed,"Firing squeeze became a control click");
                hand.Sample(true,0,false,"CockpitCover0",0);
                hand.Sample(true,1,true,"CockpitCover0",0,canAcquire:false);
                Require(hand.Held<0,"Blocked native action captured a control");
                hand.Sample(true,0,false,"CockpitCover0",0);
                hand.Sample(true,1,true,"CockpitCover0",0);
                Require(hand.Pressed,"Cover fresh trigger lost");
            }
            hands[0].Reset();
            Require(hands[1].Held==0,"Releasing one hand released the other");
            GripPresses();
            DualPresses();
            NearMisses();
            SoftCapture();
            var heldLever=new CockpitTouch.Hand();
            var repeatDrag=new ControlDrag();
            heldLever.Sample(true,0,false,"Lever",0);
            heldLever.Sample(true,1,true,"Lever",0);
            repeatDrag.Begin(Vector3.Zero,new Vector3(0,0,.04f),Vector3.Zero,Vector3.Right,0,1);
            int activations=0;
            for(int i=0;i<20;i++)
            {
                heldLever.Sample(true,.3f,false,null,-1,retainSqueeze:true);
                Require(heldLever.Surface=="Lever" && heldLever.Committed,"Relaxing a held lever detached it");
                var radial=new Vector3(0,0,.04f);
                var hand=Vector3.Transform(radial,Matrix.CreateRotationX(i%2==0 ? 1:0))-radial;
                if(repeatDrag.Move(hand,heldLever.Committed)==1) activations++;
                Require(repeatDrag.Move(hand)==-1,"Held momentary lever repeated without a new detent");
            }
            Require(activations==10,"Repeated momentary flicks were lost");
            heldLever.Sample(true,0,false,null,-1,retainSqueeze:true);
            Require(heldLever.Surface==null,"Lever retained attachment after full release");
            var descent=new GripDescent(); var pressed=DateTime.UtcNow;
            descent.Update(true,.2f,pressed);
            descent.Update(true,1,pressed.AddMilliseconds(149));
            Require(!descent.Ready,"Partial third-person squeeze leaked descent/sprint");
            descent.Update(true,1,pressed.AddMilliseconds(151));
            Require(descent.Ready,"Delayed descent/sprint never became ready");
            descent.Update(true,1,pressed.AddSeconds(5));
            Require(descent.Ready,"Held descent acquired another delay");
            descent.Update(true,0,pressed.AddSeconds(6));
            descent.Update(true,1,pressed.AddSeconds(7));
            Require(!descent.Ready,"Fresh grip inherited the previous delay");
            descent.Update(false,1,pressed.AddSeconds(8));
            Require(!descent.Ready,"Pan ownership leaked descent/sprint");
            float Burst(GripDescent replay,Func<double,float> grip,double seconds,out double first,out double last)
            {
                double total=0; first=last=-1;
                for(int frame=0;frame*(1/90.0)<=seconds;frame++)
                {
                    double time=frame/90.0;
                    if(replay.Replay(grip(time),pressed.AddSeconds(time))<=0) continue;
                    total+=1/90.0; if(first<0) first=time; last=time;
                }
                return (float)total;
            }
            var replayed=new GripDescent();
            float tapThrust=Burst(replayed,at => at<.1 ? .6f:0,1,out double burstStart,out double burstEnd);
            Require(burstStart>=GripDescent.Delay-.001 && burstStart<GripDescent.Delay+.012 && Math.Abs(tapThrust-.1f)<.012f,"Short descent tap did not replay late for its own length");
            Require(replayed.Replay(0,pressed.AddSeconds(1.01))==0,"Descent tap repeated after playback");
            replayed=new GripDescent();
            float holdThrust=Burst(replayed,at => at<.6 ? 1:0,1.2,out burstStart,out burstEnd);
            Require(Math.Abs(holdThrust-.6f)<.012f && burstEnd<.6+GripDescent.Delay+.012,"Held descent lost or extended its length");
            replayed=new GripDescent();
            float doubleThrust=Burst(replayed,at => at<.05 || at>=.1 && at<.14 ? 1:0,1,out burstStart,out burstEnd);
            Require(Math.Abs(doubleThrust-.09f)<.025f && burstEnd<.14+GripDescent.Delay+.012,"Two quick descent taps did not replay as two bursts");
            replayed=new GripDescent();
            replayed.Replay(1,pressed); replayed.Replay(1,pressed.AddSeconds(.1));
            replayed.Cancel();
            Require(replayed.Replay(0,pressed.AddSeconds(.2))==0 && replayed.Replay(0,pressed.AddSeconds(.3))==0,"Pan start fired queued descent");
            replayed.Replay(1,pressed.AddSeconds(3));
            Require(replayed.Replay(0,pressed.AddSeconds(5))==0,"Paused input replayed stale descent");
            replayed=new GripDescent();
            replayed.Replay(1,pressed);
            Require(replayed.Replay(1,pressed.AddSeconds(.25))==1 && replayed.Replay(1,pressed.AddSeconds(.5))==1,"Low frame rate suppressed held descent");
            var tap=new GripTap();
            Require(!tap.Update(true,false,false,pressed) && tap.Update(false,true,false,pressed.AddMilliseconds(200)),"Short centered grip tap did not lock");
            tap.Update(true,false,false,pressed); tap.Update(true,false,true,pressed.AddMilliseconds(100));
            Require(!tap.Update(true,false,false,pressed.AddMilliseconds(150)) && !tap.Update(false,true,false,pressed.AddMilliseconds(200)),"Grip roll with stick input requested a lock");
            tap.Update(true,false,false,pressed);
            Require(!tap.Update(false,true,false,pressed.AddMilliseconds(500)),"Held roll grip requested a lock on release");
            tap.Update(true,false,false,pressed);
            Require(!tap.Update(false,false,false,pressed.AddMilliseconds(100)) && !tap.Update(false,true,false,pressed.AddMilliseconds(150)),"Blocked third-person grip requested a lock");
            var menuClick=new PartialClick(); int clicks=0;
            foreach(float pressure in new[] {0,.3f,1,.75f,1,.65f,.95f,.6f,.1f,0,.26f})
            { menuClick.Update(pressure>=.2f,pressure>=.25f && !menuClick.Held && pressure<=.3f,pressure); if(menuClick.Pressed) clicks++; }
            Require(clicks==3,"Menu trigger needs a full release between clicks or a held drag let go on small drift: "+clicks+" clicks");
            menuClick.Update(false,false,1); menuClick.Update(true,false,1);
            Require(!menuClick.Held,"A trigger held through a consumed press became a menu click");
            var recovery=new RenderRecovery("Test");
            Require(recovery.Fail(null,"",pressed)==2 && recovery.FailedAt(pressed.AddSeconds(1)) && !recovery.FailedAt(pressed.AddSeconds(2.1)),"Failed renderer does not retry after two seconds");
            Require(recovery.Fail(null,"",pressed.AddSeconds(3))==4 && recovery.Fail(null,"",pressed.AddSeconds(8))==8,"Repeated renderer failures do not back off");
            for(int i=0;i<8;i++) recovery.Fail(null,"",pressed.AddSeconds(20+i));
            Require(recovery.Fail(null,"",pressed.AddSeconds(30))==60,"Renderer retry delay is unbounded");
            Require(recovery.Fail(null,"",pressed.AddSeconds(500))==2,"Renderer backoff persists after a quiet period");
            recovery.Clear(); Require(!recovery.FailedAt(pressed.AddSeconds(500)),"Renderer reset keeps a failure");
            var tracked=Matrix.CreateFromYawPitchRoll(.4f,.2f,-.1f); tracked.Translation=new Vector3(.3f,1.1f,-.2f);
            var nan=tracked; nan.M42=float.NaN; var far=tracked; far.Translation=new Vector3(0,0,2000); var flat=tracked*Matrix.CreateScale(0);
            Require(TrackedDevice.Plausible(tracked) && !TrackedDevice.Plausible(nan) && !TrackedDevice.Plausible(far) && !TrackedDevice.Plausible(flat),"Implausible controller pose accepted");
            var pointer=new PointerHand();
            pointer.Update(true,false,false); Require(!pointer.Left,"Right laser lost the menu");
            pointer.Update(false,true,false); Require(pointer.Left,"Left hand aimed alone did not take the laser");
            pointer.Update(true,true,false); Require(pointer.Left,"Both hands aimed switched the laser away from the current hand");
            pointer.Update(true,false,true); Require(pointer.Left,"Laser switched hands during a held click");
            pointer.Update(true,false,false); Require(!pointer.Left,"Right hand aimed alone did not take the laser back");
            pointer.Update(false,false,false); Require(!pointer.Left,"Laser changed hands with neither aimed");
            var crouch=new CrouchControl(); const float depth=.55f; var t=pressed; var stickDown=new Vector2(.1f,-.9f); var stickUp=new Vector2(-.1f,.9f);
            Require(!crouch.Update(stickDown,true,false,0,depth,false,t),"Stick held through a context change toggled crouch");
            crouch.Update(Vector2.Zero,true,false,0,depth,false,t);
            Require(crouch.Update(stickDown,true,false,0,depth,false,t) && !crouch.Update(stickDown,true,false,0,depth,true,t.AddSeconds(1)),"Stick crouch must toggle once per push");
            crouch.Update(Vector2.Zero,true,false,0,depth,true,t.AddSeconds(1));
            Require(!crouch.Update(new Vector2(.95f,-.8f),true,false,0,depth,true,t.AddSeconds(1)),"Diagonal turn toggled crouch");
            Require(!crouch.Update(stickDown,true,false,0,depth,true,t.AddSeconds(1)),"Stick down stood the character up");
            crouch.Update(Vector2.Zero,true,false,0,depth,true,t.AddSeconds(1));
            Require(crouch.Update(stickUp,true,false,0,depth,true,t.AddSeconds(1)) && !crouch.Automatic,"Stick up did not stand");
            crouch.Update(Vector2.Zero,true,false,0,depth,false,t.AddSeconds(1));
            Require(!crouch.Update(stickUp,true,false,0,depth,false,t.AddSeconds(1)),"Stick up crouched a standing character");
            crouch.Reset(); crouch.Update(Vector2.Zero,true,true,0,depth,false,t);
            Require(!crouch.Update(Vector2.Zero,true,true,.3f,depth,false,t) && crouch.Update(Vector2.Zero,true,true,.4f,depth,false,t) && crouch.Automatic,"Real crouch did not crouch the character");
            Require(!crouch.Update(Vector2.Zero,true,true,.1f,depth,true,t.AddSeconds(.2)),"Auto crouch flickered before settling");
            Require(!crouch.Update(Vector2.Zero,true,true,.3f,depth,true,t.AddSeconds(1)) && crouch.Update(Vector2.Zero,true,true,.2f,depth,true,t.AddSeconds(1)),"Standing up did not stand the character");
            crouch.Update(Vector2.Zero,true,true,0,depth,false,t.AddSeconds(2));
            Require(crouch.Update(stickDown,true,true,0,depth,false,t.AddSeconds(3)) && !crouch.Update(Vector2.Zero,true,true,0,depth,true,t.AddSeconds(4)),"Stick crouch stood up automatically");
            crouch.Reset(); crouch.Update(Vector2.Zero,true,true,.45f,depth,false,t); crouch.Update(Vector2.Zero,true,true,.45f,depth,true,t.AddSeconds(1));
            Require(crouch.Update(stickUp,true,true,.45f,depth,true,t.AddSeconds(2)) && !crouch.Update(Vector2.Zero,true,true,.45f,depth,false,t.AddSeconds(3)),"Stick stand while low re-crouched immediately");
            crouch.Update(Vector2.Zero,true,true,.1f,depth,false,t.AddSeconds(4));
            Require(crouch.Update(Vector2.Zero,true,true,.45f,depth,false,t.AddSeconds(5)),"Auto crouch stayed suppressed after rising");
            crouch.Reset(); crouch.Update(Vector2.Zero,true,false,.6f,depth,false,t);
            Require(!crouch.Update(Vector2.Zero,true,false,.6f,depth,false,t.AddSeconds(1)),"Seated or ladder play auto-crouched");
            Require(Math.Abs(CrouchControl.ViewDrop(true,false,0,depth)-depth)<1e-5f && Math.Abs(CrouchControl.ViewDrop(true,false,.3f,depth)-.25f)<1e-5f &&
                CrouchControl.ViewDrop(true,true,.4f,depth)==0 && CrouchControl.ViewDrop(false,false,0,depth)==0 && CrouchControl.ViewDrop(true,false,.8f,depth)==0,"Crouched view height is wrong");
            var gesture=new HeadGesture(); var gameTrigger=new InputGate();
            var temple=Matrix.CreateTranslation(.21f,0,0);
            gesture.Update(true,temple,Matrix.Identity,false); gameTrigger.Update(true,false);
            int cycles=0;
            for(int press=0;press<8;press++)
            {
                var hand=Matrix.CreateTranslation(press==0 ? .21f:.27f,0,0);
                foreach(bool down in new[] {true,true,false,false})
                {
                    gameTrigger.Update(true,down);
                    gesture.Update(true,hand,Matrix.Identity,down);
                    if(gesture.Inside) gameTrigger.Block();
                    if(gesture.Pressed) cycles++;
                    Require(!gameTrigger.Held,"Repeated helmet gesture leaked selected ship weapon input");
                }
            }
            Require(cycles==8,"Held-near-head gesture failed to rearm between trigger presses");
            gesture.Update(true,temple,Matrix.Identity,false,false);
            gesture.Update(true,temple,Matrix.Identity,true,false);
            Require(gesture.Inside && !gesture.Pressed,"Equipped gun trigger cycled HUD or disabled helmet grip zone");
            gesture.Update(true,temple,Matrix.Identity,true,true);
            Require(!gesture.Pressed,"Unequipping a gun rearmed a held helmet trigger");
            gesture.Update(true,temple,Matrix.Identity,false,true);
            gesture.Update(true,temple,Matrix.Identity,true,true);
            Require(gesture.Pressed,"Fresh empty-hand helmet trigger did not recover");
            gesture.Update(true,Matrix.CreateTranslation(.5f,-.3f,0),Matrix.Identity,false);
            Require(!gesture.Inside,"Withdrawn gesture retained tool ownership");
            gesture.Update(true,Matrix.CreateTranslation(.27f,0,0),Matrix.Identity,true);
            Require(!gesture.Inside && !gesture.Pressed,"Retention volume acquired a fresh gesture");
            gesture.Update(false,temple,Matrix.Identity,true);
            gesture.Update(true,temple,Matrix.Identity,true);
            Require(!gesture.Pressed,"Tracking/context loss rearmed a held trigger");
            foreach(float side in new[] {-1f,1f})
            {
                var headPose=Matrix.CreateRotationY(.7f)*Matrix.CreateTranslation(20,2,30);
                Require(HelmetHud.NearHead(Matrix.CreateTranslation(side*.21f,0,0)*headPose,headPose),"Head protection missed a temple");
                Require(HelmetHud.NearHead(Matrix.CreateTranslation(0,0,.25f)*headPose,headPose),"Head protection missed the rear of the helmet");
                foreach(var outside in new[] {new Vector3(side*.26f,0,0),new Vector3(0,-.15f,0),new Vector3(0,.18f,0),new Vector3(0,0,-.16f)})
                    Require(!HelmetHud.NearHead(Matrix.CreateTranslation(outside)*headPose,headPose),"Head protection retained oversized bounds");
                for(int i=0;i<40;i++)
                {
                    var hand=Matrix.CreateTranslation(side*(.1f+i*.006f),0,0)*headPose;
                    Require(!HelmetHud.NearTemple(hand,headPose,side<0) || HelmetHud.NearHead(hand,headPose),"Temple gesture extends outside early input protection");
                }
                Require(!HelmetHud.NearHead(Matrix.CreateTranslation(side*.5f,-.3f,0)*headPose,headPose),"Head protection captured ordinary flight input");
            }
            foreach(bool linear in new[] {false,true})
                foreach(float end in new[] {.2f,.8f})
                {
                    var drag=new ControlDrag(); var radial=new Vector3(0,0,.035f);
                    if(linear) drag.BeginLinear(Vector3.Zero,Vector3.Up,.5f,.030f);
                    else drag.Begin(Vector3.Zero,radial,Vector3.Zero,Vector3.Right,.5f,1.05f);
                    Func<float,Vector3> move=value=>linear ? Vector3.Up*((value-.5f)*.030f) :
                        Vector3.Transform(radial,Matrix.CreateRotationX((value-.5f)*1.05f))-radial;
                    foreach(float value in new[] {.5f,.52f,.48f,.6f,.4f,.5f})
                        Require(drag.Move(move(value))==-1,"Ready connector selected a state inside the neutral band");
                    Require(drag.Move(move(end))==(end>.5f ? 1 : 0),"Ready connector missed its first connect/disconnect request");
                    Require(drag.Move(move(end))==-1 && drag.Move(move(.5f))==-1,"Held or centered connector repeats a request");
                    Require(drag.Move(move(1-end))==(end>.5f ? 0 : 1),"Connector reversal missed the opposite detent");
                }
            foreach(float start in new[] {0f,1f})
            {
                var drag=new ControlDrag(); var radial=new Vector3(0,0,.035f);
                var origin=new Vector3(.2f,-.1f,.3f);
                drag.Begin(origin,radial,Vector3.Zero,Vector3.Right,start,1.3f);
                Require(drag.Move(origin)==-1 && Math.Abs(drag.Value-start)<.0001f,"Grab snapped hinge");
                foreach(float value in new[] {.5f,.8f,.55f,.45f,.2f,.5f,.8f})
                {
                    var moved=origin+Vector3.Transform(radial,Matrix.CreateRotationX((value-start)*1.3f))-radial;
                    bool before=drag.State;
                    int changed=drag.Move(moved);
                    bool expected=before ? value>.35f : value>=.65f;
                    Require(Math.Abs(drag.Value-value)<.0001f && drag.State==expected,"Controller travel or hinge direction is incorrect");
                    Require(changed==(expected==before ? -1 : expected ? 1 : 0),"Hinge detent repeated or missed");
                    Require(drag.Move(moved)==-1,"Stationary hinge buzzes or repeats action");
                }
                float prior=drag.Value;
                Require(drag.Move(new Vector3(float.NaN,0,0))==-1 && drag.Value==prior,"Invalid tracking altered hinge state");
            }
            var bar=CockpitRig.Find(CockpitLayout.Fighter).Bars[0];
            foreach(float start in new[] {0f,1f})
            {
                var drag=new ControlDrag();
                var axis=bar.Normal;
                var origin=bar.Front;
                drag.BeginLinear(origin,axis,start,bar.Travel);
                Require(drag.Move(origin)==-1 && drag.Value==start,"Pull bar activates on capture");
                foreach(float value in new[] {.5f,.8f,.55f,.45f,.2f,1f,0f})
                {
                    bool before=drag.State;
                    var hand=origin+axis*((value-start)*bar.Travel);
                    int changed=drag.Move(hand);
                    Require(Math.Abs(drag.Value-value)<.0001f,"Pull bar lost linear hand travel");
                    bool expected=before ? value>.35f : value>=.65f;
                    Require(changed==(expected==before ? -1 : expected ? 1 : 0),"Pull bar repeats or misses a detent");
                    Require(drag.Move(hand)==-1,"Held pull bar repeats its action/haptic");
                }
                drag.BeginLinear(origin,axis,start,bar.Travel);
                var lateral=Vector3.Normalize(Vector3.Cross(axis,Vector3.Up))*.1f;
                Require(drag.Move(origin+lateral)==-1 && Math.Abs(drag.Value-start)<.0001f,"Sideways motion switches pull bar");
                Require(drag.Move(new Vector3(float.NaN,0,0))==-1,"Lost tracking switches pull bar");
            }
            var pulse=new CockpitFeedback.MotionPulse(); var now=DateTime.UtcNow;
            Require(pulse.Sample(true,Vector3.Zero,now)==0,"Grab produced a movement pulse");
            for(int i=1;i<20;i++) Require(pulse.Sample(true,new Vector3(i%2*.002f,0,0),now.AddMilliseconds(i*10))==0,"Still-hand jitter buzzes");
            int pulses=0;
            for(int i=1;i<100;i++) if(pulse.Sample(true,new Vector3(i*.003f,0,0),now.AddMilliseconds(200+i*10))>0) pulses++;
            Require(pulses>3 && pulses<15,"Slow stick travel has no feedback or buzzes continuously");
            Require(pulse.Sample(true,Vector3.One,now.AddSeconds(2),true)==0,"Movement overwrote a detent pulse");
            pulse.Sample(false,Vector3.Zero,now.AddSeconds(3));
            Require(pulse.Sample(true,Vector3.One,now.AddSeconds(4))==0,"Regrab used the previous stick position");
            var valuePulse=new CockpitFeedback.ValuePulse(); valuePulse.Reset(.5f);
            for(int i=0;i<20;i++) Require(!valuePulse.Sample(.5f+(i%2==0 ? .003f:-.003f),now.AddMilliseconds(i*10)),"Analog jitter repeats clicks");
            Require(valuePulse.Sample(.53f,now.AddSeconds(1)),"Analog value change has no click");
            Require(!valuePulse.Sample(.6f,now.AddSeconds(1).AddMilliseconds(10)),"Analog clicks exceed rate limit");
            Require(valuePulse.Sample(.6f,now.AddSeconds(1).AddMilliseconds(60)),"Analog rate limit loses continued motion");
            valuePulse.Reset(.99f);
            Require(valuePulse.Sample(1,now.AddSeconds(2)) && !valuePulse.Sample(1,now.AddSeconds(3)),"Endpoint click repeats or is missing");
            valuePulse.Reset(.1f); Require(!valuePulse.Sample(.1f,now.AddSeconds(4)),"Regrab pulses from an old value");
            var farFinger=new CockpitProbe {Start=new Vector3D(1),End=new Vector3D(1),Tip=new Vector3D(1)};
            foreach(var palm in new[] {new Vector3(0,0,.02f),new Vector3(.02f,0,-.035f),new Vector3(-.075f,0,-.0155f)})
                Require(CockpitTouch.NearBar(farFinger,new CockpitProbe {Start=palm,End=palm},out _,out _),"Palm approach misses bar from top, underside or side");
            Require(!CockpitTouch.NearBar(farFinger,new CockpitProbe {Start=new Vector3D(.2,0,0),End=new Vector3D(.2,0,0)},out _,out _),"Distant palm captures bar");
            Require(CockpitTouch.NearBar(farFinger,new CockpitProbe {Start=new Vector3D(0,0,.08),End=new Vector3D(0,0,.02)},out _,out _),"Curled-finger end misses while palm is outside reach");
            Require(!CockpitTouch.NearBar(farFinger,new CockpitProbe {Start=new Vector3D(0,0,.09),End=new Vector3D(0,0,.05)},out _,out _),"Distant grasp cavity captures bar");
            var nearFinger=new CockpitProbe {Start=new Vector3D(0,0,.005),End=new Vector3D(0,0,.03),Tip=new Vector3D(0,0,.005)};
            Require(CockpitTouch.NearBar(nearFinger,null,out _,out _),"Fingertip capture requires a palm");
            var hover=new CockpitFeedback.ProximityPulse();
            Require(!hover.Sample(true,.2f,.1f) && hover.Sample(true,.099f,.1f),"Stick approach did not pulse once on entry");
            for(int i=0;i<120;i++) Require(!hover.Sample(true,i%2==0 ? .098f : .102f,.1f),"Grab-zone boundary jitter repeated hover feedback");
            Require(!hover.Sample(true,.12f,.1f) && hover.Sample(true,.09f,.1f),"Leaving the grab zone did not rearm hover");
            hover.Sample(false,0,.1f);
            Require(!hover.Sample(true,.09f,.1f,true) && !hover.Sample(true,.09f,.1f),"Held grip or release inside the zone added a hover pulse");
            hover.Sample(false,0,.1f);
            Require(hover.Sample(true,.09f,.1f),"Seat/focus reset retained old proximity");
            var head=Matrix.CreateFromYawPitchRoll(.8f,.3f,-.2f)*Matrix.CreateTranslation(1,2,3);
            Require(HelmetHud.NearTemple(Matrix.CreateTranslation(-.21f,0,0)*head,head,true),"Left helmet gesture lost its head-relative frame");
            Require(!HelmetHud.NearTemple(Matrix.CreateTranslation(.21f,0,0)*head,head,true),"Right temple toggles left helmet light");
            var world=(MatrixD)head*MatrixD.CreateTranslation(2e6,-3e6,4e6);
            var lamp=HelmetLight.Pose(world);
            Require(Vector3D.Distance(lamp.Translation,world.Translation+world.Up*.05)<.00001 && lamp.Forward==world.Forward && lamp.Up==world.Up,
                "Helmet beam lost headset direction or eye-relative light offset");
            log("PASS cockpit capture: deliberate trigger, partial/full release, no seat-to-power slide, 180-frame seat movement, pullaway/focus/tracking cancellation, firing ownership, independent hands; hinged travel and detents; gentle movement pulses; left temple and headset light transform.");
        }
        private static void GripPresses()
        {
            foreach(float preload in new[] {.3f,.5f,.7f})
            {
                var analog=new Analog(77,InteractionInput.GripThreshold);
                var hand=new CockpitTouch.Hand();
                void Sample(float value,string target,bool active=true,bool retain=false)
                {
                    analog.AcceptSample(new Valve.VR.InputAnalogActionData_t { bActive=active,x=value,activeOrigin=77 });
                    hand.Sample(active,new InteractionInput(true,true,value,value>InteractionInput.GripThreshold,analog.CanPress),
                        target,target==null ? -1:0,guarded:target!=null,retainSqueeze:retain);
                    if(hand.Consumed) analog.BlockUntilRelease(value>InteractionInput.GripThreshold);
                }
                // Approaching from a ray changes source while the fingers are already partly curled.
                hand.Sample(true,new InteractionInput(false,true,0,false,true),null,-1);
                Sample(preload,null); Sample(preload,"A");
                Require(!hand.Pressed && hand.Surface==null,"Partial grip clicked or attached before a firm squeeze");
                Sample(1,"A"); Require(hand.Pressed && hand.Surface=="A","Pre-grip approach lost the completed squeeze");
                Sample(1,"B"); Require(!hand.Pressed && hand.Surface=="A","Held grip slid onto another button");
                Sample(preload,"B"); Sample(1,"B");
                Require(hand.Pressed && hand.Surface=="B","Partial release failed to rearm the next button");
                Sample(1,"B",false); Sample(1,"B");
                Require(!hand.Pressed && hand.Surface==null,"Tracking return reused a full grip");
                Sample(preload,null); Sample(1,null); Sample(1,"B");
                Require(!hand.Pressed,"Already-full grip activated on entering a button");
                Sample(preload,"Lever"); Sample(1,"Lever");
                Sample(preload,"Other",retain:true);
                Require(hand.Surface=="Lever" && hand.Committed,"Relaxing a grabbed lever lost its ownership");
                Sample(0,"Other",retain:true);
                Require(hand.Surface==null,"Lever failed to release with the hand");
            }
        }
        private static void DualPresses()
        {
            foreach(bool firstGrip in new[] {false,true})
            {
                var grip=new Analog(81,InteractionInput.GripThreshold); var trigger=new Analog(82,.55f);
                var hand=new CockpitTouch.Hand();
                void Sample(float g,float t,string target,bool retain=false)
                {
                    grip.AcceptSample(new Valve.VR.InputAnalogActionData_t { bActive=true,x=g,activeOrigin=81 });
                    trigger.AcceptSample(new Valve.VR.InputAnalogActionData_t { bActive=true,x=t,activeOrigin=82 });
                    var input=new InteractionInput(true,g,t,grip.CanPress,trigger.CanPress);
                    hand.Sample(true,input,target,0,guarded:true,retainSqueeze:retain);
                    if(hand.Consumed) { grip.BlockUntilRelease(input.Down); trigger.BlockUntilRelease(input.Down); }
                }
                Sample(.5f,.2f,"A");
                Sample(firstGrip ? 1:.5f,firstGrip ? .2f:1,"A");
                Require(hand.Pressed && hand.Surface=="A","Either-input fingertip click was lost");
                Sample(1,1,"B");
                Require(!hand.Pressed && hand.Surface=="A","Second input double-clicked or transferred a held control");
                Sample(firstGrip ? 0:1,firstGrip ? 1:0,"B");
                Require(hand.Surface==null,"Releasing the original button failed to release the control");
                Sample(firstGrip ? 0:1,firstGrip ? 1:0,"B");
                Require(!hand.Pressed && hand.Surface==null,"Already-held alternate button recaptured after release");
                Sample(.5f,.2f,"B"); Sample(firstGrip ? .5f:1,firstGrip ? 1:.2f,"B");
                Require(hand.Pressed,"Fresh alternate input did not rearm");
                Sample(0,0,"Lever"); Sample(firstGrip ? 1:0,firstGrip ? 0:1,"Lever");
                Sample(firstGrip ? .3f:0,firstGrip ? 0:.3f,"Lever",true);
                Require(hand.Surface=="Lever" && hand.Committed,"Relaxing the owning input detached the lever");
                Sample(0,0,"Lever",true); Require(hand.Surface==null,"Dual-input lever did not release");
            }
            var press=new InteractionPress();
            press.Update(true,false,false,false,true);
            Require(!press.Update(true,false,true,false,true,true),"Grip clicked a distant ray");
            press.Update(true,true,false,false,true,true);
            Require(press.Update(true,true,false,true,true,true) && !press.Grip,"Near trigger failed to acquire window");
            Require(!press.Update(true,true,true,true,true,true) && !press.Grip,"Grip stole a trigger-owned window");
            Require(!press.Update(true,true,true,false,false,false) && !press.Held,"Alternate grip retained a released window");
        }
        private static void SoftCapture()
        {
            var onFoot=new CockpitTouch.Hand();
            foreach(float pressure in new[] {0f,.1f,.2f,.4f})
            {
                onFoot.Sample(true,pressure,false,"Button",0,reachable:false,softCapture:false);
                Require(!onFoot.Consumed && !onFoot.Captured,"On-foot trigger ramp captured before its full click");
            }
            onFoot.Sample(true,1,true,"Button",0,reachable:false,softCapture:false);
            Require(onFoot.Pressed && onFoot.Committed,"On-foot trigger ramp prevented the native button press");
            foreach(bool linear in new[] {false,true}) foreach(float start in new[] {0f,.5f,1f})
            {
                var hand=new CockpitTouch.Hand(); var drag=new ControlDrag();
                hand.Sample(true,0,false,"A",0);
                hand.Sample(true,.25f,false,"A",0);
                Require(hand.Captured && !hand.Committed && !hand.Pressed,"Early capture issued a button action");
                var radial=new Vector3(0,0,.035f);
                if(linear) drag.BeginLinear(Vector3.Zero,Vector3.Up,start,.03f);
                else drag.Begin(Vector3.Zero,radial,Vector3.Zero,Vector3.Right,start,1.05f);
                float end=start>.5f ? 0 : 1;
                var motion=linear ? Vector3.Up*((end-start)*.03f) : Vector3.Transform(radial,Matrix.CreateRotationX((end-start)*1.05f))-radial;
                bool initial=drag.State;
                hand.Sample(true,.14f,false,"B",1);
                Require(hand.Surface=="A" && !hand.Captured && !hand.Committed,"Soft capture jitter changed target or committed it");
                Require(drag.Move(motion,hand.Committed)==-1 && drag.State==initial && Math.Abs(drag.Value-end)<.0001,"Light squeeze changed the detent or lost preview movement");
                hand.Sample(true,1,true,"B",1,canAcquire:false);
                Require(hand.Pressed && hand.Committed && hand.Surface=="A","Owned light capture cannot commit after blocking flight input");
                Require(drag.Move(motion,hand.Committed)==(int)end,"Completing squeeze requires repeating the early gesture");
                Require(drag.Move(motion,hand.Committed)==-1,"Held completed gesture repeats action");
                hand.Sample(true,.2f,false,"B",1);
                Require(hand.Held<0 && hand.Consumed && !hand.Committed,"Partial release leaks ownership or retains commit");
            }
            var cancel=new CockpitTouch.Hand();
            cancel.Sample(true,0,false,"A",0); cancel.Sample(true,.22f,false,"A",0);
            cancel.Sample(true,.05f,false,"A",0);
            Require(cancel.Held<0 && !cancel.Pressed && cancel.Consumed,"Soft release activates or leaks firing");
            cancel.Sample(true,1,true,"A",0);
            Require(!cancel.Pressed && cancel.Held<0,"Soft cancellation reacquires before full release");
            cancel.Sample(true,0,false,"A",0); cancel.Sample(true,.22f,false,"A",0);
            cancel.Sample(false,.22f,false,"A",0); cancel.Sample(true,1,true,"A",0);
            Require(cancel.Held<0 && !cancel.Pressed,"Tracking return commits canceled soft capture");
            var grace=new CockpitTouch.HoverGrace();
            grace.Remember("A",2,Vector3D.Zero,new Vector3(0,.03f,0),10);
            Require(grace.TryGet(new Vector3D(.006,0,0),10.099,out int key,out var contact) && key==2 && contact.Y==.03f,"Recent nearby target missed grace capture");
            Require(!grace.TryGet(Vector3D.Zero,10.101,out _,out _) && grace.Surface==null,"Grace persists beyond 100ms");
            grace.Remember("A",2,Vector3D.Zero,Vector3.Zero,11);
            Require(!grace.TryGet(new Vector3D(.012,0,0),11.02,out _,out _) && !grace.TryGet(Vector3D.Zero,11.03,out _,out _),"Pullaway retains or resurrects stale target");
            grace.Remember("A",2,Vector3D.Zero,Vector3.Zero,12); grace.Remember("B",4,Vector3D.One,Vector3.One,12.01);
            Require(grace.Surface=="B" && grace.TryGet(Vector3D.One,12.02,out key,out _) && key==4,"New target loses to stale grace");
            grace.Reset(); Require(!grace.TryGet(Vector3D.One,12.03,out _,out _),"Context reset retains target grace");
            var world=MatrixD.CreateRotationY(.8)*MatrixD.CreateTranslation(2e6,-3e6,4e6);
            grace.Remember("A",0,Vector3D.Zero,Vector3.Zero,13);
            var local=Vector3D.Transform(Vector3D.Transform(new Vector3D(.006,0,0),world),MatrixD.Invert(world));
            Require(grace.TryGet(local,13.05,out _,out _),"Moving-world local grace drifted");
        }
        private static void NearMisses()
        {
            var panel=new SurfaceView { Pose=MatrixD.Identity,Width=.108f,Height=.120f,Keys=SeatPanel.Keys(true,true) };
            var miss=new CockpitProbe(MatrixD.CreateTranslation(.075,0,.02));
            Require(CockpitTouch.NearKey(panel,miss,out _,out _)<0 && CockpitPanelGuard.NearSurface(panel,miss),"Panel near miss is clickable or not protected");
            foreach(var point in new[] {new Vector3(.11f,0,.02f),new Vector3(0,0,.10f),new Vector3(0,0,-.11f),new Vector3(float.NaN,0,0)})
                Require(!CockpitPanelGuard.NearSurface(panel,new CockpitProbe(MatrixD.CreateTranslation(point))),"Control margin blocks distant or invalid input");
            foreach(var hand in new[] {new CockpitTouch.Hand(),new CockpitTouch.Hand()})
            {
                hand.Sample(true,0,false,null,-1);
                hand.Sample(true,1,true,null,-1,guarded:true);
                Require(hand.Consumed && !hand.Pressed && hand.Surface==null,"Near miss fires or activates a button");
                hand.Sample(true,1,true,"Seat",9,guarded:true);
                Require(hand.Consumed && !hand.Pressed && hand.Surface==null,"Held near miss slides onto Power");
                hand.Sample(true,.1f,false,null,-1);
                Require(hand.Consumed,"Partial near-miss release resumes firing");
                hand.Sample(false,1,true,null,-1);
                hand.Sample(true,1,true,"Seat",7,guarded:true);
                Require(hand.Consumed && !hand.Pressed,"Focus return turns near miss into a click");
                hand.Sample(true,0,false,"Seat",7);
                Require(!hand.Consumed,"Released near miss retains flight ownership");
                for(int i=0;i<4;i++)
                {
                    hand.Sample(true,1,true,"Seat",7,guarded:true);
                    Require(hand.Pressed && hand.Held==7,"Repeated lock button clicks do not rearm");
                    hand.Sample(true,0,false,"Seat",7);
                }
                hand.Sample(true,1,true,null,-1);
                hand.Sample(true,1,true,null,-1,guarded:true);
                Require(!hand.Consumed && !hand.Pressed,"Firing gesture crossing a panel is stolen");
                hand.Sample(true,0,false,null,-1);
                hand.Sample(true,.1f,false,null,-1,canAcquire:false);
                hand.Sample(true,.2f,false,"Seat",7,canAcquire:false,guarded:true);
                hand.Sample(true,1,true,"Seat",7,guarded:true);
                Require(hand.Pressed && hand.Consumed,"Light squeeze entering a panel before the firing threshold cannot click");
                hand.Sample(true,0,false,null,-1);
                hand.Sample(true,1,true,"Seat",7,canAcquire:false,guarded:true);
                Require(!hand.Consumed && !hand.Pressed,"Near-miss margin bypasses a blocked native trigger");
                hand.Sample(true,0,false,"Seat",7);
                hand.Sample(true,.2f,false,"Seat",7,guarded:true);
                Require(hand.Consumed && !hand.Pressed,"Left-trigger ramp thrusts before a cockpit click");
                hand.Sample(true,1,true,"Seat",7,guarded:true);
                Require(hand.Pressed && hand.Held==7,"Protected trigger ramp prevents its intended click");
                hand.Sample(true,0,false,null,-1);
                hand.Sample(true,.2f,false,null,-1,guarded:true);
                hand.Sample(true,.4f,false,null,-1);
                Require(hand.Consumed,"Leaving the panel mid-squeeze resumes thrust");
                hand.Sample(false,.4f,false,null,-1);
                hand.Sample(true,1,true,"Seat",7,guarded:true);
                Require(hand.Consumed && !hand.Pressed,"Tracking interruption rearms a reserved squeeze");
            }
        }
    }
}
