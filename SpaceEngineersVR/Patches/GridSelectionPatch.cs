using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.SessionComponents.Clipboard;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using System.Collections.Generic;
using System.Reflection;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class GridSelectionPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach(string name in new[] {"Copy","Cut","Delete","CreateBlueprint"})
                yield return AccessTools.Method(typeof(MyClipboardComponent),name,new[] {typeof(bool),typeof(bool)});
        }
        private static bool Prefix(MethodBase __originalMethod,bool __0,bool __1,ref bool __result)
        {
            if(!Main.VrActive || GridSelection.Executing || __originalMethod.Name=="CreateBlueprint" && PlacementControls.ClipboardActive) return true;
            __result=GridSelection.Arm(__originalMethod.Name,__0,__1);
            return false;
        }
    }

    [HarmonyPatch(typeof(MyCubeGrid),nameof(MyCubeGrid.GetTargetEntity))]
    internal static class SelectedEntityPatch
    {
        private static bool Prefix(ref VRage.Game.Entity.MyEntity __result)
        {
            if(!GridSelection.Executing) return true;
            __result=GridSelection.Confirmed;
            return false;
        }
    }

    [HarmonyPatch(typeof(MyCubeGrid),nameof(MyCubeGrid.GetTargetGrid))]
    internal static class SelectedGridPatch
    {
        private static bool Prefix(ref MyCubeGrid __result)
        {
            if(!GridSelection.Executing) return true;
            __result=GridSelection.Confirmed as MyCubeGrid;
            return false;
        }
    }
}
