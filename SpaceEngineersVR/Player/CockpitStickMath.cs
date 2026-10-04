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
        public static float Response(float value,float exponent=2)
        {
            if(float.IsNaN(value) || float.IsInfinity(value)) return 0;
            return Math.Sign(value)*(float)Math.Pow(MathHelper.Clamp(Math.Abs(value),0,1),exponent);
        }
        public static Vector3 Response(Vector3 value,float exponent=2) => new Vector3(Response(value.X,exponent),Response(value.Y,exponent),Response(value.Z,exponent));
        internal sealed class Filter
        {
            private Vector3 value;
            internal Vector3 Update(bool held,Vector3 input,float seconds,float smoothing)
            {
                if(!held || !input.IsValid()) return value=Vector3.Zero;
                float blend=smoothing<=0 ? 1 : 1-(float)Math.Exp(-Math.Max(0,seconds)/smoothing);
                value=Vector3.Lerp(value,input,blend);
                // Neutral and end stops must remain exact despite smoothing.
                value=new Vector3(Endpoint(input.X,value.X),Endpoint(input.Y,value.Y),Endpoint(input.Z,value.Z));
                return value;
            }
            private static float Endpoint(float input,float smoothed) => input==0 || Math.Abs(input)>=1 ? input:smoothed;
        }
        public static void ApplyFlight(bool ownsTranslation,bool ownsRotation,Vector3 translation,Vector3 rotation,float thumbVertical,float thumbYaw,
            float speed,float rollSensitivity,ref Vector3 move,ref Vector2 rotate,ref float roll,float exponent=2)
        {
            if (ownsTranslation) move=Vector3.Clamp(Response(translation,exponent)+thumbVertical*Vector3.Up,-Vector3.One,Vector3.One);
            if (!ownsRotation) return;
            Vector3 command=Response(rotation,exponent);
            command.Y=MathHelper.Clamp(command.Y+thumbYaw,-1,1);
            rotate=new Vector2(command.X,command.Y)*speed;
            roll=FlightAxes.Roll(command.Z,true,speed,rollSensitivity);
        }
        public static Vector3 Translation(Matrix neutral,Matrix current,float deadzone,bool twist,float sensitivity=1)
        {
            Vector3 tilt=Rotation(neutral,current,deadzone,twist,sensitivity);
            // Forward tilt drives -Z; clockwise twist (viewed from above) drives +Y.
            return new Vector3(tilt.Z,tilt.Y,-tilt.X);
        }
        public static Vector3 Rotation(Matrix neutral,Matrix current,float deadzone,bool twist,float sensitivity=1)
        {
            if (!neutral.IsValid() || !current.IsValid()) return Vector3.Zero;
            Quaternion q=Quaternion.CreateFromRotationMatrix(Matrix.Transpose(neutral.GetOrientation())*current.GetOrientation());
            if (q.W<0) q=new Quaternion(-q.X,-q.Y,-q.Z,-q.W);
            Vector3 xyz=new Vector3(q.X,q.Y,q.Z);
            float length=xyz.Length();
            Vector3 angle=length<1e-6f ? Vector3.Zero : xyz*((float)(2*Math.Atan2(length,q.W))/length);
            Vector2 tilt=Tilt(new Vector2(-angle.X,-angle.Z)*sensitivity/FighterProfile.Tilt,deadzone);
            return new Vector3(tilt.X,twist ? Axis(-angle.Y*sensitivity/FighterProfile.Twist,deadzone) : 0,tilt.Y);
        }
        internal static Vector2 Tilt(Vector2 value,float deadzone)
        {
            float length=value.Length();
            if (!(length>deadzone) || float.IsInfinity(length)) return Vector2.Zero;
            return Vector2.Clamp(value*((length-deadzone)/(1-deadzone)/length),-Vector2.One,Vector2.One);
        }
        public static Matrix Around(Vector3 pivot,Matrix rotation) => Matrix.CreateTranslation(-pivot)*rotation*Matrix.CreateTranslation(pivot);
        internal static Matrix Visual(Vector3 pivot,Vector3 axes)
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
            return RaiseGrip(GripPalm(left,left ? FighterProfile.LeftContact : FighterProfile.RightContact,shaft),shaft,.035f,.015f);
        }
        internal static Matrix GripPalm(bool left,Vector3 contact,Vector3 shaft)
        {
            float side=left ? -1 : 1;
            Vector3 length=Vector3.Normalize(Vector3.Backward-shaft*Vector3.Dot(Vector3.Backward,shaft));
            Matrix palm=Matrix.Identity;
            palm.Right=length; palm.Backward=shaft*side; palm.Up=Vector3.Cross(palm.Backward,palm.Right);
            // Grip cavity measured with the astronaut's curled finger bones.
            palm.Translation=contact-
                Vector3.TransformNormal(new Vector3(-.105f,-.035f,0),palm);
            return palm;
        }
        internal static Matrix RaiseGrip(Matrix palm,Vector3 shaft,float lift,float inset)
        {
            palm.Translation+=shaft*lift+palm.Up*inset;
            return palm;
        }
    }
}
