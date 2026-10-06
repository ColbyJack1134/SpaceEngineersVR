using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRage.Input;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class HotkeyInputPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var input=AccessTools.TypeByName("VRage.Input.MyVRageInput");
            foreach(string name in new[] { "IsKeyPress","WasKeyPress","IsNewKeyPressed","IsNewKeyReleased" })
                yield return AccessTools.Method(input,name,new[] { typeof(MyKeys) });
        }
        private static void Postfix(MyKeys key,MethodBase __originalMethod,ref bool __result)
        {
            if(__result) return;
            switch(__originalMethod.Name)
            {
                case "IsKeyPress": __result=HotkeyInput.Down(key); break;
                case "WasKeyPress": __result=HotkeyInput.Was(key); break;
                case "IsNewKeyPressed": __result=HotkeyInput.NewPressed(key); break;
                default: __result=HotkeyInput.NewReleased(key); break;
            }
        }
    }
}
