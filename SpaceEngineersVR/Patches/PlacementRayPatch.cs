using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyDefaultPlacementProvider))]
    internal static class PlacementRayPatch
    {
        [HarmonyPostfix]
        [HarmonyPatch("get_RayStart")]
        private static void Start(ref Vector3D __result)
        {
            if (PlacementControls.TryBuilderPose(out MatrixD pose))
                __result = pose.Translation;
        }
        [HarmonyPostfix]
        [HarmonyPatch("get_RayDirection")]
        private static void Direction(ref Vector3D __result)
        {
            if (PlacementControls.TryBuilderPose(out MatrixD pose))
                __result = Vector3D.Normalize(pose.Forward);
        }
    }

    [HarmonyPatch]
    internal static class ObserverBuildDistancePatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.PropertyGetter(typeof(MyBlockBuilderBase),nameof(MyBlockBuilderBase.IntersectionDistance));
            yield return AccessTools.PropertyGetter(typeof(MyDefaultPlacementProvider),nameof(MyDefaultPlacementProvider.IntersectionDistance));
        }
        private static void Postfix(ref float __result) => __result=PlacementControls.BuildDistance(__result);
    }

    [HarmonyPatch(typeof(MyCubeBuilderGizmo),nameof(MyCubeBuilderGizmo.DefaultGizmoCloseEnough))]
    internal static class ObserverBuildReachPatch
    {
        private static bool Prefix(MatrixD invGridWorldMatrix,BoundingBoxD gizmoBox,float gridSize,float intersectionDistance,ref bool __result)
        {
            if(!PlacementControls.Observer || !PlacementControls.TryBuilderPose(out var pose)) return true;
            var line=new LineD(Vector3D.Transform(pose.Translation,invGridWorldMatrix),
                Vector3D.Transform(pose.Translation+pose.Forward*intersectionDistance,invGridWorldMatrix));
            gizmoBox.Inflate(.025f*gridSize);
            __result=gizmoBox.Intersects(ref line,out _) && PlacementControls.WithinReach(gizmoBox,invGridWorldMatrix,
                MySession.Static.LocalCharacter.GetHeadMatrix(true).Translation,PlacementControls.SurvivalRange(gridSize));
            return false;
        }
    }

    [HarmonyPatch(typeof(MyCubeBuilder),"Change")]
    internal static class PaintReachPatch
    {
        private static bool Prefix(ref int expand,MyCubeBuilderGizmo ___m_gizmo)
        {
            if(NativeActions.Read(Sandbox.Game.MyControlsSpace.CUBE_COLOR_CHANGE,VRage.Input.MyControlStateType.PRESSED)) expand=0;
            if(!PlacementControls.Observer || MySession.Static?.SurvivalMode!=true) return true;
            var block=___m_gizmo.SpaceDefault.m_removeBlock;
            if(block==null) return false;
            var grid=block.CubeGrid;
            var box=new BoundingBoxD(((Vector3D)block.Min-Vector3D.Half)*grid.GridSize,((Vector3D)block.Max+Vector3D.Half)*grid.GridSize);
            return PlacementControls.WithinReach(box,MatrixD.Invert(grid.WorldMatrix),MySession.Static.LocalCharacter.GetHeadMatrix(true).Translation,
                PlacementControls.SurvivalRange(grid.GridSize));
        }
    }

    [HarmonyPatch(typeof(MyCubeBuilder),nameof(MyCubeBuilder.ColorPickerOk))]
    internal static class PaletteSelectionPatch
    {
        private static bool Prefix(MyCubeBuilder __instance) => !Plugin.Main.VrActive || !__instance.IsBuildToolActive();
    }

    [HarmonyPatch]
    internal static class ClipboardPosePatch
    {
        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach(var type in new[] {typeof(MyGridClipboard),AccessTools.TypeByName("Sandbox.Game.Entities.MyVoxelClipboard"),AccessTools.TypeByName("Sandbox.Game.Entities.MyFloatingObjectClipboard")})
                yield return AccessTools.Method(type,"GetPasteMatrix");
        }
        [HarmonyPostfix]
        private static void Postfix(ref MatrixD __result)
        {
            if (PlacementControls.ClipboardActive && PlacementControls.TryPose(out MatrixD pose)) __result = pose;
        }
    }
}
