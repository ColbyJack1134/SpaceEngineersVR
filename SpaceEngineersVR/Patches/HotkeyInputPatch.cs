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
    [HarmonyPatch]
    internal static class HotkeyMousePatch
    {
        private static readonly string[] names={"IsRightMousePressed","IsNewRightMousePressed","IsNewRightMouseReleased","IsMiddleMousePressed","IsNewMiddleMousePressed","IsNewMiddleMouseReleased"};
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var input=AccessTools.TypeByName("VRage.Input.MyVRageInput");
            foreach(string name in names) yield return AccessTools.Method(input,name,new System.Type[0]);
            foreach(string name in new[] {"IsMousePressed","IsNewMousePressed"}) yield return AccessTools.Method(input,name,new[] {typeof(MyMouseButtonsEnum)});
            yield return AccessTools.Method(input,"DeltaMouseScrollWheelValue",new System.Type[0]);
        }
        private static void Postfix(MethodBase __originalMethod,object[] __args,ref object __result)
        {
            string name=__originalMethod.Name;
            if(name=="DeltaMouseScrollWheelValue") { if((int)__result==0) __result=HotkeyInput.WheelDelta; return; }
            if((bool)__result) return;
            var button=__args.Length>0 ? (MyMouseButtonsEnum)__args[0] : name.Contains("Right") ? MyMouseButtonsEnum.Right:MyMouseButtonsEnum.Middle;
            __result=name.Contains("Released") ? HotkeyInput.MouseNewReleased(button) : name.Contains("New") ? HotkeyInput.MouseNewPressed(button) : HotkeyInput.MouseDown(button);
        }
    }
}
