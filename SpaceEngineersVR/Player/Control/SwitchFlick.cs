using System;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class SwitchFlick
    {
        private bool inside,spent;
        private float start;
        public void Reset() { inside=spent=false; }
        public int Update(Vector3 point)
        {
            bool near=point.IsValid() && Math.Abs(point.X)<.009f && Math.Abs(point.Y)<.025f && point.Z>-.014f && point.Z<.028f;
            if(!near) { Reset(); return -1; }
            if(!inside) { inside=true; start=point.Y; return -1; }
            if(spent || Math.Abs(point.Y-start)<.008f) return -1;
            spent=true;
            return point.Y>start ? 1 : 0;
        }
    }
}
