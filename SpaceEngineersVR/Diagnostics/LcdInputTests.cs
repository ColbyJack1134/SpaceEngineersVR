using System;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using Valve.VR;

namespace SpaceEngineersVR.Diagnostics
{
    public static class LcdInputTests
    {
        internal static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        internal static LcdInput.Bindings ControlsForTest()
        {
            var controls=new LcdInput.Bindings {
                Primary=new Button(0),
                LeftClick=new Button(0),
                Secondary=new Button(0),
                Interact=new Button(0),
                ThrustRoll=new Button(0),
                JumpOrClimbUp=new Button(0),
                CrouchOrClimbDown=new Button(0),
                PointerPressure=new Analog(0,.55f),
                LeftTriggerPressure=new Analog(0,.55f),
                RightGripPressure=new Analog(0,InteractionInput.GripThreshold),
                LeftGripPressure=new Analog(0,InteractionInput.GripThreshold),
                ThrustRotate=new Analog(0,0),
                WalkRotate=new Analog(0,0),
                ThrustLRFB=new Analog(0,0),
                WalkLongitudinal=new Analog(0,0),
                ThrustUp=new Analog(0,0),
                ThrustForward=new Analog(0,0),
                ThrustDown=new Analog(0,0),
            };
            Poll(controls,0,0);
            return controls;
        }
        internal static void Poll(LcdInput.Bindings c,float trigger,float grip,bool right=true,float axis=0,bool alias=false)
        {
            var button=new InputDigitalActionData_t { bActive=true,bState=trigger>=.25f };
            (right ? c.Primary:c.LeftClick).AcceptSample(button);
            c.Interact.AcceptSample(new InputDigitalActionData_t { bActive=true,bState=alias && button.bState });
            (right ? c.PointerPressure:c.LeftTriggerPressure).AcceptSample(new InputAnalogActionData_t { bActive=true,x=trigger });
            (right ? c.RightGripPressure:c.LeftGripPressure).AcceptSample(new InputAnalogActionData_t { bActive=true,x=grip });
            if(right)
            {
                c.Secondary.AcceptSample(new InputDigitalActionData_t { bActive=true,bState=grip>.8f });
                c.ThrustRoll.AcceptSample(new InputDigitalActionData_t { bActive=true,bState=grip>.8f });
                c.WalkRotate.AcceptSample(new InputAnalogActionData_t { bActive=true,y=axis });
                c.ThrustRotate.AcceptSample(new InputAnalogActionData_t { bActive=true,y=axis });
            }
            else
            {
                c.ThrustUp.AcceptSample(new InputAnalogActionData_t { bActive=true,x=trigger });
                c.ThrustForward.AcceptSample(new InputAnalogActionData_t { bActive=true,x=trigger });
                c.ThrustDown.AcceptSample(new InputAnalogActionData_t { bActive=true,x=grip });
                c.JumpOrClimbUp.AcceptSample(button);
                c.CrouchOrClimbDown.AcceptSample(new InputDigitalActionData_t { bActive=true,bState=grip>.8f });
                c.WalkLongitudinal.AcceptSample(new InputAnalogActionData_t { bActive=true,y=axis });
                c.ThrustLRFB.AcceptSample(new InputAnalogActionData_t { bActive=true,y=axis });
            }
        }
        public static void Run(Action<string> log)
        {
            var now=DateTime.UtcNow;
            var rightPose=new LcdPointing(); var leftPose=new LcdPointing();
            rightPose.Update(true,true,now); leftPose.Update(true,true,now);
            for(int frame=1;frame<=60;frame++)
            {
                var time=now.AddMilliseconds(frame*16);
                rightPose.Update(true,frame%2==0,time); leftPose.Update(true,frame%2!=0,time);
                Require(rightPose.Active(time) && leftPose.Active(time),"LCD cursor winner or intermittent hit changed a pointing hand pose");
            }
            Require(!rightPose.Active(now.AddMilliseconds(1200)),"Pointing pose remained after leaving the LCD");
            leftPose.Update(false,false,now.AddMilliseconds(961));
            Require(!leftPose.Active(now.AddMilliseconds(961)),"Tracking/menu/physical ownership loss retained pointing");
            rightPose.Update(true,true,now); rightPose.Reset();
            Require(!rightPose.Active(now),"LCD reset retained pointing pose");
            log("PASS LCD hand pose: independent hands, alternating hits, bounded miss grace, immediate unavailable/reset release.");
            foreach(bool near in new[] {false,true}) foreach(bool right in new[] {false,true})
            {
                var c=ControlsForTest(); var input=new LcdInput();
                foreach(float value in new[] {0,.1f,.3f,.8f,.8f,0})
                {
                    Poll(c,value,0,right,alias:true);
                    input.Update(input.Read(c,right,false),true,near,now);
                    input.Consume(c,right);
                    Require(!(right ? c.Primary:c.LeftClick).IsPressed,"LCD trigger escaped to BeginShoot/Use");
                    Require(!right || !c.Interact.HasPressed,"Shared Interact alias escaped LCD consumption");
                    Require(!near || value!=.3f || !input.Primary,"Near trigger ignored its deliberate squeeze threshold");
                    Require(value!=.8f || input.Primary,"Consumed raw trigger failed to hold native mouse down");
                }
                Require(!input.Primary,"Trigger release retained mouse down");
                Poll(c,0,.1f,right);
                input.Update(input.Read(c,right,false),true,near,now); input.Consume(c,right);
                Poll(c,0,.9f,right);
                input.Update(input.Read(c,right,false),true,near,now);
                input.Consume(c,right);
                Require(input.Secondary && !input.Primary,"Grip did not remain secondary at both distances");
                Require(right ? !c.Secondary.IsPressed && !c.ThrustRoll.IsPressed : !c.CrouchOrClimbDown.IsPressed && c.ThrustDown.Position.X==0,"Grip aliases escaped to gameplay");
                input.Cancel();
                Poll(c,0,.9f,right);
                input.Update(input.Read(c,right,false),false,near,now); input.Consume(c,right);
                Require(!input.Secondary && input.ReserveGrip,"Target loss released grip to gameplay");
                input.Update(input.Read(c,right,false),true,near,now);
                Require(!input.Secondary,"Held grip transferred onto another LCD");
                Poll(c,0,0,right); input.Update(input.Read(c,right,false),true,near,now);
                Poll(c,0,.9f,right); input.Update(input.Read(c,right,false),true,near,now);
                Require(input.Secondary,"Grip failed to rearm after release");
            }
            foreach(bool near in new[] {false,true})
            {
                var c=ControlsForTest(); var input=new LcdInput();
                Poll(c,.1f,0);
                input.Update(input.Read(c,true,false),true,near,now); input.Consume(c,true);
                Poll(c,.8f,0);
                input.Update(input.Read(c,true,false),true,near,now); input.Consume(c,true);
                Require(input.Primary && !c.Primary.IsPressed,"Light-squeeze target acquisition failed to arm the full click");
            }
            var controls=ControlsForTest(); var state=new LcdInput();
            Poll(controls,.8f,0);
            state.Update(state.Read(controls,true,false),true,false,now);
            Require(!state.Primary && state.ReserveTrigger,"Held entry acquired a primary click");
            state.Cancel();
            state.Update(new LcdInput.Sample(false,0,false,0,false,false,triggerActive:false),false,false,now);
            Require(state.ReserveTrigger,"Inactive input was mistaken for physical release");
            Poll(controls,0,0); state.Update(state.Read(controls,true,false),true,false,now);
            Poll(controls,.8f,.9f); controls.Primary.BlockUntilRelease(); controls.Secondary.BlockUntilRelease();
            state.Update(state.Read(controls,true,false),true,false,now);
            Require(!state.Primary && !state.Secondary,"Previously consumed digital input acquired an LCD click");
            state.Cancel();
            Poll(controls,0,0); state.Update(state.Read(controls,true,false),true,true,now);
            state.Update(state.Read(controls,true,false),true,true,now);
            Require(!state.Primary && !state.Pressed,"Proximity alone pressed the LCD");
            for(int i=0;i<3;i++)
            {
                Poll(controls,.8f,0); state.Update(state.Read(controls,true,false),true,true,now); state.Consume(controls,true);
                Require(state.Primary && state.Pressed,"Near trigger re-press was lost");
                Poll(controls,0,0); state.Update(state.Read(controls,true,false),true,true,now); state.Consume(controls,true);
                Require(!state.Primary,"Trigger release retained mouse-down while still near the LCD");
            }
            state.Cancel();
            Poll(controls,0,0); state.Update(state.Read(controls,true,false),true,false,now);
            Poll(controls,.1f,0,axis:1); state.Update(state.Read(controls,true,false),true,false,now); state.Consume(controls,true);
            Require(controls.WalkRotate.Position.Y==0 && state.TakeScroll()==120 && state.TakeScroll()==0,"Scroll failed reservation or dispatched twice");
            state.Cancel();
            state.Update(state.Read(controls,true,false),true,false,now.AddSeconds(1));
            Require(state.TakeScroll()==0,"Held scroll transferred after cancellation");
            var noScroll=ControlsForTest(); var screenInput=new LcdInput();
            Poll(noScroll,0,0); screenInput.Update(screenInput.Read(noScroll,true,false,scrollEnabled:false),true,false,now);
            Poll(noScroll,.1f,0,axis:1); screenInput.Update(screenInput.Read(noScroll,true,false,scrollEnabled:false),true,false,now); screenInput.Consume(noScroll,true);
            Require(screenInput.TakeScroll()==0 && noScroll.WalkRotate.Position.Y!=0,"TouchScreenAPI acquired Arthur-only scroll input");
            string fixture=Environment.GetEnvironmentVariable("SEVR_TOUCH_API_FIXTURE");
            if(!string.IsNullOrEmpty(fixture))
            {
                var type=System.Reflection.Assembly.LoadFrom(fixture).GetType("Lima.Touch.ButtonState",true);
                var mouse=Activator.CreateInstance(type); var update=type.GetMethod("Update");
                var c=ControlsForTest(); var input=new LcdInput();
                foreach(float value in new[] {0f,.8f,0f,.8f,0f})
                {
                    Poll(c,value,0); input.Update(input.Read(c,true,false,scrollEnabled:false),true,true,now); input.Consume(c,true);
                    update.Invoke(mouse,new object[] {input.Primary,true});
                    Require((bool)type.GetProperty("IsPressed").GetValue(mouse)==(value>.55f),"TouchScreenAPI mouse-down did not follow the trigger");
                    if(value==0) Require(!input.Primary,"TouchScreenAPI retained mouse-down inside the screen");
                }
                Require((bool)type.GetProperty("JustReleased").GetValue(mouse),"TouchScreenAPI lost trigger release while still inside the screen");
                log("PASS actual TouchScreenAPI trigger-only input: repeated near presses and immediate inside-screen release.");
            }
            log("PASS shared LCD input routes: light-squeeze acquisition, digital/near threshold gap, consumed controls and shared Interact, both-hand secondary aliases, held entry/cancel/rearm, inactive input, trigger-only proximity, repeated near clicks/release and scroll reservation.");
        }
    }
}
