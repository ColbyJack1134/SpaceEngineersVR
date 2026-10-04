using System;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class KeyboardInputTests
    {
        public static void Run(Action<string> log)
        {
            FarClicks(log);
            Repeat(log);
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
        private static void Repeat(Action<string> log)
        {
            var start=new DateTime(2026,10,4);
            var left=new KeyboardRepeat(); var right=new KeyboardRepeat();
            int backspace=Array.FindIndex(MenuKeyboard.Keys,k=>k.Label=="BKSP");
            void Expect(int actual,int expected) { if(actual!=expected) throw new Exception("Keyboard repeat: "+actual+" != "+expected); }
            Expect(left.Update(backspace,backspace,start),backspace);
            Expect(right.Update(3,3,start.AddMilliseconds(500)),3);
            Expect(left.Update(-1,backspace,start.AddMilliseconds(999)),-1);
            Expect(left.Update(-1,backspace,start.AddSeconds(1)),backspace);
            Expect(right.Update(-1,3,start.AddSeconds(1)),-1);
            Expect(left.Update(-1,backspace,start.AddMilliseconds(1099)),-1);
            Expect(left.Update(-1,backspace,start.AddMilliseconds(1100)),backspace);
            Expect(right.Update(-1,3,start.AddMilliseconds(1500)),3);
            Expect(left.Update(-1,-1,start.AddSeconds(2)),-1);
            Expect(left.Update(-1,backspace,start.AddSeconds(3)),-1);
            Expect(right.Update(-1,4,start.AddSeconds(3)),-1);
            Expect(right.Update(-1,3,start.AddSeconds(4)),-1);
            Expect(left.Update(backspace,backspace,start.AddSeconds(4)),backspace);
            Expect(left.Update(-1,backspace,start.AddSeconds(9)),backspace);
            Expect(left.Update(-1,backspace,start.AddSeconds(9)),-1);
            left.Reset();
            Expect(left.Update(-1,backspace,start.AddSeconds(10)),-1);
            foreach(var label in new[] {"DONE","⇧ #@"})
            {
                int key=Array.FindIndex(MenuKeyboard.Keys,k=>k.Label==label);
                if(MenuKeyboard.Repeatable(key)) throw new Exception("Keyboard repeats a toggle or Done");
                Expect(left.Update(key,-1,start),key);
                Expect(left.Update(-1,-1,start.AddSeconds(2)),-1);
            }
            if(!MenuKeyboard.Repeatable(backspace) || !MenuKeyboard.Repeatable(3) ||
                !MenuKeyboard.Repeatable(Array.FindIndex(MenuKeyboard.Keys,k=>k.Label=="SPACE")))
                throw new Exception("Text keys cannot repeat");
            log("PASS keyboard repeat: delay, cadence, independent hands, release, key change, reset, no hitch burst, text/backspace only");
        }
        private static void FarClicks(Action<string> log)
        {
            foreach(bool right in new[] {false,true})
            {
                var button=new Button(91); var menuButton=new Button(93); var pressure=new Analog(92,.55f);
                var click=new TriggerClick(button,pressure); var menuClick=new TriggerClick(menuButton,pressure); var hand=new CockpitTouch.Hand();
                bool down=false;
                void Sample(float value,bool active=true)
                {
                    // SteamVR button-mode hysteresis from the shipped trigger bindings.
                    down=active && value>(down ? .20f:.25f);
                    var digital=new InputDigitalActionData_t { bActive=active,bState=down };
                    button.AcceptSample(digital); menuButton.AcceptSample(digital);
                    pressure.AcceptSample(new InputAnalogActionData_t { bActive=active,x=value,activeOrigin=92 });
                    hand.Sample(active,click.Read(right),"Keyboard",3,guarded:true,softCapture:false);
                    if(hand.Consumed) click.Consume(false,click.Down);
                }
                Sample(1);
                if(hand.Pressed || menuClick.Pressed) throw new Exception("Held trigger crossed startup");
                Sample(0);
                for(int repeat=0;repeat<5;repeat++)
                {
                    Sample(.24f);
                    if(hand.Pressed || menuClick.Pressed) throw new Exception("Trigger clicked below the press threshold");
                    Sample(.26f);
                    if(!hand.Pressed || !menuClick.Pressed || !menuClick.Held) throw new Exception("Light trigger click or partial-release rearm lost: right="+right+"; repeat="+repeat);
                    foreach(float value in new[] {1f,.26f,.24f,.21f})
                    {
                        Sample(value);
                        if(hand.Pressed || menuClick.Pressed || !click.Down) throw new Exception("Held trigger repeated or released inside hysteresis");
                    }
                    Sample(.19f);
                    if(click.Down || hand.Pressed) throw new Exception("Trigger failed to release below 0.20");
                }
                Sample(1,false); Sample(1);
                if(hand.Pressed || menuClick.Pressed) throw new Exception("Tracking return reused a held trigger");
                Sample(.19f); Sample(.26f);
                if(!hand.Pressed || !menuClick.Pressed) throw new Exception("Trigger failed to rearm after tracking return");
            }
            log("PASS left/right far clicks: 0.25 press, 0.20 release, five partial-release clicks, consumed holds, startup and tracking gates");
        }
    }
}
