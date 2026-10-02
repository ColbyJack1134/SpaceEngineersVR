using System;
using Sandbox.Game;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class DampenerTargeting
    {
        private static MyCharacter requested;
        private static DateTime reportAfter;
        internal static Vector2 HeadAngles(Vector3D forward,MatrixD body)
        {
            var local=Vector3D.Normalize(Vector3D.TransformNormal(forward,MatrixD.Transpose(body.GetOrientation())));
            return new Vector2((float)(Math.Asin(MathHelper.Clamp(local.Y,-1,1))*180/Math.PI),
                (float)(Math.Atan2(-local.X,-local.Z)*180/Math.PI));
        }
        public static void Activate()
        {
            var character=MySession.Static?.LocalCharacter;
            if(character!=null && MySession.Static.ControlledEntity==character && Player.Headset.pose.isTracked && !ThirdPersonView.Active)
            {
                var head=CameraRig.DeviceWorld(Player.Headset.pose.deviceToAbsolute.matrix);
                var angles=HeadAngles(head.Forward,character.WorldMatrix);
                // Native relative targeting uses these character angles, not the independent VR camera.
                character.HeadLocalXAngle=angles.X; character.HeadLocalYAngle=angles.Y;
                requested=character; reportAfter=DateTime.UtcNow.AddSeconds(.5);
                Logger.Info("Auto dampeners: native head aligned to HMD; pitch="+angles.X.ToString("F1")+"; yaw="+angles.Y.ToString("F1"));
            }
            NativeActions.Pulse(MyControlsSpace.DAMPING_RELATIVE);
        }
        public static void Update()
        {
            if(requested==null || DateTime.UtcNow<reportAfter) return;
            if(requested==MySession.Static?.LocalCharacter && MySession.Static.ControlledEntity==requested && !requested.Closed)
                Logger.Info("Auto dampeners: native relative target="+(requested.RelativeDampeningEntity?.DisplayName ?? "none")+
                    "; damping="+requested.JetpackComp?.DampenersTurnedOn);
            requested=null;
        }
    }
}
