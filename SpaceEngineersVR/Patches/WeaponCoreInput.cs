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
    // VR fire (as left mouse) and reload (as WeaponCore's raw reload key) are reported only while WeaponCore's session code is
    // on the stack, so vanilla controls and other mods never see them. Attached only while a WeaponCore world is loaded.
    internal static class WeaponCoreScope
    {
        [ThreadStatic] private static int depth;
        private static readonly List<KeyValuePair<MethodBase,MethodInfo>> patched=new List<KeyValuePair<MethodBase,MethodInfo>>();
        internal static bool Inside => depth>0;

        internal static void Attach(Type session)
        {
            if(patched.Count>0 || session==null) return;
            foreach(string name in new[] {"HandleInput","UpdateBeforeSimulation","Simulate","UpdateAfterSimulation","Draw"})
                Patch(AccessTools.DeclaredMethod(session,name,Type.EmptyTypes),nameof(Enter),nameof(Leave));
            if(patched.Count==0) { Logger.Info("WeaponCore session updates not found; VR fire and reload will not reach WeaponCore"); return; }
            var input=AccessTools.TypeByName("VRage.Input.MyVRageInput");
            Patch(AccessTools.Method(input,"IsMousePressed",new[] {typeof(MyMouseButtonsEnum)}),postfix:nameof(MousePressed));
            foreach(string name in new[] {"IsAnyMousePressed","IsNewLeftMousePressed","IsNewLeftMouseReleased"})
                Patch(AccessTools.Method(input,name,Type.EmptyTypes),postfix:nameof(MouseEdge));
            foreach(string name in new[] {"IsKeyPress","IsNewKeyPressed","IsNewKeyReleased"})
                Patch(AccessTools.Method(input,name,new[] {typeof(MyKeys)}),postfix:nameof(Key));
        }

        private static void Patch(MethodBase method,string prefix=null,string finalizer=null,string postfix=null)
        {
            if(method==null) return;
            HarmonyMethod Hook(string name) => name==null ? null:new HarmonyMethod(typeof(WeaponCoreScope),name);
            Common.Plugin.Harmony.Patch(method,Hook(prefix),Hook(postfix),finalizer:Hook(finalizer));
            foreach(string name in new[] {prefix,finalizer,postfix})
                if(name!=null) patched.Add(new KeyValuePair<MethodBase,MethodInfo>(method,AccessTools.Method(typeof(WeaponCoreScope),name)));
        }

        internal static void Detach()
        {
            foreach(var pair in patched) Common.Plugin.Harmony.Unpatch(pair.Key,pair.Value);
            patched.Clear(); depth=0;
        }

        // __state stays false when another prefix skips ours, so the depth only drops for an Enter that ran.
        private static void Enter(out bool __state) { depth++; __state=true; }
        private static Exception Leave(Exception __exception,bool __state) { if(__state) depth--; return __exception; }

        private static void MousePressed(MyMouseButtonsEnum button,ref bool __result) { if(!__result) __result=VirtualMouse.Down(button); }
        private static void MouseEdge(MethodBase __originalMethod,ref bool __result)
        {
            if(__result) return;
            switch(__originalMethod.Name)
            {
                case "IsAnyMousePressed": __result=VirtualMouse.Any(); break;
                case "IsNewLeftMousePressed": __result=VirtualMouse.NewLeft(); break;
                case "IsNewLeftMouseReleased": __result=VirtualMouse.ReleasedLeft(); break;
            }
        }
        private static void Key(MyKeys key,MethodBase __originalMethod,ref bool __result)
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

    // WeaponCore fires on raw left mouse rather than game controls; mirrors the VR fire trigger onto it.
    internal static class VirtualMouse
    {
        private static int frame=int.MinValue;
        private static bool left,prevLeft,failed;

        // Seated or on a turret; on foot only while a WeaponCore hand weapon is out.
        private static bool Active => WeaponCoreBridge.ApiReady && Main.VrActive && !Main.MenuOpen &&
            (InputRouter.Mode==InputMode.Piloting || InputRouter.Mode==InputMode.Turret ||
             (InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Jetpack) && WeaponCoreInternals.Available && WeaponCoreInternals.HandheldActive);

        // State advances once per tick, whoever asks first.
        private static void Sample()
        {
            int now=MySession.Static?.GameplayFrameCounter ?? 0;
            if(now==frame) return;
            frame=now; prevLeft=left;
            // Runs inside the game's input checks, where Integration cannot catch a failure.
            try { left=!failed && Active && SpaceEngineersVR.Player.Components.VRMovementComponent.PrimaryFire; }
            catch(Exception ex) { failed=true; left=prevLeft=false; Logger.Warning(ex,"WeaponCore VR fire disabled until the next world"); }
        }

        internal static void Reset() { frame=int.MinValue; left=prevLeft=failed=false; }

        internal static bool Down(MyMouseButtonsEnum button) { Sample(); return WeaponCoreScope.Inside && button==MyMouseButtonsEnum.Left && left; }
        internal static bool Any() { Sample(); return WeaponCoreScope.Inside && left; }
        internal static bool NewLeft() { Sample(); return WeaponCoreScope.Inside && left && !prevLeft; }
        internal static bool ReleasedLeft() { Sample(); return WeaponCoreScope.Inside && !left && prevLeft; }
    }

    // WeaponCore hand weapons reload on the raw key bound to vanilla Reload, which it blocks as a control. It counts ticks
    // held: 60 cycles ammo, any shorter hold reloads on release.
    internal static class WeaponCoreReload
    {
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
            try
            {
                bool active=!Main.MenuOpen && Ready;
                // Deactivating mid-hold reports no release, which WeaponCore would take as a reload.
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
}
