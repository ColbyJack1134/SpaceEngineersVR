using System;
using VRageMath;

namespace SpaceEngineersVR.Player.Control
{
    internal sealed class ControlDrag
    {
        private Vector3 start,radial,axis;
        private float initial,travel;
        private bool linear,decided;
        public float Value { get; private set; }
        public bool State { get; private set; }
        public void Begin(Vector3 hand,Vector3 contact,Vector3 pivot,Vector3 hinge,float position,float range)
        {
            linear=false;
            start=hand; initial=Value=position; travel=range; State=position>=.5f;
            decided=position<=.35f || position>=.65f;
            axis=Vector3.Normalize(hinge);
            radial=contact-pivot; radial-=axis*Vector3.Dot(radial,axis);
            // Tiny cosmetic levers need a little more hand travel than their mesh radius.
            radial=Vector3.Normalize(radial)*Math.Max(.035f,radial.Length());
        }
        public void BeginLinear(Vector3 hand,Vector3 direction,float position,float range)
        {
            linear=true; start=hand; axis=Vector3.Normalize(direction);
            initial=Value=position; travel=range; State=position>=.5f;
            decided=position<=.35f || position>=.65f;
        }
        public int Move(Vector3 hand,bool commit=true)
        {
            if(!hand.IsValid() || !axis.IsValid() || Math.Abs(travel)<.001f) return -1;
            float movement;
            if(linear) movement=Vector3.Dot(hand-start,axis);
            else
            {
                if(!radial.IsValid()) return -1;
                Vector3 next=radial+hand-start;
                next-=axis*Vector3.Dot(next,axis);
                if(next.LengthSquared()<.000001f) return -1;
                movement=(float)Math.Atan2(Vector3.Dot(Vector3.Cross(radial,next),axis),Vector3.Dot(radial,next));
            }
            Value=MathHelper.Clamp(initial+movement/travel,0,1);
            if(!commit) return -1;
            if(!decided)
            {
                if(Value>.35f && Value<.65f) return -1;
                decided=true; State=Value>=.65f;
                return State ? 1 : 0;
            }
            bool nextState=State ? Value>.35f : Value>=.65f;
            if(nextState==State) return -1;
            State=nextState;
            return State ? 1 : 0;
        }
    }
}
