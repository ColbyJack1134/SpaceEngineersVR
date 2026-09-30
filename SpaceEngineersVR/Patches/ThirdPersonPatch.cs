using System.Reflection;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Entities;
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
            if(!Main.VrActive || !(Sandbox.Game.World.MySession.Static?.ControlledEntity is MyCockpit)) return true;
            ThirdPersonView.Toggle(); return false;
        }
    }
    [HarmonyPatch]
    internal static class ThirdPersonCameraPatch
    {
        private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(MyCockpit)).Single(m=>m.Name.EndsWith(".ControlCamera"));
        private static bool Prefix(MyCockpit __instance,MyCamera currentCamera)
        {
            if(!Main.VrActive || !ThirdPersonView.Owns(__instance)) return true;
            ThirdPersonView.Publish();
            var frame=ThirdPersonView.Current;
            currentCamera.SetViewMatrix(VrMath.EyeView(MatrixD.Invert(frame.Anchor),Player.Player.Headset.pose.deviceToAbsolute.matrix,
                frame.OriginInverse,Matrix.Identity,frame.UnitsPerMeter),smooth:false);
            __instance.Pilot?.EnableHead(true);
            return false;
        }
    }
    [HarmonyPatch(typeof(MyCockpit),nameof(MyCockpit.ForceFirstPersonCamera),MethodType.Getter)]
    internal static class ThirdPersonCollisionPatch
    {
        private static void Postfix(MyCockpit __instance,ref bool __result)
        {
            if(Main.VrActive && ThirdPersonView.Owns(__instance)) __result=false;
        }
    }
}
