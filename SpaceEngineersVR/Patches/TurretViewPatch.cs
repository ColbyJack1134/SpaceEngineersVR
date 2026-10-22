using System.Linq;
using System.Reflection;
using HarmonyLib;
using VRage.Game.Utils;
using Sandbox.Game.Weapons;
using SpaceEngineersVR.Player;

namespace SpaceEngineersVR.Patches
{
    // Head-aimed turret views are rolled to the player's up, so the picture and its native HUD stay upright on a turret mounted at any roll.
    [HarmonyPatch]
    internal static class TurretViewPatch
    {
        private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(MyLargeTurretBase)).First(m=>m.Name.EndsWith(".ControlCamera"));
        private static void Postfix(MyLargeTurretBase __instance,MyCamera currentCamera)
        {
            if(!TurretHeadAim.Upright(__instance,currentCamera.ViewMatrix,out var view)) return;
            // A second SetViewMatrix would make this frame's position its own previous one.
            var previous=currentCamera.PreviousPosition;
            currentCamera.SetViewMatrix(view,currentCamera.SmoothMotion);
            currentCamera.PreviousPosition=previous;
        }
    }
}
