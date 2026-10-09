using System;
using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal sealed class CockpitBarTilt
    {
        private bool blocked;
        private float neutralHeight,neutralBank;
        private Vector3 previousLeft,previousRight;
        private Matrix neutralLeft,neutralRight;
        private Matrix commonFrame=Matrix.Identity,shaftOrientation=Matrix.Identity;
        private Vector3 leftShaft,rightShaft;
        private readonly CockpitStickMath.Filter filter=new CockpitStickMath.Filter();
        internal bool Active { get; private set; }
        internal Vector2 Raw { get; private set; }
        internal Vector2 Visual { get; private set; }
        internal Vector2 Command { get; private set; }

        internal void Reset()
        {
            Active=blocked=false; Raw=Visual=Command=Vector2.Zero; commonFrame=shaftOrientation=Matrix.Identity;
            filter.Update(false,Vector3.Zero,0,0);
        }
        internal Vector2 Update(bool enabled,Vector3 left,Vector3 right,FlightTuning tuning,float seconds)
        {
            Vector3 pair=right-left;
            float span=new Vector2(pair.X,pair.Z).Length();
            bool valid=left.IsValid() && right.IsValid() && span>=.12f;
            if(enabled && (!valid || Active &&
                (Vector3.DistanceSquared(left,previousLeft)>.04f || Vector3.DistanceSquared(right,previousRight)>.04f))) blocked=true;
            if(!enabled || blocked)
            {
                Active=false; Raw=Command=Vector2.Zero;
                filter.Update(false,Vector3.Zero,0,0);
                if(!enabled) blocked=false;
                var returned=CockpitStickMath.ReturnVisual(new Vector3(Visual.X/tuning.BarPitchTravel,Visual.Y/tuning.BarRollTravel,0),seconds);
                Visual=new Vector2(returned.X*tuning.BarPitchTravel,returned.Y*tuning.BarRollTravel);
                return Command;
            }
            float height=(left.Y+right.Y)*.5f,bank=-(float)Math.Atan2(pair.Y,span);
            if(!Active) {neutralHeight=height; neutralBank=bank; Active=true;}
            previousLeft=left; previousRight=right;
            Raw=new Vector2(height-neutralHeight,bank-neutralBank);
            var command=new Vector3(-CockpitStickMath.Axis(Raw.X/tuning.BarPitchTravel,tuning.BarPitchDeadzone/tuning.BarPitchTravel),
                CockpitStickMath.Axis(Raw.Y/tuning.BarRollTravel,tuning.BarRollDeadzone/tuning.BarRollTravel),0);
            var result=filter.Update(true,command,seconds,tuning.Smoothing);
            Command=new Vector2(result.X,result.Y);
            var target=new Vector3(MathHelper.Clamp(Raw.X,-tuning.BarPitchTravel,tuning.BarPitchTravel),
                MathHelper.Clamp(Raw.Y,-tuning.BarRollTravel,tuning.BarRollTravel),0);
            var visual=CockpitStickMath.SmoothVisual(new Vector3(Visual,0),target,seconds);
            Visual=new Vector2(visual.X,visual.Y);
            return Command;
        }
        internal Vector2 Update(bool enabled,Matrix left,Matrix right,CockpitRig.Steering wheel,FlightTuning tuning,float seconds,Matrix? shaftFrame=null)
        {
            bool was=Active;
            var result=Update(enabled,left.Translation,right.Translation,tuning,seconds);
            if(Active && !was)
            {
                neutralLeft=left.GetOrientation(); neutralRight=right.GetOrientation(); commonFrame=Matrix.Identity;
                shaftOrientation=(shaftFrame ?? Matrix.Identity).GetOrientation();
                leftShaft=Vector3.TransformNormal(wheel.LeftShaft,shaftOrientation);
                rightShaft=Vector3.TransformNormal(wheel.RightShaft,shaftOrientation);
            }
            return result;
        }
        internal Matrix ThrottleFrame(Matrix left,Matrix right,CockpitRig.Steering wheel)
        {
            if(!Active) return Matrix.Identity;
            Vector3 l=Vector3.TransformNormal(leftShaft,Matrix.Transpose(neutralLeft)*left.GetOrientation());
            Vector3 r=Vector3.TransformNormal(rightShaft,Matrix.Transpose(neutralRight)*right.GetOrientation());
            if(!TryFrame(l,r,out Matrix current) || !TryFrame(leftShaft,rightShaft,out Matrix rest)) return shaftOrientation*commonFrame;
            // Shaft directions ignore each wrist's axial throttle twist while retaining common bar rotation.
            commonFrame=Matrix.Transpose(rest)*current;
            return shaftOrientation*commonFrame;
        }
        private static bool TryFrame(Vector3 left,Vector3 right,out Matrix frame)
        {
            frame=Matrix.Identity;
            Vector3 up=Vector3.Cross(left,right);
            if(!left.IsValid() || !right.IsValid() || up.LengthSquared()<.04f) return false;
            frame.Right=Vector3.Normalize(left); frame.Up=Vector3.Normalize(up);
            frame.Backward=Vector3.Cross(frame.Right,frame.Up);
            return true;
        }
    }
}
