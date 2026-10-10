using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class RadialNavigation
    {
        internal int Owner { get; private set; }=-1;
        internal void Reset() => Owner=-1;
        internal Vector2 Update(Vector2 left,Vector2 right,int preferred)
        {
            var current=Owner==0 ? left:right;
            if(Owner>=0)
            {
                if(current.LengthSquared()<.0625f || !Valid(current)) { Reset(); return Vector2.Zero; }
                return current;
            }
            bool l=Valid(left) && left.LengthSquared()>=.25f;
            bool r=Valid(right) && right.LengthSquared()>=.25f;
            if(!l && !r) return Vector2.Zero;
            Owner=l && r ? preferred:l ? 0:1;
            return Owner==0 ? left:right;
        }
        private static bool Valid(Vector2 value) => !float.IsNaN(value.X) && !float.IsNaN(value.Y) &&
            !float.IsInfinity(value.X) && !float.IsInfinity(value.Y);
    }
}
