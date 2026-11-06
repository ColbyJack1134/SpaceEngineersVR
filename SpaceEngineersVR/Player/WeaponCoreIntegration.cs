using System;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Player
{
    // A failure disables WeaponCore support until the next world instead of stopping VR.
    internal static class WeaponCoreIntegration
    {
        private static bool attached;

        internal static void Refresh()
        {
            if(Main.WorldAvailable) WeaponCoreBridge.Load();
            if(!WeaponCoreBridge.ApiReady) return;
            TargetLockToolbar.Register();
            Attach();
        }

        private static void Attach()
        {
            if(attached) return;
            var session=WeaponCoreInternals.SessionType;
            if(session==null) return;
            attached=true;
            // Each part is optional: a WeaponCore change that breaks one leaves the rest running.
            var scope=PatchGroups.WeaponCore.Part("input scope");
            if(!scope.Apply(attach:h=>WeaponCoreScope.Attach(session,scope))) WeaponCoreScope.Detach();
            var aim=PatchGroups.WeaponCore.Part("manual aim");
            if(!aim.Apply(attach:h=>WeaponCoreAim.Attach(session.Assembly.GetType("WeaponCore.Data.Scripts.CoreSystems.Ui.Targeting.TargetUi",false),aim))) WeaponCoreAim.Detach();
        }

        // Main thread, after controls are polled.
        internal static void Update()
        {
            if(!WeaponCoreBridge.ApiReady) return;
            WeaponCoreAlerts.Update(TargetLock.ShooterGrid());
            WeaponCoreAim.UpdatePaintArm();
            HandLock.Update();
            TargetLock.Update();
        }

        internal static void Reset()
        {
            attached=false;
            // Armed actions hold off vanilla fire, so they clear before anything that can throw.
            HandLock.Reset(); TargetLock.Reset();
            WeaponCoreScope.Detach(); WeaponCoreAim.Detach(); PatchGroups.WeaponCore.RemoveParts(); VirtualMouse.Reset(); WeaponCoreReload.Reset(); FeedCamera.Reset();
            WeaponCoreStatus.Reset(); WeaponCoreMarkers.Reset(); WeaponCoreAlerts.Reset();
            WeaponCoreBridge.Unload(); WeaponCoreInternals.Reset();
        }
    }
}
