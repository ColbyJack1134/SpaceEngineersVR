using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using SpaceEngineersVR.Player;
using VRage.Input;
using VRage.Utils;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(Sandbox.Game.Screens.Helpers.RadialMenuActions.MyActionToggleDampeners),"ExecuteAction")]
    internal static class DampenerTogglePatch
    {
        private static void Prefix()
        {
            if(!Plugin.Main.VrActive) return;
            DampenerTargeting.Cancel(); GridSelection.CancelDampeners();
        }
    }
    [HarmonyPatch(typeof(MyControllerHelper), nameof(MyControllerHelper.IsControl),
        new[] { typeof(MyStringId), typeof(MyStringId), typeof(MyControlStateType), typeof(bool), typeof(bool) })]
    internal static class NativeActionPatch
    {
        [HarmonyPostfix]
        private static void Postfix(MyStringId context, MyStringId controlId, MyControlStateType type, ref bool __result)
        {
            if (context != MyControllerHelper.CX_GUI) __result |= NativeActions.Read(controlId, type);
            if(Plugin.Main.VrActive && InputRouter.Gameplay && type==MyControlStateType.NEW_PRESSED && __result && controlId==Sandbox.Game.MyControlsSpace.DAMPING_RELATIVE)
            { DampenerTargeting.Activate(); __result=false; }
            if(Plugin.Main.VrActive && __result && controlId==Sandbox.Game.MyControlsSpace.DAMPING)
            { DampenerTargeting.Cancel(); GridSelection.CancelDampeners(); }
        }
    }

    [HarmonyPatch]
    internal static class NativeKeyboardActionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var input = AccessTools.TypeByName("VRage.Input.MyVRageInput");
            foreach (string name in new[] { "IsNewGameControlPressed", "IsGameControlPressed", "IsNewGameControlReleased" })
                yield return AccessTools.Method(input, name, new[] { typeof(MyStringId) });
        }
        [HarmonyPostfix]
        private static void Postfix(MyStringId __0, MethodBase __originalMethod, ref bool __result)
        {
            var type = __originalMethod.Name == "IsGameControlPressed" ? MyControlStateType.PRESSED :
                __originalMethod.Name == "IsNewGameControlReleased" ? MyControlStateType.NEW_RELEASED : MyControlStateType.NEW_PRESSED;
            __result |= NativeActions.Read(__0, type);
        }
    }
}
