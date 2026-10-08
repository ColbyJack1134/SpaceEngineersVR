using System;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using Valve.VR;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class WindowFocusTests
    {
        internal static void Run(Action<string> log)
        {
            void Require(bool value,string message) { if(!value) throw new Exception(message); }
            foreach(bool focusedAfterRequest in new[] {false,true})
            {
                var focus=new FocusTrigger();
                var button=new Button(91); var analog=new Analog(92,.55f);
                bool down=false;
                bool Sample(float pressure,bool focused,bool eligible=true,bool active=true)
                {
                    down=active && pressure>(down ? .20f:.25f);
                    var digital=new InputDigitalActionData_t {bActive=active,bState=down};
                    var value=new InputAnalogActionData_t {bActive=active,x=pressure,activeOrigin=92};
                    bool request=focus.Update(eligible,focused,active,down);
                    button.AcceptSample(digital);
                    analog.AcceptSample(value);
                    if(focus.Suppress) { WindowFocus.Suppress(button); WindowFocus.Suppress(analog); }
                    return request;
                }
                Require(!Sample(1,false),"Held trigger restored focus on startup");
                Sample(.19f,false);
                Require(Sample(.26f,false),"Fresh trigger did not request focus");
                Require(!button.HasPressed && !button.RawPressed && analog.RawPosition.X==0,"Focus squeeze reached gameplay or physical UI");
                Require(!Sample(1,focusedAfterRequest) && !button.IsPressed && analog.Position.X==0,"Held focus squeeze escaped suppression");
                Sample(1,focusedAfterRequest,false,false);
                Require(!Sample(1,focusedAfterRequest) && !button.RawPressed,"Tracking/overlay return reused focus squeeze");
                Sample(.21f,focusedAfterRequest);
                Require(!button.RawPressed,"Trigger rearmed above release threshold");
                Sample(.19f,focusedAfterRequest);
                Require(!button.HasPressed,"Partial release activated input");
                bool second=Sample(.26f,focusedAfterRequest);
                Require(focusedAfterRequest ? !second && button.HasPressed : second && !button.HasPressed,"Partial release did not rearm click or retry denied focus");
            }
            var overlay=new FocusTrigger();
            overlay.Update(false,false,true,false);
            Require(!overlay.Update(false,false,true,true),"Overlay trigger requested focus");
            Require(!overlay.Update(true,false,true,true),"Overlay close reused held trigger");
            overlay.Update(true,false,true,false);
            Require(overlay.Update(true,false,true,true),"Fresh press after overlay failed");
            log("PASS window focus: fresh trigger only, dashboard/tracking gates, held squeeze suppression, partial release, successful focus and denied retry");
        }
    }
}
