using System;
using System.Collections.Generic;
using System.Linq;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // An action started from a wrist, wheel or cockpit button and completed by the next trigger pull, so the hand that
    // pressed the button is free to aim first. The pull is consumed and never fires; firing is held off while any arm is set.
    internal sealed class TriggerArm
    {
        private static readonly List<TriggerArm> all=new List<TriggerArm>();
        internal static bool AnyArmed => all.Any(a=>a.Armed);

        private readonly double seconds;
        private DateTime until;
        private bool releaseWait;
        internal bool Armed { get; private set; }

        internal TriggerArm(double seconds) { this.seconds=seconds; all.Add(this); }

        internal void Arm()
        {
            Armed=true; until=DateTime.UtcNow.AddSeconds(seconds);
            // The trigger that pressed the button may still be down; completing needs a fresh pull.
            releaseWait=true;
        }

        internal void Disarm() { Armed=false; }

        internal bool Expired => Armed && DateTime.UtcNow>until;

        // Main thread, after controls are polled. True on the pull that completes the action; the arm is then cleared and the
        // trigger blocked until released, so the pull never becomes a shot.
        internal bool Pulled()
        {
            if(!Armed) return false;
            var fire=Controls.Static.Fire;
            var hand=Handedness.ActionHand;
            bool free=!Main.MenuOpen && InputRouter.Gameplay && !HelmetHud.ProtectsFire && !FloatingWindows.OwnsInput && !SpatialUi.Owns(hand) &&
                !CockpitTouch.Owns(hand) && !HandInteraction.Consumes(hand) && !TouchScreenBridge.OwnsInput && !ArthurLcdBridge.Owns(hand);
            if(!Pulled(fire.RawPressed,fire.HasPressed,free)) return false;
            fire.BlockUntilRelease();
            return true;
        }

        // The arm's decision for one frame of trigger state; free is false while the trigger belongs to a UI.
        internal bool Pulled(bool rawPressed,bool hasPressed,bool free)
        {
            if(!Armed) return false;
            if(releaseWait) { if(!rawPressed) releaseWait=false; return false; }
            if(!free || !hasPressed) return false;
            Armed=false;
            return true;
        }
    }
}
