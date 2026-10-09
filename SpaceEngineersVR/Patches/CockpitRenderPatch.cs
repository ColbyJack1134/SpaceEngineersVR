using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRage.Utils;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents.Renders;
using Sandbox.Game.World;
using SpaceEngineersVR.Plugin;
using VRageRender;
using VRageRender.Messages;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class CockpitMaterialPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"ProcessMessageInternal");
        private static void Prefix(MyRenderMessageBase __0) => CockpitMaterials.Register(__0);
    }
    [HarmonyPatch(typeof(MyCockpit),nameof(MyCockpit.UpdateCockpitModel))]
    internal static class RemoteCockpitModelPatch
    {
        private static bool Prefix(MyCockpit __instance)
        {
            if(!Main.VrActive || ThirdPersonView.Active || __instance.Pilot==null || __instance.Pilot!=MySession.Static?.LocalCharacter ||
                __instance.Pilot.IsDead || !RemoteView.UsesSeat(__instance) ||
                !(__instance.IsInFirstPersonView || __instance.ForceFirstPersonCamera) || __instance.BlockDefinition.InteriorModel==null ||
                !(__instance.Render is MyRenderComponentCockpit render) || render.RenderObjectIDs.Length<2 ||
                render.ExteriorRenderId==uint.MaxValue || render.InteriorRenderId==uint.MaxValue) return true;
            MyRenderProxy.UpdateRenderObjectVisibility(render.ExteriorRenderId,false,false);
            MyRenderProxy.UpdateRenderObjectVisibility(render.InteriorRenderId,render.Visible,false);
            return false;
        }
    }
    [HarmonyPatch]
    internal static class CockpitRenderPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Scene.Components.MyRenderableComponent"),"RebuildRenderProxies");
        private static void Postfix(object __instance) => CockpitRender.Verify(__instance);
    }
    [HarmonyPatch]
    internal static class CockpitRuntimeSectionsPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyMeshes"),"CreateRuntimeMesh");
        private static void Postfix(MyStringId nameKey,object __result)
        {
            if (nameKey.ToString().StartsWith("SEVR_Glove_",System.StringComparison.Ordinal) || nameKey.ToString().StartsWith("SEVR_Cockpit_",System.StringComparison.Ordinal) || nameKey.ToString().StartsWith("SEVR_Grab_",System.StringComparison.Ordinal)) CockpitRender.InitializeRuntimeSections(__result);
        }
    }
}
