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
            // Each hook is optional: a WeaponCore change that breaks one leaves the rest running.
            try { WeaponCoreScope.Attach(session); }
            catch(Exception ex) { WeaponCoreScope.Detach(); Logger.Warning(ex,"WeaponCore VR input scope unavailable; VR fire and reload will not reach WeaponCore"); }
            try { WeaponCoreAim.Attach(session.Assembly.GetType("WeaponCore.Data.Scripts.CoreSystems.Ui.Targeting.TargetUi",false)); }
            catch(Exception ex) { WeaponCoreAim.Detach(); Logger.Warning(ex,"WeaponCore VR manual aim and painting unavailable"); }
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
            WeaponCoreScope.Detach(); WeaponCoreAim.Detach(); VirtualMouse.Reset(); WeaponCoreReload.Reset(); FeedCamera.Reset();
            WeaponCoreStatus.Reset(); WeaponCoreMarkers.Reset(); WeaponCoreAlerts.Reset();
            WeaponCoreBridge.Unload(); WeaponCoreInternals.Reset();
        }
    }
}
