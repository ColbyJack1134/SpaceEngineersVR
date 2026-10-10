using HarmonyLib;
using Sandbox.Engine.Multiplayer;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyMultiplayer),nameof(MyMultiplayer.TeleportControlledEntity))]
    internal static class SpectatorTeleportPatch
    {
        private static void Prefix(ref Vector3D location)
        {
            if(!SpectatorView.CanTeleport || !Player.Player.Headset.pose.isTracked) return;
            var eye=CameraRig.DeviceWorld(Player.Player.Headset.pose.deviceToAbsolute.matrix);
            if(eye.IsValid()) location=eye.Translation;
        }
    }
}
