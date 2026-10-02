using System;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class KeyboardInputTests
    {
        public static void Run(Action<string> log)
        {
            var touch=new KeyboardContact();
            var trigger=new CockpitTouch.Hand();
            Func<float,int,bool,int> sample=(depth,key,down)=> {
                trigger.Sample(true,down ? 1:0,down,key>=0 ? "Keyboard":null,key,guarded:key>=0,softCapture:false);
                return touch.Update(new Vector3(0,0,depth),depth<.07f ? key:-1,
                    trigger.Pressed ? trigger.Held:-1,down || trigger.Consumed);
            };
            Action<int,int,string> expect=(actual,wanted,message)=> {
                if(actual!=wanted) throw new Exception(message+": "+actual+" instead of "+wanted);
            };
            expect(sample(.04f,3,false),-1,"Approach typed");
            expect(sample(.01f,3,false),3,"Poke lost");
            expect(sample(.01f,3,true),-1,"Trigger duplicated poke");
            expect(sample(.01f,3,true),-1,"Held trigger repeated");
            expect(sample(.01f,3,false),-1,"Release repeated");
            expect(sample(.01f,3,true),3,"Fresh trigger re-press lost at same contact");
            sample(.01f,3,false);
            expect(sample(.01f,3,true),3,"Repeated backspace-style trigger lost");
            sample(.04f,3,false);
            expect(sample(.01f,3,false),3,"Repeated letter after withdrawal lost");
            sample(.04f,4,false);
            expect(sample(.02f,4,true),4,"Trigger-first press lost");
            expect(sample(.01f,4,true),-1,"Poke duplicated trigger");
            expect(sample(.01f,4,false),-1,"Trigger release at surface typed");
            expect(sample(-.04f,4,false),-1,"Deep contact typed");
            expect(sample(.01f,4,false),-1,"Deep contact return rearmed");
            sample(.04f,4,false);
            expect(sample(.01f,4,false),4,"Poke after trigger withdrawal lost");
            sample(.5f,5,false);
            expect(sample(.5f,5,true),5,"Ray click lost");
            expect(sample(.5f,6,true),-1,"Ray drag typed another key");
            sample(.5f,6,false);
            expect(sample(.5f,6,true),6,"Second ray click lost");
            touch.Reset(); trigger.Reset();
            expect(sample(.01f,2,true),-1,"Held input crossed keyboard reset");
            sample(.04f,2,false);
            expect(sample(.01f,2,false),2,"Reset failed to rearm");
            log("PASS keyboard: poke/trigger deduplication, held contact, withdrawal/repeated letters, deep contact, ray clicks and reset");
            log("Sound and haptic perception require a headset check.");
        }
    }
}
