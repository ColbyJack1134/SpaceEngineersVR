using System;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class TurnInput
    {
        private bool held;
        private DateTime next;

        internal float Read(float horizontal,bool snap,float smoothSpeed,float angle,float nativeSpeed,DateTime now)
        {
            if(!snap) { held=false; return horizontal*smoothSpeed; }
            if(Math.Abs(horizontal)<.35f) { held=false; return 0; }
            if(Math.Abs(horizontal)<.7f || nativeSpeed<=0 || held && now<next) return 0;
            held=true;
            next=now.AddMilliseconds(300);
            // Native character movement applies yaw as indicator * RotationSpeed * .02 radians.
            return Math.Sign(horizontal)*MathHelper.ToRadians(angle)/(nativeSpeed*.02f);
        }

        internal void Reset() { held=false; next=DateTime.MinValue; }
    }
}
