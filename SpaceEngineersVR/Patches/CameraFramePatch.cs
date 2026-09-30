using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageRender;
using VRageRender.Messages;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyRenderProxy), "EnqueueMessage")]
    internal static class CameraFramePatch
    {
        private static void Prefix(MyRenderMessageBase message)
        {
            if (Main.VrActive && message is MyRenderMessageSetCameraViewMatrix)
                RenderFrameBridge.Capture(message, CameraRig.Current);
        }
    }
}
