using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class CreativePlacement
    {
        private static readonly List<MyEntity> entities=new List<MyEntity>();
        private static readonly List<Vector3I> cells=new List<Vector3I>();
        private static readonly Vector3I[] directions={Vector3I.Right,Vector3I.Left,Vector3I.Up,Vector3I.Down,Vector3I.Forward,Vector3I.Backward};
        internal static bool Active => PlacementControls.Observer && PlacementControls.Creative && !PlacementControls.ClipboardActive &&
            MyCubeBuilder.Static?.IsActivated==true && MyCubeBuilder.Static.IsBuildToolActive();

        internal static MyCubeGrid FindGrid()
        {
            var definition=MyCubeBuilder.Static.CurrentBlockDefinition;
            if(definition==null) return null;
            var start=MyBlockBuilderBase.IntersectionStart;
            var end=start+MyBlockBuilderBase.IntersectionDirection*MyBlockBuilderBase.IntersectionDistance;
            var box=new BoundingBoxD(Vector3D.Min(start,end),Vector3D.Max(start,end));
            box.Inflate(definition.CubeSize==VRage.Game.MyCubeSize.Large ? 2.5:.5);
            MyCubeGrid closest=null;
            double nearest=double.MaxValue;
            entities.Clear();
            MyGamePruningStructure.GetTopMostEntitiesInBox(ref box,entities);
            foreach(var entity in entities)
            {
                var grid=entity as MyCubeGrid;
                if(grid==null || grid.MarkedForClose || !grid.Editable || grid.GridSizeEnum!=definition.CubeSize) continue;
                if(TryCell(grid,start,end,out _,out _,out double distance) &&
                    (distance<nearest || distance==nearest && grid.EntityId<closest.EntityId))
                { nearest=distance; closest=grid; }
            }
            entities.Clear();
            return closest;
        }

        internal static bool TryCell(MyCubeGrid grid,Vector3D start,Vector3D end,
            out Vector3I position,out Vector3I direction,out double distance)
        {
            position=direction=Vector3I.Zero; distance=double.MaxValue;
            if(grid.GridSize<=0 || !start.IsValid() || !end.IsValid() || Vector3D.DistanceSquared(start,end)<1e-12) return false;
            grid.RayCastCells(start,end,cells);
            var localStart=Vector3D.Transform(start,grid.PositionComp.WorldMatrixNormalizedInv)/grid.GridSize;
            var localEnd=Vector3D.Transform(end,grid.PositionComp.WorldMatrixNormalizedInv)/grid.GridSize;
            var line=new LineD(localStart,localEnd);
            foreach(var cell in cells)
            {
                if(grid.CubeExists(cell)) return false;
                if(!Adjacent(grid,cell,out var face)) continue;
                var box=new BoundingBoxD((Vector3D)cell-Vector3D.Half,(Vector3D)cell+Vector3D.Half);
                if(!box.Intersects(ref line,out double entry)) continue;
                position=cell; direction=face; distance=entry*grid.GridSize;
                return true;
            }
            return false;
        }

        private static bool Adjacent(MyCubeGrid grid,Vector3I cell,out Vector3I direction)
        {
            foreach(var face in directions)
                if(grid.CubeExists(cell-face)) { direction=face; return true; }
            direction=Vector3I.Zero;
            return false;
        }
    }
}
