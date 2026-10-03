using System;
using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyBlockBuilderBase),nameof(MyBlockBuilderBase.FindClosestPlacementObject))]
    internal static class CreativeGridPatch
    {
        private static void Postfix(MyBlockBuilderBase __instance,ref MyCubeGrid closestGrid,MyVoxelBase closestVoxelMap,
            MyPhysics.HitInfo? ___m_hitInfo,ref bool __result)
        {
            if(!(__instance is MyCubeBuilder) || closestGrid!=null || closestVoxelMap!=null || ___m_hitInfo.HasValue || !CreativePlacement.Active) return;
            closestGrid=CreativePlacement.FindGrid();
            __result=closestGrid!=null;
        }
    }

    [HarmonyPatch(typeof(MyCubeBuilder),nameof(MyCubeBuilder.GetAddAndRemovePositions))]
    internal static class CreativeCellPatch
    {
        private static bool Prefix(float gridSize,bool placingSmallGridOnLargeStatic,ref Vector3I addPos,ref Vector3? addPosSmallOnLarge,
            ref Vector3I addDir,ref Vector3I removePos,ref MySlimBlock removeBlock,ref ushort? compoundBlockId,
            HashSet<Tuple<MySlimBlock,ushort?>> removeBlocksInMultiBlock,MyCubeGrid ___m_currentGrid,MyPhysics.HitInfo? ___m_hitInfo,
            bool ___canBuild,ref bool __result)
        {
            if(___m_currentGrid==null || ___m_hitInfo.HasValue) return true;
            // An empty-cell ray has no physics hit or block to remove.
            addPos=addDir=removePos=Vector3I.Zero; addPosSmallOnLarge=null; removeBlock=null; compoundBlockId=null;
            removeBlocksInMultiBlock?.Clear();
            __result=false;
            if(CreativePlacement.Active && ___canBuild && !placingSmallGridOnLargeStatic &&
                ___m_currentGrid.Editable && !___m_currentGrid.MarkedForClose && Math.Abs(___m_currentGrid.GridSize-gridSize)<.001)
            {
                var start=MyBlockBuilderBase.IntersectionStart;
                __result=CreativePlacement.TryCell(___m_currentGrid,start,start+MyBlockBuilderBase.IntersectionDirection*MyBlockBuilderBase.IntersectionDistance,
                    out addPos,out addDir,out _);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(MyCubeBuilder),"StartRemoving",new Type[0])]
    internal static class CreativeRemoveStrokePatch
    {
        private static bool Prefix(MyCubeBuilder __instance) => !CreativePlacement.Active || __instance.HitInfo.HasValue;
    }

    [HarmonyPatch(typeof(MyCubeBuilderGizmo),nameof(MyCubeBuilderGizmo.EnableGizmoSpaces))]
    internal static class CreativeSymmetryPatch
    {
        private static void Postfix(MyCubeBuilderGizmo __instance,MyCubeGrid cubeGrid)
        {
            if(cubeGrid==null || !CreativePlacement.Active || MyCubeBuilder.Static.HitInfo.HasValue) return;
            ClearRemoval(__instance);
        }

        internal static void ClearRemoval(MyCubeBuilderGizmo gizmo)
        {
            // Native symmetry repopulates removal targets from cell coordinates.
            foreach(var space in gizmo.Spaces)
            {
                space.m_removeBlock=null;
                space.m_removeBlocksInMultiBlock.Clear();
                space.m_blockIdInCompound=null;
            }
        }
    }
}
