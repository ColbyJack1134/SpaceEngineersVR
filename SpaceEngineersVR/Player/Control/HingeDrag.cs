using System;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class HingeDrag
    {
        private Vector3 start,radial,axis;
        private float initial,travel;
        public float Value { get; private set; }
        public bool State { get; private set; }
        public void Begin(Vector3 hand,Vector3 contact,Vector3 pivot,Vector3 hinge,float position,float range)
        {
            start=hand; initial=Value=position; travel=range; State=position>=.5f;
            axis=Vector3.Normalize(hinge);
            radial=contact-pivot; radial-=axis*Vector3.Dot(radial,axis);
            // Tiny cosmetic levers need a little more hand travel than their mesh radius.
            radial=Vector3.Normalize(radial)*Math.Max(.035f,radial.Length());
        }
        public int Move(Vector3 hand)
        {
            if(!hand.IsValid() || !radial.IsValid() || !axis.IsValid() || Math.Abs(travel)<.001f) return -1;
            Vector3 next=radial+hand-start;
            next-=axis*Vector3.Dot(next,axis);
            if(next.LengthSquared()<.000001f) return -1;
            float angle=(float)Math.Atan2(Vector3.Dot(Vector3.Cross(radial,next),axis),Vector3.Dot(radial,next));
            Value=MathHelper.Clamp(initial+angle/travel,0,1);
            bool nextState=State ? Value>.35f : Value>=.65f;
            if(nextState==State) return -1;
            State=nextState;
            return State ? 1 : 0;
        }
    }
}
