using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class CockpitSteering
    {
        private bool leftWas,rightWas;
        private readonly Hand leftHand=new Hand(),rightHand=new Hand();
        private float angle;
        internal float Position { get; private set; }
        internal float VisualPosition { get; private set; }

        internal void Reset()
        {
            leftWas=rightWas=false; angle=Position=VisualPosition=0;
            leftHand.Clear(); rightHand.Clear();
        }

        internal float Update(CockpitRig.Steering wheel,bool left,Vector3 leftPoint,bool right,Vector3 rightPoint,float seconds)
        {
            left=left && leftPoint.IsValid(); right=right && rightPoint.IsValid();
            if(left!=leftWas || right!=rightWas)
            {
                leftHand.Begin(wheel,true,Position,leftPoint);
                rightHand.Begin(wheel,false,Position,rightPoint);
            }
            if(left || right)
            {
                float change=0; int count=0;
                if(left && leftHand.Move(wheel,true,Position,leftPoint,out float l)) {change+=l; count++;}
                if(right && rightHand.Move(wheel,false,Position,rightPoint,out float r)) {change+=r; count++;}
                // Consume travel at the stop so reversing responds without unwinding excess motion.
                if(count>0) angle=MathHelper.Clamp(angle+change/count,-wheel.Range,wheel.Range);
            }
            else angle=CockpitStickMath.ReturnVisual(new Vector3(angle/wheel.Range,0,0),seconds).X*wheel.Range;
            Position=angle/wheel.Range; leftWas=left; rightWas=right;
            VisualPosition=left || right ? CockpitStickMath.SmoothVisual(new Vector3(VisualPosition,0,0),new Vector3(Position,0,0),seconds).X:
                CockpitStickMath.ReturnVisual(new Vector3(VisualPosition,0,0),seconds).X;
            return left || right ? Position:0;
        }

        private sealed class Hand
        {
            private Vector3 start,radial,previous;
            private bool sampled;
            internal void Clear() => sampled=false;
            internal void Begin(CockpitRig.Steering wheel,bool left,float position,Vector3 point)
            {
                start=point; radial=wheel.Contact(left,position)-wheel.Pivot;
                radial-=wheel.Axis*Vector3.Dot(radial,wheel.Axis);
                previous=radial; sampled=point.IsValid();
            }
            internal bool Move(CockpitRig.Steering wheel,bool left,float position,Vector3 point,out float change)
            {
                change=0;
                if(!sampled) {Begin(wheel,left,position,point); return false;}
                Vector3 current=radial+point-start; current-=wheel.Axis*Vector3.Dot(current,wheel.Axis);
                if(current.LengthSquared()<radial.LengthSquared()*.04f) {Clear(); return false;}
                change=(float)Math.Atan2(Vector3.Dot(wheel.Axis,Vector3.Cross(previous,current)),Vector3.Dot(previous,current));
                previous=current;
                if(Math.Abs(change)>MathHelper.PiOver2) {Clear(); change=0; return false;}
                return true;
            }
        }
    }
}
