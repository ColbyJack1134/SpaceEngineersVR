using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class RadialMath
    {
        public static int Sector(Vector2 stick, int count)
        {
            if (count < 1 || stick.LengthSquared() < 0.25f || float.IsNaN(stick.X) || float.IsNaN(stick.Y) ||
                float.IsInfinity(stick.X) || float.IsInfinity(stick.Y)) return -1;
            double turn = Math.Atan2(stick.X, stick.Y) / (Math.PI * 2);
            return ((int)Math.Floor((turn + 1) * count + 0.5)) % count;
        }
    }
}
