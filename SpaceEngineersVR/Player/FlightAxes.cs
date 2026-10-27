using SpaceEngineersVR.Multiplayer;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class FlightAxes
    {
        internal static bool SecondaryGrip(bool flying,bool ship,bool stickOwned,bool nearStick,Vector2 rotation) =>
            !flying || ship && !stickOwned && !nearStick && rotation.LengthSquared()<=.04f;

        public static bool ControllerInputAllowed(bool ship,bool thirdPerson,bool physicalOnly) => !ship || thirdPerson || !physicalOnly;

        // In physical-only mode the controller still supplies the axes of a stick the seat does not have.
        public static void MissingStickAxes(bool controllerFlight,bool needsTranslation,bool needsRotation,ref Vector3 move,ref Vector2 rotate,ref float roll)
        {
            if(controllerFlight) return;
            if(!needsTranslation) move=Vector3.Zero;
            if(!needsRotation) { rotate=Vector2.Zero; roll=0; }
        }
        // Physical-only seats with a modeled stick need it in hand before the trigger fires or drives.
        public static bool StickTriggerAllowed(bool ship,bool physicalOnly,bool sticksAvailable,bool stickHeld) =>
            !ship || !physicalOnly || !sticksAvailable || stickHeld;

        // In seats the left trigger is up thrust, which wheels read as the brake.
        public static void ApplyLeftTrigger(LeftTrigger action,ref float up,ref float back)
        {
            if(action==LeftTrigger.Reverse) back=System.Math.Max(back,up);
            if(action!=LeftTrigger.Brake) up=0;
        }

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
