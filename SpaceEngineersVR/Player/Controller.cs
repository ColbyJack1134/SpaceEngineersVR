using SpaceEngineersVR.Plugin;
using System.Text;
using SpaceEngineersVR.Util;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public class Controller : TrackedDevice
    {
        private readonly string handPath;
        private Matrix aimOffset = Matrix.Identity;
        private Matrix gripOffset = Matrix.Identity;
        private bool gripResolved;
        public string ProfileName { get; private set; }="default";
        public Matrix GripTracking => VrMath.Affine(gripOffset * pose.deviceToAbsolute.matrix);
        public Matrix RenderGripTracking => VrMath.Affine(gripOffset * renderPose.deviceToAbsolute.matrix);
        private uint aimDevice = OpenVR.k_unTrackedDeviceIndexInvalid;
        private int aimRetry;
        private bool aimResolved;
        public Matrix RenderAimTracking => VrMath.Affine(aimOffset * renderPose.deviceToAbsolute.matrix);
        public Matrix AimTracking => VrMath.Affine(aimOffset * pose.deviceToAbsolute.matrix);

        public override void MainUpdate()
        {
            if (aimDevice != deviceId) { aimDevice=deviceId; aimOffset=gripOffset=Matrix.Identity; aimResolved=gripResolved=false; aimRetry=0; }
            if (!pose.isConnected || (aimResolved && gripResolved) || aimRetry++ % 120 != 0) return;
            var error=ETrackedPropertyError.TrackedProp_Success;
            var model=new StringBuilder(1024);
            OpenVR.System.GetStringTrackedDeviceProperty(deviceId,ETrackedDeviceProperty.Prop_RenderModelName_String,model,1024,ref error);
            ulong source=0;
            if (error!=ETrackedPropertyError.TrackedProp_Success || OpenVR.RenderModels==null ||
                OpenVR.Input.GetInputSourceHandle(handPath,ref source)!=EVRInputError.None) return;
            ProfileName=model.ToString();
            var mode=new RenderModel_ControllerMode_State_t();
            var component=new RenderModel_ComponentState_t();
            if (!aimResolved && OpenVR.RenderModels.GetComponentStateForDevicePath(model.ToString(),"tip",source,ref mode,ref component))
            {
                aimOffset=component.mTrackingToComponentLocal.ToMatrix();
                aimResolved=true;
                Logger.Info("SteamVR controller tip alignment loaded: "+handPath+" / "+model);
            }
            else if(!aimResolved && aimRetry==1) Logger.Warning("SteamVR tip unavailable for "+handPath+"; using raw controller pose and retrying.");
            if(!gripResolved && OpenVR.RenderModels.GetComponentStateForDevicePath(model.ToString(),"handgrip",source,ref mode,ref component))
            {
                gripOffset=component.mTrackingToComponentLocal.ToMatrix(); gripResolved=true;
                Logger.Info("SteamVR hand-grip alignment loaded: "+handPath);
            }
            else if(!gripResolved && aimRetry==1) Logger.Warning("SteamVR handgrip unavailable for "+handPath+"; arms use raw controller origin and retry.");
        }

        public Controller(string actionName, string hapticsName)
            : base(actionName, hapticsName)
        {
            handPath=actionName.Contains("Right") ? "/user/hand/right" : "/user/hand/left";
        }


        protected override void OnConnected() { Logger.Info("Controller connected: " + deviceId); }
        protected override void OnDisconnected() { aimResolved=gripResolved=false; aimOffset=gripOffset=Matrix.Identity; aimRetry=0; Logger.Info("Controller disconnected; input will be released."); }

    }
}
