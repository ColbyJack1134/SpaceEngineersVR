using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class CockpitSteering
    {
        private bool leftWas,rightWas;
        private Vector3 leftStart,rightStart,leftRadial,rightRadial;
        private float angle,startAngle;
        internal float Position { get; private set; }

        internal void Reset()
        {
            leftWas=rightWas=false; angle=startAngle=Position=0;
        }

        internal float Update(CockpitRig.Steering wheel,bool left,Vector3 leftPoint,bool right,Vector3 rightPoint,float seconds)
        {
            left=left && leftPoint.IsValid(); right=right && rightPoint.IsValid();
            if(left!=leftWas || right!=rightWas)
            {
                // Rebase both hands when ownership changes, preserving the current shared angle.
                startAngle=angle; leftStart=leftPoint; rightStart=rightPoint;
                leftRadial=wheel.Contact(true,Position)-wheel.Pivot;
                rightRadial=wheel.Contact(false,Position)-wheel.Pivot;
            }
            if(left || right)
            {
                float change=(left ? Delta(leftRadial,leftPoint-leftStart,wheel.Axis):0)+
                    (right ? Delta(rightRadial,rightPoint-rightStart,wheel.Axis):0);
                angle=MathHelper.Clamp(startAngle+change/(left && right ? 2:1),-wheel.Range,wheel.Range);
            }
            else angle=CockpitStickMath.ReturnVisual(new Vector3(angle/wheel.Range,0,0),seconds).X*wheel.Range;
            Position=angle/wheel.Range; leftWas=left; rightWas=right;
            return left || right ? Position:0;
        }

        private static float Delta(Vector3 radial,Vector3 motion,Vector3 axis)
        {
            radial-=axis*Vector3.Dot(radial,axis);
            Vector3 current=radial+motion; current-=axis*Vector3.Dot(current,axis);
            if(radial.LengthSquared()<.0001f || current.LengthSquared()<.0001f) return 0;
            return (float)Math.Atan2(Vector3.Dot(axis,Vector3.Cross(radial,current)),Vector3.Dot(radial,current));
        }
    }
}
