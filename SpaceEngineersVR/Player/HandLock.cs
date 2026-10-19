using Sandbox.Game.World;
using VRage.Game.Entity;

namespace SpaceEngineersVR.Player
{
    // Guided WeaponCore hand weapons: the grip is the gun's secondary action, so the wheel arms the lock and a trigger pull locks.
    internal static class HandLock
    {
        private static readonly TriggerArm arm=new TriggerArm(10);
        internal static bool Armed => arm.Armed;
        internal static MyEntity Preview { get; private set; }
        internal static readonly ActionChoice Action=new ActionChoice(()=>Armed ? "Cancel lock":"Lock target",Toggle,()=>Shooter()!=null,"WeaponCore hand lock");

        internal static MyEntity Shooter() =>
            WeaponCoreInternals.Available && WeaponCoreInternals.HandheldActive && WeaponCoreInternals.SmartHandheld ? MySession.Static?.LocalCharacter : null;

        internal static void Reset() { arm.Disarm(); Preview=null; }

        private static void Toggle()
        {
            if(Armed) { Reset(); EssentialHud.Notify("Lock cancelled"); return; }
            if(Shooter()==null) return;
            arm.Arm();
            EssentialHud.Notify("Lock armed: aim and pull the trigger");
            Handedness.ActionHand.Vibrate(0,.03f,120,.15f);
        }

        internal static void Update()
        {
            Preview=null;
            if(!Armed) return;
            var shooter=Shooter();
            if(shooter==null) { Reset(); return; }
            if(arm.Expired) { Reset(); EssentialHud.Notify("Lock cancelled"); return; }
            var hand=Handedness.ActionHand;
            if(GridSelection.TryPointer(hand,out var pose)) Preview=TargetLock.Pick(shooter,pose.Translation,pose.Forward);
            if(!arm.Pulled()) return;
            TargetLock.Toggle(shooter,Preview,true);
            hand.Vibrate(0,.025f,90,Preview!=null ? .25f:.12f);
            Preview=null;
        }
    }
}
