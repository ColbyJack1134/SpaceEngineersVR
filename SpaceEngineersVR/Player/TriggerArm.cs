using System;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // A button arms the action and the next trigger pull completes it, so the pressing hand can aim first; that pull never fires.
    internal sealed class TriggerArm
    {
        private static int armedCount;
        internal static bool AnyArmed => armedCount>0;

        private readonly double seconds;
        private DateTime until;
        private bool releaseWait;
        private bool armed;
        internal bool Armed
        {
            get => armed;
            private set { if(armed==value) return; armed=value; armedCount+=value ? 1:-1; }
        }

        internal TriggerArm(double seconds) { this.seconds=seconds; }

        internal void Arm()
        {
            Armed=true; until=DateTime.UtcNow.AddSeconds(seconds);
            // The trigger that pressed the button may still be down; completing needs a fresh pull.
            releaseWait=true;
        }

        internal void Disarm() { Armed=false; }

        internal bool Expired => Armed && DateTime.UtcNow>until;

        // True on the pull that completes the action; that pull is blocked until release, so it never fires.
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
