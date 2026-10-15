using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Input;

namespace SpaceEngineersVR.Patches
{
    // WeaponCore hand weapons reload on the raw key bound to vanilla Reload, which it blacklists as a control. It counts ticks
    // held: 60 cycles ammo, and any release reloads. The VR Reload button and wrist actions report that key as held.
    internal static class WeaponCoreReload
    {
        // One tick held then released is a tap; 62 ticks clears WeaponCore's 60-tick hold before the release.
        private const int TapTicks=1,CycleTicks=62;
        private static int frame=int.MinValue,scripted;
        private static bool held,wasHeld,failed;
        private static MyKeys key=MyKeys.None;

        internal static bool Ready => !failed && WeaponCoreBridge.ApiReady && Main.VrActive &&
            (InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Jetpack) && WeaponCoreInternals.Available &&
            WeaponCoreInternals.HandheldActive && WeaponCoreInternals.ReloadKey!=MyKeys.None;

        internal static void Tap() { if(Ready) scripted=TapTicks; }
        internal static void Cycle() { if(Ready) scripted=CycleTicks; }

        private static void Sample()
        {
            int now=MySession.Static?.GameplayFrameCounter ?? 0;
            if(now==frame) return;
            frame=now; wasHeld=held;
            // Runs inside the game's input checks, where Integration cannot catch a failure.
            try
            {
                bool active=!Main.MenuOpen && Ready;
                // No release is reported when deactivated mid-hold, since WeaponCore would treat it as a reload; an interrupted
                // scripted hold is dropped rather than resumed later.
                key=active ? WeaponCoreInternals.ReloadKey : MyKeys.None;
                bool script=active && scripted>0;
                scripted=script ? scripted-1 : 0;
                held=active && (Controls.Static.Reload.RawPressed || script);
            }
            catch(Exception ex) { failed=true; key=MyKeys.None; held=false; scripted=0; Logger.Warning(ex,"WeaponCore VR reload disabled until the next world"); }
        }

        internal static void Reset() { frame=int.MinValue; held=wasHeld=failed=false; key=MyKeys.None; scripted=0; }

        internal static bool Is(MyKeys queried) { Sample(); return WeaponCoreScope.Inside && key!=MyKeys.None && queried==key; }
        internal static bool Down => held;
        internal static bool Pressed => held && !wasHeld;
        internal static bool Released => !held && wasHeld;
    }

    [HarmonyPatch]
    internal static class WeaponCoreReloadPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var input=AccessTools.TypeByName("VRage.Input.MyVRageInput");
            foreach(string name in new[] { "IsKeyPress","IsNewKeyPressed","IsNewKeyReleased" })
            {
                var method=AccessTools.Method(input,name,new[] { typeof(MyKeys) });
                if(method!=null) yield return method;
            }
        }
        private static void Postfix(MyKeys key,MethodBase __originalMethod,ref bool __result)
        {
            if(__result || !WeaponCoreReload.Is(key)) return;
            switch(__originalMethod.Name)
            {
                case "IsKeyPress": __result=WeaponCoreReload.Down; break;
                case "IsNewKeyPressed": __result=WeaponCoreReload.Pressed; break;
                default: __result=WeaponCoreReload.Released; break;
            }
        }
    }
}
