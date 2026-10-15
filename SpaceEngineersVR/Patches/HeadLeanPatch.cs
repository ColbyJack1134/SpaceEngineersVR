using System;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Sync;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    // The engine bends the upper body with head pitch on foot only; a desktop jetpack pitches the whole body instead.
    // A VR head pitches on its own, so the body bends with the headset in both, and looking down shows the legs.
    [HarmonyPatch(typeof(MyCharacter),"UpdateFallAndSpine")]
    internal static class HeadLeanPatch
    {
        private static readonly FieldInfo leaning=AccessTools.Field(typeof(MyCharacter),"m_animLeaning");
        private static bool disabled;

        private static void Postfix(MyCharacter __instance)
        {
            if(disabled || !Main.VrActive || __instance!=MySession.Static?.LocalCharacter || !__instance.UseNewAnimationSystem ||
                __instance.IsDead || __instance.IsSitting || __instance.IsOnLadder || ThirdPersonView.Active || CameraRig.Detached ||
                !Player.Player.Headset.pose.isTracked) return;
            try
            {
                var gaze=CameraRig.DeviceWorld(Player.Player.Headset.pose.deviceToAbsolute.matrix).Forward;
                if(!gaze.IsValid()) return;
                // Same clamp and multiplier as the native on-foot bend; whole degrees keep headset jitter off the synced value.
                float pitch=MathHelper.Clamp((float)Math.Round(-AimPitchSyncPatch.Pitch(gaze,__instance.WorldMatrix.Up)),-89.9f,89f);
                ((Sync<float,SyncDirection.BothWays>)leaning.GetValue(__instance)).Value=__instance.Definition.BendMultiplier3rd*pitch;
            }
            catch(Exception ex) { disabled=true; Logger.Warning(ex,"Head-driven body lean disabled"); }
        }
    }
}
