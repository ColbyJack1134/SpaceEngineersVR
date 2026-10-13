using System;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Replication.ClientStates;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // A host without SEVR rebuilds shots and tool sensors from the sent head pitch, which VR otherwise leaves level.
    // Host shots ignore head yaw, so horizontal aim follows the body facing.
    [HarmonyPatch(typeof(MyCharacter),nameof(MyCharacter.GetNetState))]
    internal static class AimPitchSyncPatch
    {
        private static bool disabled;

        private static void Postfix(MyCharacter __instance,ref MyCharacterClientState state)
        {
            if(disabled || !Main.VrActive || CameraRig.Detached || __instance!=MySession.Static?.LocalCharacter ||
                __instance.IsSitting || !Player.Player.Headset.pose.isTracked) return;
            try
            {
                var direction=__instance.CurrentWeapon!=null && MotionToolPatch.Eligible(__instance) && MotionToolPatch.TryAim(__instance,out _,out _,out var aim) ? aim:
                    CameraRig.DeviceWorld(Player.Player.Headset.pose.deviceToAbsolute.matrix).Forward;
                if(direction.IsValid() && direction.LengthSquared()>1e-6) state.HeadX=Pitch(direction,__instance.WorldMatrix.Up);
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Aim pitch sync disabled"); }
        }

        internal static float Pitch(Vector3D direction,Vector3D up) =>
            MathHelper.ToDegrees((float)Math.Asin(MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(direction),up),-1,1)));
    }
}
