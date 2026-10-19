using SpaceEngineersVR.Util;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public class TrackedDevice
    {
        public Matrix deviceToPlayer => VrMath.Affine(pose.deviceToAbsolute.matrix * Player.PlayerToAbsolute.inverted);

        public struct Pose
        {
            public bool isConnected;
            public bool isTracked;

            public MatrixAndInvert deviceToAbsolute;

            public Vector3 velocity;
            public Vector3 angularVelocity;

            public static Pose Create()
            {
                return new Pose()
                {
                    deviceToAbsolute = MatrixAndInvert.Identity,
                };
            }
        }
        public Pose renderPose = Pose.Create();
        public Pose pose = Pose.Create();

        public uint deviceId = OpenVR.k_unTrackedDeviceIndexInvalid;

        private readonly ulong hapticsActionHandle;
        private bool hapticsReported;
        private readonly ulong actionHandle;

        public TrackedDevice(string actionName = null, string hapticsName = "/actions/feedback/out/GenericHaptic")
        {
            if (!string.IsNullOrEmpty(actionName))
                OpenVR.Input.GetActionHandle(actionName, ref actionHandle);
            OpenVR.Input.GetActionHandle(hapticsName, ref hapticsActionHandle);
        }

        private long pulseStart,pulseLast;
        private bool pulseReported;
        public void Vibrate(float delay, float duration, float frequency, float amplitude,[System.Runtime.CompilerServices.CallerMemberName] string caller="",[System.Runtime.CompilerServices.CallerFilePath] string file="")
        {
            if (hapticsActionHandle == 0)
                return;
            Watch(caller,file);
            var error=OpenVR.Input.TriggerHapticVibrationAction(hapticsActionHandle, delay, duration, frequency, amplitude, OpenVR.k_ulInvalidInputValueHandle);
            if(error!=EVRInputError.None && !hapticsReported)
            { hapticsReported=true; Plugin.Logger.Warning("Controller haptic output failed: "+error); }
        }


        // Every pulse is short, so a buzz that lasts is a caller pulsing every frame; name it once per episode with developer tools on.
        private void Watch(string caller,string file)
        {
            long now=System.Diagnostics.Stopwatch.GetTimestamp(),frequency=System.Diagnostics.Stopwatch.Frequency;
            if(now-pulseLast>frequency*3/10) { pulseStart=now; pulseReported=false; }
            pulseLast=now;
            if(pulseReported || now-pulseStart<frequency || Plugin.Common.Config?.DeveloperTools!=true) return;
            pulseReported=true;
            Plugin.Logger.Info("Haptics: "+(ReferenceEquals(this,Player.HandL) ? "left":ReferenceEquals(this,Player.HandR) ? "right":"device")+" hand pulsed continuously for 1 s from "+System.IO.Path.GetFileNameWithoutExtension(file)+"."+caller);
        }
        public virtual void MainUpdate()
        {
        }

        public void SetMainPoseData(TrackedDevicePose_t value)
        {
            SetPoseData(ref pose, value, out bool wasConnected, out bool wasDisconnected, out bool startedTracking, out bool lostTracking);

            if (wasConnected)
                OnConnected();
            if (wasDisconnected)
                OnDisconnected();

            if (startedTracking)
                OnStartTracking();
            if (lostTracking)
                OnLostTracking();
        }

        public void SetRenderPoseData(TrackedDevicePose_t value)
        {
            SetPoseData(ref renderPose, value, out _, out _, out _, out _);
        }

        private static void SetPoseData(ref Pose pose, TrackedDevicePose_t value, out bool wasConnected, out bool wasDisconnected, out bool startedTracking, out bool lostTracking)
        {
            wasConnected = false;
            wasDisconnected = false;
            startedTracking = false;
            lostTracking = false;

            if (pose.isConnected != value.bDeviceIsConnected)
            {
                pose.isConnected = value.bDeviceIsConnected;

                if (value.bDeviceIsConnected)
                    wasConnected = true;
                else
                    wasDisconnected = true;
            }

            var matrix = value.mDeviceToAbsoluteTracking.ToMatrix();
            bool valid = value.bPoseIsValid && Plausible(matrix);
            if (pose.isTracked != valid)
            {
                pose.isTracked = valid;

                if (valid)
                    startedTracking = true;
                else
                    lostTracking = true;
            }

            if (pose.isTracked)
            {
                pose.deviceToAbsolute = new MatrixAndInvert(matrix);
                pose.velocity = value.vVelocity.ToVector();
                pose.angularVelocity = value.vAngularVelocity.ToVector();
            }
        }


        // A dropped or waking controller can report a valid flag with an unusable pose.
        internal static bool Plausible(Matrix matrix) => matrix.IsValid() && matrix.Translation.LengthSquared() < 1e6f &&
            System.Math.Abs(matrix.Forward.LengthSquared() - 1) < .1f && System.Math.Abs(matrix.Up.LengthSquared() - 1) < .1f;

        protected virtual void OnConnected()
        {
        }
        protected virtual void OnDisconnected()
        {
        }

        protected virtual void OnStartTracking()
        {
        }
        protected virtual void OnLostTracking()
        {
        }
    }
}
