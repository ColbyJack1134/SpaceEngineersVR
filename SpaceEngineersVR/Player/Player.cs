using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using System.Text;
using System.Threading;
using Valve.VR;
using VRage;
using VRage.Collections;
using VRage.Input;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public static class Player
    {
        private const int CalibrationTimeTicks = 60 * 5;


        public static readonly Headset Headset = new Headset();
        public static readonly Controller HandL = new Controller("/actions/common/in/LeftHand", "/actions/feedback/out/LeftHaptic");
        public static readonly Controller HandR = new Controller("/actions/common/in/RightHand", "/actions/feedback/out/RightHaptic");
        public static readonly MyConcurrentList<TrackedDevice> AllDevices = new MyConcurrentList<TrackedDevice>(3);

        public static BodyCalibration GetBodyCalibration()
        {
            using (PlayerCalibrationLock.AcquireSharedUsing())
            {
                return PlayerCalibration;
            }
        }

        public static bool IsCalibrating => CalibratingTicksLeft > 0;

        private static readonly FastResourceLock PlayerCalibrationLock = new FastResourceLock();
        private static BodyCalibration PlayerCalibration;

        private static int CalibratingTicksLeft = 0;
        private static BodyCalibration CalibrationInProgress;
        private static readonly StandingMeasurement standingMeasurement=new StandingMeasurement();


        public static MatrixAndInvert PlayerToAbsolute { get; private set; } = MatrixAndInvert.Identity;

        //PlayerToAbsolute that is synced for render thread
        public static MatrixAndInvert RenderPlayerToAbsolute = MatrixAndInvert.Identity;

        private static readonly FastResourceLock SyncPlayerToAbsoluteLock = new FastResourceLock();
        private static MatrixAndInvert SyncPlayerToAbsolute = MatrixAndInvert.Identity;


        private static uint NextDeviceId = 0;

        private static readonly TrackedDevicePose_t[] RenderPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private static readonly TrackedDevicePose_t[] RenderPosesFuture = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount]; //Poses one frame in the future

        private static readonly object SyncPosesLock = new object();
        private static TrackedDevicePose_t[] SyncPoses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        private static TrackedDevicePose_t[] Poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];

        static Player()
        {
            AllDevices.Add(Headset);
            AllDevices.Add(HandL);
            AllDevices.Add(HandR);

            using (PlayerCalibrationLock.AcquireExclusiveUsing())
            {
                PlayerCalibration.height = Common.Config.PlayerHeight;
                PlayerCalibration.armSpan = Common.Config.PlayerArmSpan;
            }
        }

        public static void RenderUpdate()
        {
            //Check for new devices
            for (; NextDeviceId < OpenVR.k_unMaxTrackedDeviceCount; NextDeviceId++)
            {
                //In OpenVR, once a device is connected once, its ID is unique, even if disconnected
                ETrackedDeviceClass deviceClass = OpenVR.System.GetTrackedDeviceClass(NextDeviceId);

                if (deviceClass == ETrackedDeviceClass.Invalid)
                {
                    break;
                }

                if (deviceClass == ETrackedDeviceClass.GenericTracker)
                {
                    TrackedDevice device = new TrackedDevice
                    {
                        deviceId = NextDeviceId
                    };
                    AllDevices.Add(device);
                }
            }

            long waitStart=FeatureTiming.Start();
            OpenVR.Compositor.WaitGetPoses(RenderPoses, RenderPosesFuture);
            FeatureTiming.End(FeatureTiming.Area.WaitPoses,waitStart);

            {
                bool lockTaken = false;
                try
                {
                    Monitor.TryEnter(SyncPosesLock, ref lockTaken);
                    if (lockTaken)
                    {
                        for (int i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; ++i)
                        {
                            SyncPoses[i] = RenderPoses[i];
                        }
                    }
                }
                finally
                {
                    if (lockTaken)
                    {
                        Monitor.Exit(SyncPosesLock);
                    }
                }
            }

            // SteamVR controller roles can change at runtime.
            {
                uint rightHandIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.RightHand);
                HandR.deviceId = rightHandIndex;
            }
            {
                uint leftHandIndex = OpenVR.System.GetTrackedDeviceIndexForControllerRole(ETrackedControllerRole.LeftHand);
                HandL.deviceId = leftHandIndex;
            }

            {
                bool lockTaken = false;
                try
                {
                    // Reuse the last origin if simulation owns the lock; never block rendering.
                    lockTaken = SyncPlayerToAbsoluteLock.TryAcquireShared();
                    if (lockTaken)
                        RenderPlayerToAbsolute = SyncPlayerToAbsolute;
                }
                finally
                {
                    if (lockTaken)
                        SyncPlayerToAbsoluteLock.ReleaseShared();
                }
            }

            foreach (TrackedDevice device in AllDevices)
            {
                if (!(device.deviceId is OpenVR.k_unTrackedDeviceIndexInvalid))
                {
                    device.SetRenderPoseData(RenderPoses[device.deviceId]);
                }
                else device.SetRenderPoseData(default(TrackedDevicePose_t));
            }
        }

        public static void MainUpdate()
        {
            try
            {
                Monitor.Enter(SyncPosesLock);
                System.Array.Copy(SyncPoses, Poses, Poses.Length);
            }
            finally
            {
                Monitor.Exit(SyncPosesLock);
            }

            foreach (TrackedDevice device in AllDevices)
            {
                if (!(device.deviceId is OpenVR.k_unTrackedDeviceIndexInvalid))
                {
                    device.SetMainPoseData(Poses[device.deviceId]);
                }
                else device.SetMainPoseData(default(TrackedDevicePose_t));
            }

            if (MyInput.Static.IsNewKeyPressed(MyKeys.NumPad0))
                StartCalibration();

            if (CalibratingTicksLeft > 0)
            {
                CalibrationUpdate();
                CalibrationStatus="Measuring headset height: "+((CalibratingTicksLeft+59)/60)+" s";
                CalibratingTicksLeft--;

                if (CalibratingTicksLeft <= 0)
                {
                    FinishCalibration();
                }
            }

            foreach (TrackedDevice device in AllDevices)
            {
                device.MainUpdate();
            }
        }

        public static string CalibrationStatus { get; private set; }="Enter your height above. Stand upright to measure headset height.";
        private static bool estimateCalibrationHeight;
        public static void StartCalibration(int timeTicks = CalibrationTimeTicks,bool estimateBodyHeight=false)
        {
            estimateCalibrationHeight=estimateBodyHeight;
            CalibrationStatus="Measuring standing height…";
            CalibratingTicksLeft = timeTicks;
            PerformanceHud.Notify("Stand upright and look ahead. Measuring for five seconds.",6);

            CalibrationInProgress.armSpan = 0f;
            standingMeasurement.Clear();
        }

        private static void CalibrationUpdate()
        {
            if (Headset.pose.isTracked)
            {
                standingMeasurement.Sample(Headset.pose.deviceToAbsolute.matrix.Translation.Y);
            }

            if (HandL.pose.isTracked && HandR.pose.isTracked)
            {
                Vector3 lPos = HandL.pose.deviceToAbsolute.matrix.Translation;
                Vector3 rPos = HandR.pose.deviceToAbsolute.matrix.Translation;
                float armSpan = Vector2.Distance(new Vector2(lPos.X, lPos.Z), new Vector2(rPos.X, rPos.Z));

                if (CalibrationInProgress.armSpan < armSpan)
                    CalibrationInProgress.armSpan = armSpan;
            }
        }

        public static void FinishCalibration()
        {
            if(standingMeasurement.TryApply(Common.Config,Headset.pose.isTracked,out float measured,estimateCalibrationHeight))
            {
                if(CalibrationInProgress.armSpan>0) Common.Config.PlayerArmSpan=CalibrationInProgress.armSpan;
                using(PlayerCalibrationLock.AcquireExclusiveUsing())
                {
                    PlayerCalibration.height=Common.Config.PlayerHeight;
                    PlayerCalibration.armSpan=Common.Config.PlayerArmSpan;
                }
                ApplyCalibrationOrigin();
                CalibrationStatus=estimateCalibrationHeight ? "Height calibrated" : "Headset height: "+(measured*100).ToString("0.0")+" cm. Standing height kept.";
                PerformanceHud.Notify(CalibrationStatus,5);
            }
            else { CalibrationStatus="Measurement failed. Stand still and check tracking and floor setup."; PerformanceHud.Notify(CalibrationStatus,5); }
            CalibratingTicksLeft=0;
        }
        public static void CancelCalibration()
        {
            CalibrationStatus="Measurement cancelled.";
            CalibratingTicksLeft = 0;
        }

        public static void MovePlayerFloor(Vector3 movement, float rotation)
        {
            Matrix floor = PlayerToAbsolute.matrix;
            floor.Translation += movement;
            floor *= Matrix.CreateRotationY(rotation);

            PlayerToAbsolute = new MatrixAndInvert(floor);

            using (SyncPlayerToAbsoluteLock.AcquireExclusiveUsing())
            {
                SyncPlayerToAbsolute = PlayerToAbsolute;
            }
        }

        public static void ResetPlayerFloor()
        {
            if (!Headset.pose.isTracked) return;
            DetectPosture();
            Matrix floor = VrMath.TrackingOrigin(Headset.pose.deviceToAbsolute.matrix);
            if(BodyFit.Enabled && Sandbox.Game.World.MySession.Static?.LocalCharacter?.IsSitting!=true) floor.Translation=new Vector3(floor.Translation.X,CalibrationReference(),floor.Translation.Z);
            CameraRig.Recenter(PlayerToAbsolute.matrix,floor);
            ThirdPersonView.Recenter(PlayerToAbsolute.matrix,floor);
            SpectatorView.Recenter(floor);
            Logger.Info("Recentered tracking origin at current seated/standing head position.");

            PlayerToAbsolute = new MatrixAndInvert(floor);

            using (SyncPlayerToAbsoluteLock.AcquireExclusiveUsing())
            {
                SyncPlayerToAbsolute = PlayerToAbsolute;
            }
        }

        private static void DetectPosture()
        {
            var config=Common.Config;
            float head=Headset.pose.deviceToAbsolute.matrix.Translation.Y,standing=BodyFit.StandingReference();
            if(!config.BodyCalibrated || standing<1 || !(head>.3f)) return;
            bool seated=config.SeatedPlay ? head<standing*.88f : head<standing*.75f;
            if(seated) config.SeatedReference=head;
            config.SeatedPlay=seated;
        }
        private static float CalibrationReference()
        {
            return Common.Config.SeatedPlay ? Common.Config.SeatedReference : BodyFit.StandingReference();
        }
        public static void SeatOrigin(bool entering)
        {
            if(!BodyFit.Enabled || !Headset.pose.isTracked) return;
            Matrix origin=PlayerToAbsolute.matrix;
            origin.Translation=new Vector3(origin.Translation.X,entering ? Headset.pose.deviceToAbsolute.matrix.Translation.Y : CalibrationReference(),origin.Translation.Z);
            PlayerToAbsolute=new MatrixAndInvert(origin);
            using(SyncPlayerToAbsoluteLock.AcquireExclusiveUsing()) SyncPlayerToAbsolute=PlayerToAbsolute;
        }
        public static void ApplyCalibrationOrigin()
        {
            if(Sandbox.Game.World.MySession.Static?.LocalCharacter?.IsSitting==true) return;
            Matrix origin=PlayerToAbsolute.matrix;
            float reference=BodyFit.Enabled ? CalibrationReference() : Headset.pose.deviceToAbsolute.matrix.Translation.Y;
            origin.Translation=new Vector3(origin.Translation.X,reference,origin.Translation.Z);
            PlayerToAbsolute=new MatrixAndInvert(origin);
            using(SyncPlayerToAbsoluteLock.AcquireExclusiveUsing()) SyncPlayerToAbsolute=PlayerToAbsolute;
        }
        public static void ConsumeRoomscale(Vector3 localOffset)
        {
            Matrix origin = PlayerToAbsolute.matrix;
            origin.Translation += Vector3.TransformNormal(localOffset, origin);
            PlayerToAbsolute = new MatrixAndInvert(origin);
            using (SyncPlayerToAbsoluteLock.AcquireExclusiveUsing()) SyncPlayerToAbsolute = PlayerToAbsolute;
        }

    }
}
