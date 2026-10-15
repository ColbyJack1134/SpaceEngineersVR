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
    // Synthetic WeaponCore input (fire as left mouse, reload as its raw key) must not reach vanilla controls or other mods,
    // which read the same input methods. It is reported only while WeaponCore's own session code is on the stack.
    // Every session component reads input from inside one of these overrides.
    internal static class WeaponCoreScope
    {
        [ThreadStatic] private static int depth;
        private static readonly List<MethodBase> patched=new List<MethodBase>();
        internal static bool Inside => depth>0;

        internal static void Attach(Type session)
        {
            if(patched.Count>0 || session==null) return;
            foreach(string name in new[] {"HandleInput","UpdateBeforeSimulation","Simulate","UpdateAfterSimulation","Draw"})
            {
                var method=AccessTools.DeclaredMethod(session,name,Type.EmptyTypes);
                if(method==null) continue;
                Common.Plugin.Harmony.Patch(method,new HarmonyMethod(typeof(WeaponCoreScope),nameof(Enter)),
                    finalizer:new HarmonyMethod(typeof(WeaponCoreScope),nameof(Leave)));
                patched.Add(method);
            }
            if(patched.Count==0) Logger.Info("WeaponCore session updates not found; VR fire and reload will not reach WeaponCore");
        }

        internal static void Detach()
        {
            foreach(var method in patched)
            {
                Common.Plugin.Harmony.Unpatch(method,AccessTools.Method(typeof(WeaponCoreScope),nameof(Enter)));
                Common.Plugin.Harmony.Unpatch(method,AccessTools.Method(typeof(WeaponCoreScope),nameof(Leave)));
            }
            patched.Clear(); depth=0;
        }

        private static void Enter(out bool __state) { depth++; __state=true; }
        private static Exception Leave(Exception __exception,bool __state) { if(__state) depth--; return __exception; }
    }

    // WeaponCore fires on raw left mouse rather than game controls; mirror the VR fire trigger onto it.
    internal static class VirtualMouse
    {
        private static int frame=int.MinValue;
        private static bool left,prevLeft,failed;

        // Seated or on a turret; on foot only while a WeaponCore hand weapon is out.
        private static bool Active => WeaponCoreBridge.ApiReady && Main.VrActive && !Main.MenuOpen &&
            (InputRouter.Mode==InputMode.Piloting || InputRouter.Mode==InputMode.Turret ||
             (InputRouter.Mode==InputMode.Walking || InputRouter.Mode==InputMode.Jetpack) && WeaponCoreInternals.Available && WeaponCoreInternals.HandheldActive);

        // State advances once per tick whoever asks first; only WeaponCore is shown the result.
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

    [HarmonyPatch]
    internal static class VirtualMousePressedPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(AccessTools.TypeByName("VRage.Input.MyVRageInput"),"IsMousePressed",new[] { typeof(MyMouseButtonsEnum) });
        private static void Postfix(MyMouseButtonsEnum button,ref bool __result)
        {
            if(!__result) __result=VirtualMouse.Down(button);
        }
    }

    [HarmonyPatch]
    internal static class VirtualMouseEdgePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var input=AccessTools.TypeByName("VRage.Input.MyVRageInput");
            foreach(string name in new[] { "IsAnyMousePressed","IsNewLeftMousePressed","IsNewLeftMouseReleased" })
            {
                var method=AccessTools.Method(input,name,System.Type.EmptyTypes);
                if(method!=null) yield return method;
            }
        }
        private static void Postfix(MethodBase __originalMethod,ref bool __result)
        {
            if(__result) return;
            switch(__originalMethod.Name)
            {
                case "IsAnyMousePressed": __result=VirtualMouse.Any(); break;
                case "IsNewLeftMousePressed": __result=VirtualMouse.NewLeft(); break;
                case "IsNewLeftMouseReleased": __result=VirtualMouse.ReleasedLeft(); break;
            }
        }
    }
}
