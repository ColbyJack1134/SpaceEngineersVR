using System;
using System.Collections.Generic;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class LocomotionInputTests
    {
        private static InputAnalogActionData_t Sample(ulong origin, float forward, bool active = true, float horizontal = 0) =>
            new InputAnalogActionData_t { bActive = active, activeOrigin = origin, x = horizontal, y = forward };

        public static void Run(Action<string> log)
        {
            var character = new object();
            var characterModes = new[] { InputMode.Walking, InputMode.Jetpack, InputMode.Building };
            foreach (InputMode before in Enum.GetValues(typeof(InputMode)))
                foreach (InputMode after in Enum.GetValues(typeof(InputMode)))
                {
                    bool expected = Array.IndexOf(characterModes, before) >= 0 && Array.IndexOf(characterModes, after) >= 0;
                    bool toolTransfer=(before==InputMode.Walking || before==InputMode.Jetpack) && (after==InputMode.Walking || after==InputMode.Jetpack);
                    if(InputRouter.CanContinueHeldItem(before,after,character,character)!=toolTransfer ||
                        InputRouter.CanContinueHeldItem(before,after,character,new object()))
                        throw new Exception("Held fire crossed an equipment, menu or control boundary");
                    if (InputRouter.CanContinueLocomotion(before, after, character, character) != expected ||
                        InputRouter.CanContinueLocomotion(before, after, character, new object()) ||
                        InputRouter.CanContinueLocomotion(before, after, null, null))
                        throw new Exception("Locomotion transfer crossed a menu, tracking, cockpit or owner boundary");
                }

            var trigger=new Button(8); var pressure=new Analog(9,.55f); var modifier=new Analog(10,.5f);
            var released=new InputDigitalActionData_t {bActive=true,activeOrigin=31};
            trigger.AcceptSample(released); pressure.AcceptSample(Sample(31,0)); modifier.AcceptSample(Sample(32,0));
            var squeezed=released; squeezed.bState=true; trigger.AcceptSample(squeezed);
            pressure.AcceptSample(Sample(31,0,horizontal:1)); modifier.AcceptSample(Sample(32,0,horizontal:1));
            Controls.BlockFireInput(trigger,pressure,modifier,true);
            trigger.AcceptSample(squeezed); pressure.AcceptSample(Sample(31,0,horizontal:1)); modifier.AcceptSample(Sample(32,0,horizontal:1));
            if(!trigger.IsPressed || !pressure.CanPress || !modifier.CanPress) throw new Exception("Jetpack transition lost held fire or tool alternate modifier");
            Controls.BlockFireInput(trigger,pressure,modifier,false); trigger.AcceptSample(squeezed);
            if(trigger.IsPressed || pressure.CanPress || modifier.CanPress) throw new Exception("Held fire escaped a full input reset");
            trigger.AcceptSample(released); trigger.AcceptSample(squeezed);
            if(!trigger.IsPressed) throw new Exception("Trigger did not rearm after release");
            log("PASS held fire: jetpack continuity preserves trigger and alternate modifier; full resets still require release.");

            // Different actions, same physical stick. The flight action was inactive
            // throughout walking; merely removing the router's reset would not fix it.
            var walk = new Analog(1);
            var flight = new Analog(2);
            walk.AcceptSample(Sample(17, 0));
            walk.AcceptSample(Sample(17, 1));
            flight.AcceptSample(Sample(0, 0, false));
            var origins = new HashSet<ulong> { walk.HeldOrigin };
            walk.BlockUntilRelease();
            flight.BlockUntilRelease();
            flight.AcceptSample(Sample(17, 1), origins);
            for (int frame = 0; frame < 600; frame++)
            {
                if (flight.Position.Y != 1 || FlightAxes.Translation(Vector2.Zero, flight.Position, 0, 0, 0, 0).Z != -1)
                    throw new Exception("Forward thrust was interrupted when enabling the jetpack");
                flight.AcceptSample(Sample(17, 1));
            }
            origins.Clear();
            origins.Add(flight.HeldOrigin);
            flight.BlockUntilRelease();
            walk.AcceptSample(Sample(17, 1), origins);
            if (walk.Position.Y != 1) throw new Exception("Disabling the jetpack interrupted walking");
            walk.AcceptSample(Sample(17, 0));
            if (walk.Position != Vector2.Zero) throw new Exception("Transferred movement survived stick release");

            // Menu/death/tracking/owner reset supplies no transfer, even for the same stick.
            walk.AcceptSample(Sample(17, 1));
            walk.BlockUntilRelease();
            if (walk.HeldOrigin != 0) throw new Exception("Blocked movement remained eligible for transfer");
            walk.AcceptSample(Sample(17, 1));
            if (walk.Position != Vector2.Zero) throw new Exception("Held movement escaped a full reset");
            walk.AcceptSample(Sample(17, 0));
            walk.AcceptSample(Sample(17, 1));
            if (walk.Position.Y != 1) throw new Exception("Neutral failed to rearm walking");

            foreach (var sample in new[] { Sample(18, 1), Sample(0, 1), Sample(17, 1, false) })
            {
                flight.BlockUntilRelease();
                flight.AcceptSample(sample, origins);
                if (flight.Position != Vector2.Zero) throw new Exception("Unrelated or unavailable source inherited movement");
            }
            flight.AcceptSample(Sample(17, 1));
            if (flight.Position != Vector2.Zero) throw new Exception("Inactive transfer armed a later sample");

            foreach (float turn in new[] { -0.65f, 0.65f })
            {
                var walkRotate = new Analog(3);
                var flightRotate = new Analog(4);
                var held = Sample(23, 0.5f, horizontal: turn);
                walkRotate.AcceptSample(Sample(23, 0));
                walkRotate.AcceptSample(held);
                float walkingYaw = walkRotate.Position.X * 10;
                origins = new HashSet<ulong> { walkRotate.HeldOrigin };
                walkRotate.BlockUntilRelease();
                flightRotate.AcceptSample(held, origins);
                FlightAxes.Rotation(flightRotate.Position, false, false, 10, 1, out Vector2 rotation, out float roll);
                if (rotation.Y != walkingYaw || rotation.X != VrMath.Deadzone(held.y) * 10 || roll != 0)
                    throw new Exception("Held rotation stick lost yaw/pitch when enabling jetpack");
                origins = new HashSet<ulong> { flightRotate.HeldOrigin };
                flightRotate.BlockUntilRelease();
                walkRotate.AcceptSample(held, origins);
                if (walkRotate.Position.X * 10 != walkingYaw)
                    throw new Exception("Held yaw changed when disabling jetpack");
                walkRotate.BlockUntilRelease();
                walkRotate.AcceptSample(held);
                if (walkRotate.Position != Vector2.Zero)
                    throw new Exception("Held turn escaped a full reset");
            }
            // Native wheels steer from X and drive forward on negative Z.
            if (DriveInput.Mix(new Vector3(1, 1, -.3f), false) != new Vector3(1, 1, 0) ||
                DriveInput.Mix(new Vector3(-.6f, 0, .7f), false) != new Vector3(-.6f, 0, .7f) ||
                DriveInput.Mix(new Vector3(.8f, 0, .7f), true) != new Vector3(.8f, 0, -1))
                throw new Exception("Wheels must steer from the left stick, ignore throttle drift and drive forward from the trigger");
            log("PASS wheel input: left stick steers, steering drift does not throttle, trigger drives forward");
            log("PASS locomotion continuity: held forward and left/right yaw through jetpack on/off, flight pitch, 600 held frames, source matching, release and reset boundaries");
        }
    }
}
