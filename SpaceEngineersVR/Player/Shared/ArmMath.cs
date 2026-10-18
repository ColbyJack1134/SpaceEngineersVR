using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class ArmMath
    {
        public static Matrix AimBone(Matrix bone,Vector3 from,Vector3 to)
        {
            if(from.LengthSquared()<1e-8f || to.LengthSquared()<1e-8f) return bone;
            from.Normalize(); to.Normalize();
            float dot=MathHelper.Clamp(Vector3.Dot(from,to),-1,1);
            Matrix turn;
            if(dot>0.999999f) turn=Matrix.Identity;
            else if(dot< -0.999999f)
            {
                Vector3 axis=Vector3.Cross(from,Math.Abs(from.Y)<0.9f ? Vector3.Up : Vector3.Right);
                axis.Normalize(); turn=Matrix.CreateFromAxisAngle(axis,MathHelper.Pi);
            }
            else
            {
                Vector3 axis=Vector3.Cross(from,to); axis.Normalize();
                turn=Matrix.CreateFromAxisAngle(axis,(float)Math.Acos(dot));
            }
            Matrix result=bone.GetOrientation()*turn;
            result.Translation=bone.Translation;
            return result;
        }
        public static Matrix PalmCorrection(Matrix palmRig,Matrix forearmRig,float side)
        {
            Vector3 forward=palmRig.Translation-forearmRig.Translation;
            forward.Normalize();
            Vector3 up=Math.Abs(Vector3.Dot(forward,Vector3.Up))<0.95f ? Vector3.Up : Vector3.Backward;
            Matrix grip=Matrix.CreateWorld(Vector3.Zero,forward,up);
            // Correct the suit palm axes in grip space: left +90 degrees, right -90.
            // Inward-pointing thumbs then point up with an upright controller.
            return palmRig.GetOrientation()*Matrix.Transpose(grip.GetOrientation())*Matrix.CreateRotationZ(-side*MathHelper.PiOver2)*HandInGrip;
        }
        // Palm frame in the controller grip frame: forward runs from forearm to palm, up through the thumb.
        public static readonly Matrix HandInGrip=Matrix.CreateRotationX(MathHelper.ToRadians(-39f));
    }
}
