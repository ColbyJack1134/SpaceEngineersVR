using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitStickMath
    {
        public static float Axis(float value,float deadzone)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return Math.Sign(value)*MathHelper.Clamp((Math.Abs(value)-deadzone)/(1-deadzone),0,1);
        }
        public static void ApplyFlight(bool ownsTranslation,bool ownsRotation,Vector3 translation,Vector3 rotation,
            float speed,float sensitivity,float rollSensitivity,ref Vector3 move,ref Vector2 rotate,ref float roll)
        {
            if (ownsTranslation) move=Vector3.Clamp(translation*sensitivity,-Vector3.One,Vector3.One);
            if (!ownsRotation) return;
            rotate=new Vector2(rotation.X,rotation.Y)*speed*sensitivity;
            roll=FlightAxes.Roll(rotation.Z,true,speed*sensitivity,rollSensitivity);
        }
        public static Vector3 Translation(Matrix neutral,Matrix current,float deadzone)
        {
            Vector3 tilt=Rotation(neutral,current,deadzone);
            // Forward tilt drives -Z; clockwise twist (viewed from above) drives +Y.
            return new Vector3(tilt.Z,tilt.Y,-tilt.X);
        }
        public static Vector3 Rotation(Matrix neutral,Matrix current,float deadzone)
        {
            if (!neutral.IsValid() || !current.IsValid()) return Vector3.Zero;
            Quaternion q=Quaternion.CreateFromRotationMatrix(Matrix.Transpose(neutral.GetOrientation())*current.GetOrientation());
            if (q.W<0) q=new Quaternion(-q.X,-q.Y,-q.Z,-q.W);
            Vector3 xyz=new Vector3(q.X,q.Y,q.Z);
            float length=xyz.Length();
            Vector3 angle=length<1e-6f ? Vector3.Zero : xyz*((float)(2*Math.Atan2(length,q.W))/length);
            return new Vector3(Axis(-angle.X/FighterProfile.Tilt,deadzone),Axis(-angle.Y/FighterProfile.Twist,deadzone),Axis(-angle.Z/FighterProfile.Tilt,deadzone));
        }
        public static Matrix Around(Vector3 pivot,Matrix rotation) => Matrix.CreateTranslation(-pivot)*rotation*Matrix.CreateTranslation(pivot);
        private static Matrix Visual(Vector3 pivot,Vector3 axes)
        {
            Matrix turn=Matrix.CreateFromYawPitchRoll(-axes.Y*FighterProfile.Twist,-axes.X*FighterProfile.Tilt,-axes.Z*FighterProfile.Tilt);
            return Around(pivot,turn);
        }
        public static Matrix RightVisual(Vector3 axes) => Visual(FighterProfile.RightPivot,axes);
        public static Matrix LeftVisual(Vector3 translation) => Visual(FighterProfile.LeftPivot,new Vector3(-translation.Z,translation.Y,translation.X));
        internal static Matrix GripPalm(bool left)
        {
            float side=left ? -1 : 1;
            Vector3 shaft=Vector3.Normalize(new Vector3(-side*.30f,.9539f,-.015f));
            Vector3 length=Vector3.Normalize(Vector3.Backward-shaft*Vector3.Dot(Vector3.Backward,shaft));
            Matrix palm=Matrix.Identity;
            palm.Right=length; palm.Backward=shaft*side; palm.Up=Vector3.Cross(palm.Backward,palm.Right);
            // Grip cavity measured with the astronaut's curled finger bones.
            palm.Translation=(left ? FighterProfile.LeftContact : FighterProfile.RightContact)-
                Vector3.TransformNormal(new Vector3(-.105f,-.035f,0),palm);
            return palm;
        }
        internal static int Detents(Vector3 axes)
        {
            int result=0;
            for (int i=0;i<3;i++)
            {
                float a=i==0 ? axes.X : i==1 ? axes.Y : axes.Z;
                if (Math.Abs(a)>0.05f) result|=1<<i;
                if (Math.Abs(a)>0.97f) result|=1<<(i+3);
            }
            return result;
        }
    }
}
