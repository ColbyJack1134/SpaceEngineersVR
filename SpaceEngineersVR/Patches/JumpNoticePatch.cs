using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Localization;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Utils;

namespace SpaceEngineersVR.Patches
{
    // Native HUD notifications are not drawn in the headset, so a refused jump otherwise looks like nothing happened.
    [HarmonyPatch(typeof(MyHudNotifications),nameof(MyHudNotifications.Add),new[] {typeof(MyHudNotificationBase)})]
    internal static class JumpNoticePatch
    {
        private static readonly HashSet<MyStringId> Refusals=new HashSet<MyStringId> {
            MySpaceTexts.NotificationCannotJumpFromGravity,MySpaceTexts.NotificationCannotJumpIntoGravity,
            MySpaceTexts.NotificationCannotJumpOutsideWorld,MySpaceTexts.NotificationJumpIsBlocked,
            MySpaceTexts.NotificationJumpAbortedStatic,MySpaceTexts.NotificationJumpAbortedLocked,
            MySpaceTexts.NotificationJumpAbortedNoLocation,MySpaceTexts.NotificationJumpAbortedShortDistance,
            MySpaceTexts.NotificationJumpAbortedAlreadyJumping,MySpaceTexts.NotificationJumpAborted };
        private static void Postfix(MyHudNotificationBase notification)
        {
            if(Main.VrActive && notification is MyHudNotification native && Refusals.Contains(native.Text))
                EssentialHud.Notify(native.GetText());
        }
    }
}
