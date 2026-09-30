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
                Require(!hand.Consumed,"Partial squeeze stole flight input");
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
            foreach(float start in new[] {0f,1f})
            {
                var drag=new HingeDrag(); var radial=new Vector3(0,0,.035f);
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
            var lamp=HelmetLight.Pose(world,new Vector3(0,.15f,0));
            Require(Vector3D.Distance(lamp.Translation,world.Translation+world.Up*.15)<.00001 && lamp.Forward==world.Forward && lamp.Up==world.Up,
                "Helmet beam lost headset direction or native light offset");
            log("PASS cockpit capture: deliberate trigger, partial/full release, no seat-to-power slide, 180-frame seat movement, pullaway/focus/tracking cancellation, firing ownership, independent hands; hinged travel and detents; gentle movement pulses; left temple and headset light transform.");
        }
    }
}
