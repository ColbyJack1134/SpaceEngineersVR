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
    // A host without SEVR rebuilds shots and tool sensors from the sent body rotation and head pitch, not the VR hand.
    // Only the sent state faces the held item; the local body keeps following the head.
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
                var aim=MotionToolPatch.HostAim;
                var direction=aim ?? CameraRig.DeviceWorld(Player.Player.Headset.pose.deviceToAbsolute.matrix).Forward;
                if(!direction.IsValid() || direction.LengthSquared()<1e-6) return;
                state.HeadX=Pitch(direction,__instance.WorldMatrix.Up);
                if(aim.HasValue && !__instance.JetpackRunning) FaceForHost(__instance.WorldMatrix,aim.Value,ref state.Rotation,ref state.MoveIndicator);
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Aim pitch sync disabled"); }
        }

        // The host applies the rotation, then moves in that frame, so the move indicator turns back to keep world movement.
        internal static void FaceForHost(MatrixD body,Vector3D aim,ref Quaternion rotation,ref Vector3 move)
        {
            var up=body.Up;
            var level=aim-up*Vector3D.Dot(aim,up);
            if(level.LengthSquared()<.01) return;
            var facing=MatrixD.CreateWorld(Vector3D.Zero,Vector3D.Normalize(level),up);
            move=(Vector3)Vector3D.TransformNormal(Vector3D.TransformNormal(move,body.GetOrientation()),MatrixD.Transpose(facing));
            rotation=Quaternion.CreateFromRotationMatrix(facing);
        }

        internal static float Pitch(Vector3D direction,Vector3D up) =>
            MathHelper.ToDegrees((float)Math.Asin(MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(direction),up),-1,1)));
    }
}
