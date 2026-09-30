using System;
using System.Text;
using Valve.VR;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Util;

namespace SpaceEngineersVR.Diagnostics
{
    public static class VrProbe
    {
        // Background mode reads tracking without taking over the headset's scene application.
        public static bool Run(Action<string> log)
        {
            bool initialized = false;
            try
            {
                bool installed = OpenVR.IsRuntimeInstalled();
                bool present = installed && OpenVR.IsHmdPresent();
                log("SteamVR installed: " + installed + "; headset available: " + present);
                if (!present)
                {
                    log("Connect Quest 3 in Virtual Desktop, launch SteamVR, then rerun the VR probe.");
                    return false;
                }
                EVRInitError error = EVRInitError.None;
                OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
                initialized = error == EVRInitError.None;
                log("OpenVR background initialization: " + error);
                if (!initialized)
                {
                    log("Start SteamVR from Virtual Desktop and rerun this probe with the headset connected.");
                    return false;
                }
                uint width = 0, height = 0;
                OpenVR.System.GetRecommendedRenderTargetSize(ref width, ref height);
                log("Recommended per-eye render target: " + width + " x " + height);
                foreach (EVREye eye in new[] { EVREye.Eye_Left, EVREye.Eye_Right })
                {
                    float l=0,r=0,t=0,b=0;
                    OpenVR.System.GetProjectionRaw(eye,ref l,ref r,ref t,ref b);
                    var reference=OpenVR.System.GetProjectionMatrix(eye,0.05f,1000f).ToMatrix();
                    var ours=VrMath.Projection(l,r,t,b,0.05);
                    double difference=Math.Max(Math.Max(Math.Abs(reference.M11-ours.M11),Math.Abs(reference.M22-ours.M22)),
                        Math.Max(Math.Abs(reference.M31-ours.M31),Math.Abs(reference.M32-ours.M32)));
                    log(eye+" projection XY difference vs runtime="+difference.ToString("G6")+"; raw="+l+","+r+","+t+","+b);
                    if (difference>0.0001) throw new InvalidOperationException("Eye projection disagrees with SteamVR");
                }
                var poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
                OpenVR.System.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseStanding, 0, poses);
                for (uint i = 0; i < poses.Length; i++)
                {
                    if (!poses[i].bDeviceIsConnected) continue;
                    var value = new StringBuilder(256);
                    var propertyError = ETrackedPropertyError.TrackedProp_Success;
                    OpenVR.System.GetStringTrackedDeviceProperty(i, ETrackedDeviceProperty.Prop_ModelNumber_String, value, 256, ref propertyError);
                    log("Device " + i + ": " + OpenVR.System.GetTrackedDeviceClass(i) + " " + value + "; pose valid=" + poses[i].bPoseIsValid + "; activity=" + OpenVR.System.GetTrackedDeviceActivityLevel(i));
                }
                foreach(var role in new[] { ETrackedControllerRole.LeftHand,ETrackedControllerRole.RightHand })
                {
                    uint id=OpenVR.System.GetTrackedDeviceIndexForControllerRole(role);
                    if(id==OpenVR.k_unTrackedDeviceIndexInvalid) { log(role+" tip alignment: controller unavailable"); continue; }
                    var model=new StringBuilder(1024);
                    var err=ETrackedPropertyError.TrackedProp_Success;
                    OpenVR.System.GetStringTrackedDeviceProperty(id,ETrackedDeviceProperty.Prop_RenderModelName_String,model,1024,ref err);
                    ulong path=0;
                    var inputError=OpenVR.Input.GetInputSourceHandle(role==ETrackedControllerRole.LeftHand ? "/user/hand/left" : "/user/hand/right",ref path);
                    var mode=new RenderModel_ControllerMode_State_t();
                    var state=new RenderModel_ComponentState_t();
                    bool tip=err==ETrackedPropertyError.TrackedProp_Success && inputError==EVRInputError.None &&
                        OpenVR.RenderModels!=null && OpenVR.RenderModels.GetComponentStateForDevicePath(model.ToString(),"tip",path,ref mode,ref state);
                    log(role+" SteamVR tip alignment available="+tip+"; model="+model);
                    if(tip) log("  tip local forward="+state.mTrackingToComponentLocal.ToMatrix().Forward+"; position="+state.mTrackingToComponentLocal.ToMatrix().Translation);
                }
                bool tracked = poses[OpenVR.k_unTrackedDeviceIndex_Hmd].bPoseIsValid;
                if (!tracked) log("Headset pose is not valid yet. Wake/wear the headset and retry.");
                log("Tracking flags are reported by the driver; valid poses alone do not confirm a live headset stream or working buttons.");
                return tracked;
            }
            catch (Exception ex)
            {
                log("VR probe failed: " + ex.GetBaseException().Message);
                return false;
            }
            finally { if (initialized) OpenVR.Shutdown(); }
        }
    }
}
