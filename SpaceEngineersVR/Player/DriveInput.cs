using System;
using Sandbox.Game.Entities;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class DriveInput
    {
        // Native wheel throttle is on/off, so a stick drifting forward while steering would be full throttle.
        internal const float StickThrottle=.5f;
        public static bool Braking { get; private set; }

        public static Vector3 Update(MyShipController controller,Vector3 move,float trigger,float throttle)
        {
            Braking=move.Y>.5f;
            if(controller.ControlWheels && controller.EnableShipControl && controller.GridWheels!=null && controller.GridWheels.WheelCount>0 &&
                (controller.IsMainCockpit || !controller.CubeGrid.HasMainCockpit())) move=Mix(move,false);
            // Include throttle before native movement replication so guests and hosts send the same input.
            float gas=Math.Max(Forward(throttle),controller.Toolbar?.SelectedItem==null ? Forward(trigger):0);
            if(gas>0) move.Z=-Math.Max(-move.Z,gas);
            return move;
        }
        public static void Stop() => Braking=false;
        private static float Forward(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0:MathHelper.Clamp(value,0,1);

        // Native wheels read X as steering and the sign of Z as throttle.
        internal static Vector3 Mix(Vector3 native,bool gas) =>
            new Vector3(native.X,native.Y,gas ? -1:Math.Abs(native.Z)>=StickThrottle ? native.Z:0);
    }
}
