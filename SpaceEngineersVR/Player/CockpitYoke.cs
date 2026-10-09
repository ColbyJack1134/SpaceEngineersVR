using System;
using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class CockpitYoke
    {
        private bool leftWas,rightWas,blocked;
        private float displacement;
        private Vector3 leftStart,rightStart,leftPrevious,rightPrevious;
        private readonly CockpitStickMath.Filter filter=new CockpitStickMath.Filter();
        internal float Command {get; private set;}
        internal float Visual {get; private set;}
        internal bool Active {get; private set;}
        internal void Reset()
        {
            leftWas=rightWas=blocked=Active=false; displacement=Command=Visual=0;
            filter.Update(false,Vector3.Zero,0,0);
        }
        internal float Update(bool enabled,bool left,Vector3 l,bool right,Vector3 r,Vector3 axis,FlightTuning tuning,float seconds)
        {
            left=enabled && left; right=enabled && right;
            if(!left && !right) blocked=false;
            bool invalid=left && (!l.IsValid() || leftWas && Vector3.DistanceSquared(l,leftPrevious)>.04f) ||
                right && (!r.IsValid() || rightWas && Vector3.DistanceSquared(r,rightPrevious)>.04f);
            if(invalid) blocked=true;
            Active=!blocked && (left || right);
            if(!Active)
            {
                leftWas=left; rightWas=right; displacement=Command=0;
                filter.Update(false,Vector3.Zero,0,0);
                Visual=CockpitStickMath.ReturnVisual(new Vector3(Visual,0,0),seconds).X;
                return 0;
            }
            // Preserve measured travel across handoffs, independent of filtering and visual lag.
            if(left!=leftWas || right!=rightWas)
            {
                if(left) leftStart=l-axis*displacement;
                if(right) rightStart=r-axis*displacement;
            }
            float distance=(left ? Vector3.Dot(l-leftStart,axis):0)+(right ? Vector3.Dot(r-rightStart,axis):0);
            distance/=left && right ? 2:1;
            displacement=distance;
            float target=CockpitStickMath.Axis(distance/tuning.WheelPitchTravel,tuning.WheelPitchDeadzone/tuning.WheelPitchTravel);
            Command=-filter.Update(true,new Vector3(target,0,0),seconds,tuning.Smoothing).X;
            float visualTarget=-CockpitStickMath.Response(Command*tuning.PitchSensitivity,tuning.RotationCurve);
            Visual=CockpitStickMath.SmoothVisual(new Vector3(Visual,0,0),new Vector3(visualTarget,0,0),seconds).X;
            leftPrevious=l; rightPrevious=r; leftWas=left; rightWas=right;
            return Command;
        }
    }
}
