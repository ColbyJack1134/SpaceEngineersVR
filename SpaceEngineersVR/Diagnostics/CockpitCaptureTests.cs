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
            NearMisses();
            SoftCapture();
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
            foreach(float start in new[] {0f,1f})
            {
                var drag=new ControlDrag();
                var axis=CockpitBarGeometry.Normal;
                var origin=CockpitBarGeometry.Front;
                drag.BeginLinear(origin,axis,start,CockpitBarGeometry.Travel);
                Require(drag.Move(origin)==-1 && drag.Value==start,"Pull bar activates on capture");
                foreach(float value in new[] {.5f,.8f,.55f,.45f,.2f,1f,0f})
                {
                    bool before=drag.State;
                    var hand=origin+axis*((value-start)*CockpitBarGeometry.Travel);
                    int changed=drag.Move(hand);
                    Require(Math.Abs(drag.Value-value)<.0001f,"Pull bar lost linear hand travel");
                    bool expected=before ? value>.35f : value>=.65f;
                    Require(changed==(expected==before ? -1 : expected ? 1 : 0),"Pull bar repeats or misses a detent");
                    Require(drag.Move(hand)==-1,"Held pull bar repeats its action/haptic");
                }
                drag.BeginLinear(origin,axis,start,CockpitBarGeometry.Travel);
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
            foreach(var point in new[] {new Vector3(.11f,0,.02f),new Vector3(0,0,.10f),new Vector3(0,0,-.08f),new Vector3(float.NaN,0,0)})
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
