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
            if(message is MyRenderMessageSetCharacterTransforms bones) BodyFit.Render(bones);
            if (Main.VrActive && message is MyRenderMessageSetCameraViewMatrix)
                RenderFrameBridge.Capture(message, CameraRig.Current,ShipCrosshair.Capture());
        }
    }

    [HarmonyPatch(typeof(MyRenderProxy), nameof(MyRenderProxy.AfterUpdate))]
    internal static class CameraFrameCommitPatch
    {
        private static void Prefix() => RenderFrameBridge.Commit();
    }
}
