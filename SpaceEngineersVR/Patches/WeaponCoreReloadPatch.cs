using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using VRage.Input;

namespace SpaceEngineersVR.Patches
{
    // WeaponCore hand weapons reload on their own raw key (the keyboard key bound to vanilla Reload), not the game's Reload
    // control, which WeaponCore blacklists while it tracks the weapon. Per tick it counts the key held: reaching 60 cycles
    // ammo, and any release forces a reload. While a WeaponCore hand weapon is out, the VR Reload button and the wrist
    // Reload / Cycle ammo actions report that key as held.
    internal static class WeaponCoreReload
    {
        // One tick held then released is a tap; 62 ticks clears WeaponCore's 60-tick hold before the release.
        private const int TapTicks=1,CycleTicks=62;
        private static int frame=int.MinValue,scripted;
        private static bool held,wasHeld;
        private static MyKeys key=MyKeys.None;

        internal static bool Ready => WeaponCoreBridge.ApiReady && Plugin.Main.VrActive &&
            (InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Jetpack) && WeaponCoreInternals.Available &&
            WeaponCoreInternals.HandheldActive && WeaponCoreInternals.ReloadKey!=MyKeys.None;

        internal static void Tap() { if(Ready) scripted=TapTicks; }
        internal static void Cycle() { if(Ready) scripted=CycleTicks; }

        internal static readonly ActionChoice CycleAmmoAction=new ActionChoice("Cycle ammo",Cycle,enabled:()=>Ready,
            searchTerms:"weaponcore wc ammo cycle switch magazine type");

        private static void Sample()
        {
            int now=MySession.Static?.GameplayFrameCounter ?? 0;
            if(now==frame) return;
            frame=now; wasHeld=held;
            bool active=!Plugin.Main.MenuOpen && Ready;
            // No release is reported when deactivated mid-hold, since WeaponCore would treat it as a reload; an interrupted
            // scripted hold is dropped rather than resumed later.
            key=active ? WeaponCoreInternals.ReloadKey : MyKeys.None;
            bool script=active && scripted>0;
            scripted=script ? scripted-1 : 0;
            held=active && (Controls.Static.Reload.RawPressed || script);
        }

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
