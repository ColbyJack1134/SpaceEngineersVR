#if SEVR_DIAGNOSTICS
using System;
using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class GrabTests
    {
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
        public static void Run(Action<string> log)
        {
            // Spring: pulls toward the hand, saturates at the cap, damps relative motion, rejects invalid input.
            var pull=GridGrab.Spring(new Vector3(.1f,0,0),Vector3.Zero,100,GridGrab.StandingForce,out float saturation);
            Require(pull.X>0 && Math.Abs(pull.Y)<1e-3f && saturation>.3f && saturation<.5f,"Spring does not pull toward a 10 cm offset at 40% demand");
            var capped=GridGrab.Spring(new Vector3(0,-2,0),Vector3.Zero,50000,GridGrab.StandingForce,out saturation);
            Require(Math.Abs(capped.Length()-GridGrab.StandingForce)<.5f && saturation>1,"Spring exceeds the force cap or does not report saturation");
            var damped=GridGrab.Spring(Vector3.Zero,new Vector3(0,0,1),1000,GridGrab.StandingForce,out _);
            Require(damped.Z<0 && damped.Length()<=GridGrab.StandingForce/2+.5f,"Damping does not oppose relative motion within half the cap");
            Require(GridGrab.Spring(new Vector3(float.NaN,0,0),Vector3.Zero,1,1,out _)==Vector3.Zero,"Invalid spring input accepted");
            Require(GridGrab.ForceCap(true)<GridGrab.ForceCap(false),"Jetpack force cap is not lower than standing");
            Require(GridGrab.WithinSize(new Vector3I(-3,0,0),new Vector3I(16,2,1)) && !GridGrab.WithinSize(new Vector3I(0,0,0),new Vector3I(20,0,0)),"Grid size cap is off by one");
            // Message round trip and rejection.
            Require(GridGrab.Decode(GridGrab.Encode(true,new Vector3(1.25f,-.5f,3)),out bool left,out var local) && left && local.HasValue && Vector3.Distance(local.Value,new Vector3(1.25f,-.5f,3))<1e-6f,"Grab message round trip failed");
            Require(GridGrab.Decode(GridGrab.EncodeRelease(false),out left,out local) && !left && !local.HasValue,"Release message round trip failed");
            Require(!GridGrab.Decode("X;1;2;3",out _,out _) && !GridGrab.Decode("L;1;2",out _,out _) && !GridGrab.Decode("L;NaN;0;0",out _,out _) && !GridGrab.Decode("R;100;0;0",out _,out _),"Malformed grab message accepted");
            log("PASS hand grab: spring pull, cap and damping, jetpack cap, size cap, grab/release message round trip and rejection.");
        }
    }
}
#endif
