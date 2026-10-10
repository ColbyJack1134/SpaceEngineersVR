using System.Reflection;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using VRage.Game.Entity;
using System.Collections.Generic;
using Sandbox.Game.Gui;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Game.Utils;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyGuiScreenGamePlay),nameof(MyGuiScreenGamePlay.SwitchCamera))]
    internal static class ThirdPersonTogglePatch
    {
        private static bool Prefix()
        {
            if(!Main.VrActive || !(Sandbox.Game.World.MySession.Static?.ControlledEntity is MyCockpit) && !(Sandbox.Game.World.MySession.Static?.ControlledEntity is MyCharacter) && !RemoteView.Active) return true;
            ThirdPersonView.Toggle(); return false;
        }
    }
    [HarmonyPatch]
    internal static class ThirdPersonCameraPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(MyCockpit),typeof(MyCharacter) }
            .Select(t=>(MethodBase)AccessTools.GetDeclaredMethods(t).Single(m=>m.Name.EndsWith(".ControlCamera")));
        private static bool Prefix(MyEntity __instance,MyCamera currentCamera)
        {
            if(!Main.VrActive || !ThirdPersonView.Owns(__instance)) return true;
            ThirdPersonView.Publish();
            var frame=ThirdPersonView.Current;
            if(frame==null) return true;
            CameraRig.ApplyObserverCamera(currentCamera,frame);
            var character=__instance is MyCockpit cockpit ? cockpit.Pilot : __instance as MyCharacter;
            character?.EnableHead(true);
            return false;
        }
        // Cockpits re-enable the spring every update.
        private static void Postfix(MyCamera currentCamera)
        {
            if(!Main.VrActive) return;
            currentCamera.CameraSpring.Enabled=false;
            currentCamera.CameraShake.ShakeEnabled=false;
        }
    }
    [HarmonyPatch]
    internal static class ThirdPersonCollisionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] { typeof(MyCockpit),typeof(MyCharacter) }
            .Select(t=>(MethodBase)AccessTools.PropertyGetter(t,"ForceFirstPersonCamera"));
        private static void Postfix(MyEntity __instance,ref bool __result)
        {
            if(Main.VrActive && ThirdPersonView.Owns(__instance)) __result=false;
        }
    }
}
