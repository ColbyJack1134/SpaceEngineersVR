using System;
using SpaceEngineersVR.Patches;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;

namespace SpaceEngineersVR.Player
{
    // WeaponCore support as one Integration (see Main): a failure disables it until the next world instead of stopping VR.
    internal static class WeaponCoreIntegration
    {
        private static bool attached;
        private static DateTime retry;

        internal static void Refresh()
        {
            if(Main.WorldAvailable) WeaponCoreBridge.Load();
            if(!WeaponCoreBridge.ApiReady) return;
            TargetLockToolbar.Register();
            Attach();
        }

        // Input scope and manual-aim hooks on the live world's WeaponCore session; retried each second until it is found.
        private static void Attach()
        {
            if(attached || DateTime.UtcNow<retry) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            var session=ModReflection.LiveType("CoreSystems.Session","I");
            if(session==null) return;
            attached=true;
            // Each hook is optional: a WeaponCore change that breaks one leaves the rest of the support running.
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
            attached=false; retry=DateTime.MinValue;
            WeaponCoreScope.Detach(); WeaponCoreAim.Detach(); VirtualMouse.Reset(); WeaponCoreReload.Reset();
            TargetLock.Reset(); HandLock.Reset(); FeedCamera.Reset();
            WeaponCoreStatus.Reset(); WeaponCoreMarkers.Reset(); WeaponCoreAlerts.Reset();
            WeaponCoreBridge.Unload(); WeaponCoreInternals.Reset();
        }
    }
}
