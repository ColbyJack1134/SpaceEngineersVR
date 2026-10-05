using System;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitStickMath
    {
        public const float TiltRange=MathHelper.Pi/6,TwistRange=MathHelper.Pi/6;
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
                float blend=smoothing<=0 ? 1 : MathHelper.Clamp(seconds/smoothing,0,1);
                value=Vector3.Lerp(value,input,blend);
                // Neutral and end stops must remain exact despite smoothing.
                value=new Vector3(Endpoint(input.X,value.X),Endpoint(input.Y,value.Y),Endpoint(input.Z,value.Z));
                return value;
            }
            private static float Endpoint(float input,float smoothed) => input==0 || Math.Abs(input)>=1 ? input:smoothed;
        }
        public static void ApplyFlight(bool ownsTranslation,bool ownsRotation,Vector3 translation,Vector3 rotation,float thumbVertical,float thumbYaw,
            float speed,float rollSensitivity,ref Vector3 move,ref Vector2 rotate,ref float roll,float exponent=2,float translationExponent=-1)
        {
            if (ownsTranslation) move=Vector3.Clamp(Response(translation,translationExponent<0 ? exponent:translationExponent)+thumbVertical*Vector3.Up,-Vector3.One,Vector3.One);
            if (!ownsRotation) return;
            Vector3 command=Response(rotation,exponent);
            command.Y=MathHelper.Clamp(command.Y+thumbYaw,-1,1);
            rotate=new Vector2(command.X,command.Y)*speed;
            roll=FlightAxes.Roll(command.Z,true,speed,rollSensitivity);
        }
        public static Vector3 Translation(Matrix neutral,Matrix current,float deadzone,bool twist,float sensitivity=1,Matrix? frame=null,float twistDeadzone=-1)
        {
            Vector3 tilt=Rotation(neutral,current,deadzone,twist,sensitivity,frame,twistDeadzone);
            // Forward tilt drives -Z; clockwise twist (viewed from above) drives +Y.
            return new Vector3(tilt.Z,tilt.Y,-tilt.X);
        }
        public static Vector3 Rotation(Matrix neutral,Matrix current,float deadzone,bool twist,float sensitivity=1,Matrix? frame=null,float twistDeadzone=-1)
        {
            if (!neutral.IsValid() || !current.IsValid()) return Vector3.Zero;
            Matrix turn=Matrix.Transpose(neutral.GetOrientation())*current.GetOrientation();
            // Express the hand delta in the authored shaft frame (row-vector convention).
            if(frame.HasValue) turn=frame.Value*turn*Matrix.Transpose(frame.Value);
            // Carry the grabbed shaft through the hand rotation; axial twist must not change its lean.
            Vector3 shaft=turn.Up,heading=turn.Backward;
            Vector2 lean=new Vector2(-(float)Math.Atan2(shaft.Z,shaft.Y),(float)Math.Atan2(shaft.X,shaft.Y));
            Vector2 tilt=Tilt(lean*sensitivity/TiltRange,deadzone);
            float yaw=-(float)Math.Atan2(heading.X,heading.Z);
            return new Vector3(tilt.X,twist ? Axis(yaw*sensitivity/TwistRange,twistDeadzone<0 ? deadzone:twistDeadzone) : 0,tilt.Y);
        }
        internal static Vector3 ReturnVisual(Vector3 value,float seconds)
        {
            float step=Math.Max(0,seconds)/.12f;
            return new Vector3(TowardZero(value.X,step),TowardZero(value.Y,step),TowardZero(value.Z,step));
        }
        private static float TowardZero(float v,float step) => Math.Sign(v)*Math.Max(0,Math.Abs(v)-step);
        internal static Vector2 Tilt(Vector2 value,float deadzone)
        {
            float length=value.Length();
            if (!(length>deadzone) || float.IsInfinity(length)) return Vector2.Zero;
            return Vector2.Clamp(value*((length-deadzone)/(1-deadzone)/length),-Vector2.One,Vector2.One);
        }
        internal static Matrix ShaftFrame(Vector3 shaft) => Matrix.CreateWorld(Vector3.Zero,
            -Vector3.Normalize(Vector3.Backward-shaft*Vector3.Dot(Vector3.Backward,shaft)),shaft);
        public static Matrix Around(Vector3 pivot,Matrix rotation) => Matrix.CreateTranslation(-pivot)*rotation*Matrix.CreateTranslation(pivot);
        internal static Matrix Visual(Vector3 pivot,Vector3 axes,Matrix? frame=null)
        {
            Matrix turn=Matrix.CreateFromYawPitchRoll(-axes.Y*TwistRange,-axes.X*TiltRange,-axes.Z*TiltRange);
            if(frame.HasValue) turn=Matrix.Transpose(frame.Value)*turn*frame.Value;
            return Around(pivot,turn);
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
