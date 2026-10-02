using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class FlightAxes
    {
        internal static bool SecondaryGrip(bool flying,bool ship,bool stickOwned,bool nearStick,Vector2 rotation) =>
            !flying || ship && !stickOwned && !nearStick && rotation.LengthSquared()<=.04f;

        public static bool ControllerInputAllowed(bool ship,bool thirdPerson,bool physicalOnly) => !ship || thirdPerson || !physicalOnly;

        public static Vector3 Translation(Vector2 lateralVertical, Vector2 lateralForward, float up, float down, float forward, float back) =>
            new Vector3(lateralVertical.X + lateralForward.X, lateralVertical.Y + up - down, -lateralForward.Y - forward + back);

        public static void Rotation(Vector2 stick, bool rollModifier, bool ship, float speed, float rollSensitivity, out Vector2 rotation, out float roll, bool invertPitch=false)
        {
            rotation = new Vector2((ship ? -stick.Y : stick.Y) * speed, rollModifier ? 0 : stick.X * speed);
            if(invertPitch) rotation.X=-rotation.X;
            roll = rollModifier ? Roll(stick.X, ship, speed, rollSensitivity) : 0;
        }

        public static float Roll(float input, bool ship, float speed, float sensitivity)
        {
            float roll=input*speed;
            // Ships multiply roll by 0.2, then cap gyro torque at 1. Scale after that saturation.
            if (ship) roll=MathHelper.Clamp(roll,-5,5);
            return roll*sensitivity;
        }
    }
}
