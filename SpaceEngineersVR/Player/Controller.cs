using SpaceEngineersVR.Plugin;
using System;
using System.Text;
using SpaceEngineersVR.Util;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    public class Controller : TrackedDevice
    {
        private readonly string handPath;
        internal readonly ControllerFingers Fingers=new ControllerFingers();
        private Matrix aimOffset = Matrix.Identity;
        private Matrix gripOffset = Matrix.Identity;
        private Matrix calibration = Matrix.Identity;
        private bool gripResolved;
        public string ProfileName { get; private set; }="default";
        public string GripSource { get; private set; }="raw";
        public bool IsLeft => handPath.EndsWith("left");
        public string CalibrationKey => "grip/"+(IsLeft ? "left/":"right/")+ProfileName;
        public Matrix GripTracking => VrMath.Affine(calibration * gripOffset * pose.deviceToAbsolute.matrix);
        public Matrix RenderGripTracking => VrMath.Affine(calibration * gripOffset * renderPose.deviceToAbsolute.matrix);
        internal Matrix UncalibratedGripTracking => VrMath.Affine(gripOffset * pose.deviceToAbsolute.matrix);
        private uint aimDevice = OpenVR.k_unTrackedDeviceIndexInvalid;
        private int aimRetry;
        private bool aimResolved;
        public Matrix RenderAimTracking => VrMath.Affine(aimOffset * renderPose.deviceToAbsolute.matrix);
        public Matrix AimTracking => VrMath.Affine(aimOffset * pose.deviceToAbsolute.matrix);

        // Touch handgrip relative to Touch openxr_grip, from the SteamVR Quest 2 render models.
        // Hand placement is tuned on Touch handgrip; openxr_grip is hand-relative by the OpenXR spec on every controller.
        internal static Matrix OpenXrGripToHandgrip(bool left) =>
            Component(0,.003f,.097f,5.037f)*Matrix.Invert(Component(left ? .007f:-.007f,-.00182941f,.1019482f,20.6f));
        private static Matrix Component(float x,float y,float z,float pitch) => Matrix.CreateRotationX(MathHelper.ToRadians(pitch))*Matrix.CreateTranslation(x,y,z);
        // Hand grip in the controller frame. A model whose openxr_grip only copies its handgrip (Steam Frame) did not
        // define a spec openxr_grip, so its handgrip is used directly.
        internal static bool TryGrip(string model,bool left,Matrix? openXr,Matrix? handgrip,out Matrix grip,out string source)
        {
            grip=Matrix.Identity; source="raw";
            var other=handgrip ?? Matrix.Identity;
            if(openXr.HasValue && !(handgrip.HasValue && openXr.Value.EqualsFast(ref other,1e-4f))) { grip=OpenXrGripToHandgrip(left)*openXr.Value; source="openxr_grip"; }
            else if(handgrip.HasValue) { grip=handgrip.Value; source="handgrip"; }
            else return false;
            // Steam Frame: rendered against the Quest 3 hand, its grip sits 14 mm high in the palm.
            if(model.Contains("frame_controller")) grip=Matrix.CreateTranslation(0,.012f,-.007f)*grip;
            return true;
        }

        public override void MainUpdate()
        {
            if (aimDevice != deviceId) { aimDevice=deviceId; Unresolve(); }
            if (!pose.isConnected || (aimResolved && gripResolved) || aimRetry++ % 120 != 0) return;
            var error=ETrackedPropertyError.TrackedProp_Success;
            var model=new StringBuilder(1024);
            OpenVR.System.GetStringTrackedDeviceProperty(deviceId,ETrackedDeviceProperty.Prop_RenderModelName_String,model,1024,ref error);
            ulong source=0;
            if (error!=ETrackedPropertyError.TrackedProp_Success || OpenVR.RenderModels==null ||
                OpenVR.Input.GetInputSourceHandle(handPath,ref source)!=EVRInputError.None) return;
            string name=model.ToString();
            if(ProfileName!=name) { ProfileName=name; ReloadCalibration(); }
            if (!aimResolved && TryComponent(name,"tip",source,out var tip))
            {
                aimOffset=tip;
                aimResolved=true;
                Logger.Info("SteamVR controller tip alignment loaded: "+handPath+" / "+name);
            }
            else if(!aimResolved && aimRetry==1) Logger.Warning("SteamVR tip unavailable for "+handPath+"; using raw controller pose and retrying.");
            if(!gripResolved)
            {
                bool hasHandgrip=TryComponent(name,"handgrip",source,out var handgrip);
                bool hasOpenXr=TryComponent(name,"openxr_grip",source,out var openXr);
                gripResolved=TryGrip(name,IsLeft,hasOpenXr ? openXr:(Matrix?)null,hasHandgrip ? handgrip:(Matrix?)null,out var grip,out var gripSource);
                if(gripResolved) { gripOffset=grip; GripSource=gripSource; }
                if(gripResolved && hasHandgrip) Logger.Info("Hand grip differs from handgrip by "+Difference(gripOffset,handgrip)+" on "+handPath);
                if(gripResolved) Logger.Info("SteamVR hand-grip alignment loaded from "+GripSource+": "+handPath+" / "+name);
                else if(aimRetry==1) Logger.Warning("SteamVR grip unavailable for "+handPath+" / "+name+" (components: "+Components(name)+"); arms use raw controller origin and retry.");
            }
        }

        private static bool TryComponent(string model,string component,ulong source,out Matrix offset)
        {
            var mode=new RenderModel_ControllerMode_State_t();
            var state=new RenderModel_ComponentState_t();
            bool found=OpenVR.RenderModels.GetComponentStateForDevicePath(model,component,source,ref mode,ref state);
            offset=found ? state.mTrackingToComponentLocal.ToMatrix() : Matrix.Identity;
            return found;
        }
        private static string Components(string model)
        {
            var names=new StringBuilder(); var name=new StringBuilder(256);
            uint count=OpenVR.RenderModels.GetComponentCount(model);
            for(uint i=0;i<count;i++)
            {
                name.Clear();
                if(OpenVR.RenderModels.GetComponentName(model,i,name,256)>0) names.Append(names.Length>0 ? ", ":"").Append(name);
            }
            return names.Length>0 ? names.ToString() : "none";
        }
        private static string Difference(Matrix a,Matrix b)
        {
            var delta=Quaternion.CreateFromRotationMatrix(a.GetOrientation()*Matrix.Transpose(b.GetOrientation()));
            float degrees=MathHelper.ToDegrees(2*(float)Math.Acos(Math.Min(1,Math.Abs(delta.W))));
            return degrees.ToString("0.0")+" deg, "+((a.Translation-b.Translation).Length()*1000).ToString("0.0")+" mm";
        }

        internal ulong InputSource { get { ulong source=0; OpenVR.Input.GetInputSourceHandle(handPath,ref source); return source; } }
        internal void ReloadCalibration()
        {
            calibration=Alignment.Get(CalibrationKey).Matrix;
        }
        private void Unresolve()
        {
            aimResolved=gripResolved=false; aimOffset=gripOffset=Matrix.Identity; GripSource="raw"; aimRetry=0;
        }

        public Controller(string actionName, string hapticsName)
            : base(actionName, hapticsName)
        {
            handPath=actionName.Contains("Right") ? "/user/hand/right" : "/user/hand/left";
        }


        protected override void OnConnected() { Logger.Info("Controller connected: " + deviceId); }
        protected override void OnDisconnected() { Fingers.Reset(); Unresolve(); Logger.Info("Controller disconnected; input will be released."); }

    }
}
