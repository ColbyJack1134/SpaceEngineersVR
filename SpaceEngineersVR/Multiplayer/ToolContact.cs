using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Engine.Physics;
using Sandbox.Game.WorldEnvironment;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.EntityComponents;
using Sandbox.Game.Weapons;
using Sandbox.Game.Weapons.Guns;
using SpaceEngineersVR.Player;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class ToolContact
    {
        private sealed class State { internal bool Near; }
        private static readonly ConditionalWeakTable<MyEngineerToolBase,State> states=new ConditionalWeakTable<MyEngineerToolBase,State>();
        private static readonly List<MyEntity> nearby=new List<MyEntity>();
        [ThreadStatic] private static List<Vector3I> cells;
        [ThreadStatic] private static List<MyEntity> rayGrids;
        private static readonly HashSet<MySlimBlock> blocks=new HashSet<MySlimBlock>();
        internal static readonly FieldInfo Sensor=AccessTools.Field(typeof(MyEngineerToolBase),"m_raycastComponent");
        private static readonly FieldInfo hitBlock=AccessTools.Field(typeof(MyCasterComponent),"m_hitBlock"),hitGrid=AccessTools.Field(typeof(MyCasterComponent),"m_hitCubeGrid"),
            hitPosition=AccessTools.Field(typeof(MyCasterComponent),"m_hitPosition"),distance=AccessTools.Field(typeof(MyCasterComponent),"m_distanceToHitSq");
        private static readonly FieldInfo[] otherHits={AccessTools.Field(typeof(MyCasterComponent),"m_hitCharacter"),AccessTools.Field(typeof(MyCasterComponent),"m_hitDestroaybleObj"),
            AccessTools.Field(typeof(MyCasterComponent),"m_hitFloatingObject"),AccessTools.Field(typeof(MyCasterComponent),"m_hitEnvironmentSector")};
        private static readonly FieldInfo rayLength=AccessTools.Field(typeof(MyDrillSensorRayCast),"m_rayLength"),
            center=AccessTools.Field(typeof(MyDrillSensorBase),"m_center"),front=AccessTools.Field(typeof(MyDrillSensorBase),"m_frontPoint"),
            environmentItem=AccessTools.Field(typeof(MyCasterComponent),"m_environmentItem");
        [System.ThreadStatic] private static List<MyPhysics.HitInfo> physicsHits;
        private static void PhysicsHits(LineD line)
        {
            if(physicsHits==null) physicsHits=new List<MyPhysics.HitInfo>();
            physicsHits.Clear(); MyPhysics.CastRay(line.From,line.To,physicsHits,24);
        }
        private static bool PhysicsHit(MyEntity owner,MyEntity tool,MyPhysics.HitInfo hit,out MyDrillSensorBase.DetectionInfo result)
        {
            result=default(MyDrillSensorBase.DetectionInfo);
            var raw=hit.HkHitInfo.GetHitEntity(); var entity=raw?.GetTopMostParent() as MyEntity;
            if(entity==null || ReferenceEquals(entity,owner) || ReferenceEquals(entity,tool)) return false;
            var point=hit.Position;
            if(entity is MyCubeGrid) point-=hit.HkHitInfo.Normal*.005f;
            if(raw is MyEnvironmentSector sector)
            {
                int item=sector.GetItemFromShapeKey(hit.HkHitInfo.GetShapeKey(0));
                if(item<0 || sector.DataView?.Items==null || item>=sector.DataView.Items.Count || sector.DataView.Items[item].ModelIndex<0) return false;
                result=new MyDrillSensorBase.DetectionInfo(sector,point,item);
            }
            else result=new MyDrillSensorBase.DetectionInfo(entity,point);
            return true;
        }
        internal static bool PhysicsCast(MyEntity owner,MyEntity tool,LineD line,out MyDrillSensorBase.DetectionInfo result)
        {
            PhysicsHits(line); result=default(MyDrillSensorBase.DetectionInfo); double best=double.MaxValue;
            foreach(var hit in physicsHits)
            {
                if(!PhysicsHit(owner,tool,hit,out var candidate)) continue;
                double distance=Vector3D.DistanceSquared(line.From,hit.Position);
                if(distance>=best) continue;
                result=candidate; best=distance;
            }
            return result.Entity!=null;
        }
        internal static float Reach(MyCasterComponent sensor) => (float)rayLength.GetValue(sensor.Caster);
        internal static bool Cast(MyCharacter owner,MyEntity tool,MatrixD ray,double reach,out MySlimBlock block,out MyEntity entity,out Vector3D point)
        {
            var line=new LineD(ray.Translation,ray.Translation+ray.Forward*reach);
            var hit=MyEntities.GetIntersectionWithLine(ref line,owner,tool,ignoreChildren:false,ignoreFloatingObjects:false);
            block=null; entity=null; point=line.To;
            if(!hit.HasValue || hit.Value.Entity==null) return false;
            block=Block(hit.Value.UserObject,hit.Value.Entity);
            entity=hit.Value.Entity.GetTopMostParent() as MyEntity; point=hit.Value.IntersectionPointInWorldSpace;
            return true;
        }
        internal static bool Near(MyEngineerToolBase tool) => states.TryGetValue(tool,out var state) && state.Near;
        internal static void Apply(MyEngineerToolBase tool,MyCasterComponent sensor,MatrixD working,WeaponProfile profile)
        {
            var state=states.GetValue(tool,_=>new State()); state.Near=false;
            var owner=HeldItemPose.Owner(tool);
            var from=working.Translation;
            HeldItemPose.TryGet(owner,out var model,out _);
            var volume=ToolVolume.For(profile,model);
            // A blade or bit may penetrate its contact surface; occlusion starts at the grip.
            var grip=Vector3D.Transform(profile.Primary,model);
            if(!HeldItemPose.Clear(owner,owner.GetHeadMatrix(true,true).Translation,grip))
            { Set(sensor,null,from); state.Near=true; return; }
            if(FindNear(owner,tool,volume,out var selected,out var selectedHit))
            { Set(sensor,selected,selectedHit); state.Near=true; }
            else if(tool is MyAngleGrinder && FindTreeNear(owner,tool,volume,grip,out var tree))
            { state.Near=true; SetTarget(sensor,null,tree.Entity,tree.DetectionPoint,tree.ItemId); }
            else if(!HeldItemPose.Supported(owner) && !HeldItemPose.Clear(owner,grip,from)) { Set(sensor,null,from); state.Near=true; }
            else
            {
                var ray=HeldItemPose.ToolAim(owner,model,profile);
                double reach=HeldItemPose.ToolReach(owner,model,profile,ray,Reach(sensor));
                center.SetValue(sensor.Caster,ray.Translation); front.SetValue(sensor.Caster,ray.Translation+ray.Forward*reach);
                sensor.SetPointOfReference(ray.Translation);
                if(!HeldItemPose.Supported(owner) && !HeldItemPose.Clear(owner,grip,ray.Translation)) { Set(sensor,null,ray.Translation); return; }
                CastTarget(owner,tool,ray,reach,out var block,out var entity,out var point,out var item,HeldItemPose.Supported(owner));
                if(HeldItemPose.Supported(owner) && entity!=null && !Accessible(owner,tool,grip,point,block,entity,item)) { Set(sensor,null,point); return; }
                SetTarget(sensor,block,entity,point,item);
            }
        }
        internal static void CastTarget(MyCharacter owner,MyEntity tool,MatrixD ray,double reach,
            out MySlimBlock block,out MyEntity entity,out Vector3D point,out int item,bool supported=false)
        {
            bool mesh=Cast(owner,tool,ray,reach,out block,out entity,out point);
            item=0;
            var line=new LineD(ray.Translation,ray.Translation+ray.Forward*reach);
            if(PhysicsCast(owner,tool,line,out var hit) && (supported || !(hit.Entity is MyCubeGrid)) &&
                (!mesh || !(hit.Entity is MyCubeGrid) && ReferenceEquals(entity,hit.Entity) || Vector3D.DistanceSquared(ray.Translation,hit.DetectionPoint)<=Vector3D.DistanceSquared(ray.Translation,point)+.0001))
            { block=hit.Entity is MyCubeGrid grid ? ResolveBlock(grid,hit.DetectionPoint):null; entity=hit.Entity; point=hit.DetectionPoint; item=hit.ItemId; }
            else if(entity is MyEnvironmentSector) entity=null;
            if(supported)
            {
                var limit=entity==null ? reach:Math.Sqrt(Vector3D.DistanceSquared(ray.Translation,point));
                if(ConstructionRay(ray,limit,out var construction,out var entry))
                { block=construction; entity=block.CubeGrid; point=entry; item=0; }
            }
        }
        internal static MySlimBlock ResolveBlock(MyCubeGrid grid,Vector3D point)
        {
            var local=Vector3D.Transform(point,grid.PositionComp.WorldMatrixNormalizedInv)/grid.GridSize;
            grid.FixTargetCube(out var cell,local); return grid.GetCubeBlock(cell);
        }
        internal static BoundingBox BlockBounds(MySlimBlock block) => new BoundingBox(
            (block.Min-new Vector3(.5f))*block.CubeGrid.GridSize,(block.Max+new Vector3(.5f))*block.CubeGrid.GridSize);
        internal static bool ConstructionRay(MatrixD ray,double reach,out MySlimBlock selected,out Vector3D point,MySlimBlock ignore=null)
        {
            selected=null; point=ray.Translation+ray.Forward*reach;
            var sphere=new BoundingSphereD((ray.Translation+point)*.5,reach*.5+.01);
            if(rayGrids==null) rayGrids=new List<MyEntity>();
            if(cells==null) cells=new List<Vector3I>();
            rayGrids.Clear(); MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere,rayGrids);
            double best=reach;
            foreach(var entity in rayGrids)
            {
                if(!(entity is MyCubeGrid grid) || grid.Closed || grid.Physics==null || !grid.Physics.Enabled) continue;
                cells.Clear(); grid.RayCastCells(ray.Translation,ray.Translation+ray.Forward*reach,cells);
                var inv=grid.PositionComp.WorldMatrixNormalizedInv;
                var start=Vector3D.Transform(ray.Translation,inv); var direction=Vector3D.TransformNormal(ray.Forward,inv);
                var localRay=new Ray((Vector3)start,(Vector3)direction);
                foreach(var cell in cells)
                {
                    var block=grid.GetCubeBlock(cell);
                    if(block==null || ReferenceEquals(block,ignore) || block.CalculateCurrentModelID()<0) continue;
                    var distance=localRay.Intersects(BlockBounds(block));
                    if(!distance.HasValue || !Better(block,distance.Value,selected,best)) continue;
                    best=distance.Value; selected=block; point=ray.Translation+ray.Forward*best;
                }
            }
            return selected!=null;
        }
        internal static bool Accessible(MyCharacter owner,MyEntity tool,Vector3D grip,Vector3D point,MySlimBlock selected,MyEntity target=null,int item=0)
        {
            var approach=point-grip;
            if(approach.LengthSquared()<=.005*.005) return true;
            var ray=MatrixD.CreateWorld(grip,Vector3D.Normalize(approach),Vector3D.CalculatePerpendicularVector(approach));
            double reach=approach.Length(); bool targetCollider=false;
            // Check every collider before contact; a selected construction box can hide another wall.
            PhysicsHits(new LineD(grip,point+ray.Forward*(selected==null ? .005:-.005)));
            foreach(var hit in physicsHits)
            {
                if(!PhysicsHit(owner,tool,hit,out var physical)) continue;
                if(physical.Entity is MyCubeGrid grid && selected!=null && ReferenceEquals(ResolveBlock(grid,physical.DetectionPoint),selected)) continue;
                bool same=ReferenceEquals(physical.Entity,target) && (target is MyEnvironmentSector ? physical.ItemId==item:
                    Vector3D.DistanceSquared(physical.DetectionPoint,point)<=.02*.02);
                if(!same) return false;
                targetCollider=true;
            }
            // Construction eligibility does not establish clearance through physical walls.
            if(Cast(owner,tool,ray,reach-.005,out var meshBlock,out var meshEntity,out var meshPoint) && meshEntity!=null &&
                !(selected!=null && ReferenceEquals(meshBlock,selected)) &&
                !(targetCollider && ReferenceEquals(meshEntity,target) && (target is MyEnvironmentSector || Vector3D.DistanceSquared(meshPoint,point)<=.02*.02))) return false;
            return !ConstructionRay(ray,reach-.005,out _,out _,selected);
        }
        internal static bool FindTreeNear(MyCharacter owner,MyEntity tool,ToolVolume volume,Vector3D grip,out MyDrillSensorBase.DetectionInfo tree)
        {
            tree=default(MyDrillSensorBase.DetectionInfo); double best=double.MaxValue;
            for(int i=0;i<7;i++)
            {
                if(!PhysicsCast(owner,tool,volume.Probe(i),out var hit) || !(hit.Entity is MyEnvironmentSector) || !volume.Contains(hit.DetectionPoint)) continue;
                var approach=hit.DetectionPoint-grip;
                double score=Vector3D.DistanceSquared(hit.DetectionPoint,volume.Start);
                if(score>=best || approach.LengthSquared()<1e-10 ||
                    !HeldItemPose.Clear(owner,grip,hit.DetectionPoint-Vector3D.Normalize(approach)*.005)) continue;
                tree=hit; best=score;
            }
            return tree.Entity!=null;
        }
        internal static void SetTarget(MyCasterComponent sensor,MySlimBlock block,MyEntity entity,Vector3D point,int item)
        {
            Set(sensor,block,point);
            if(entity is MyEnvironmentSector)
            { otherHits[3].SetValue(sensor,entity); environmentItem.SetValue(sensor,item); }
            else if(entity!=null) foreach(var field in otherHits)
                if(field.FieldType.IsInstanceOfType(entity)) field.SetValue(sensor,entity);
        }
        internal static bool FindNear(MyCharacter owner,MyEntity tool,ToolVolume volume,out MySlimBlock selected,out Vector3D selectedHit)
        {
            var from=volume.Start; var to=volume.End; var radius=volume.Disc ? volume.Bounds.Radius:volume.Radius;
            var sphere=volume.Bounds;
            if(!HeldItemPose.TryGet(owner,out var model,out var profile)) { selected=null; selectedHit=from; return false; }
            var grip=Vector3D.Transform(profile.Primary,model);
            nearby.Clear(); MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere,nearby);
            selected=null; selectedHit=from; double best=double.MaxValue;
            foreach(var entity in nearby)
            {
                if(!(entity is MyCubeGrid grid) || grid.Closed || grid.Physics==null || !grid.Physics.Enabled) continue;
                blocks.Clear(); grid.GetBlocksInsideSphere(ref sphere,blocks,false);
                var inv=grid.PositionComp.WorldMatrixNormalizedInv;
                var a=Vector3D.Transform(from,inv); var b=Vector3D.Transform(to,inv);
                foreach(var block in blocks)
                {
                    var bounds=BlockBounds(block);
                    if(SegmentBox.DistanceSquared(a,b,bounds,out var contact)>radius*radius) continue;
                    var target=Vector3D.Transform(contact,grid.WorldMatrix);
                    if(block.CalculateCurrentModelID()>=0 && ConstructionOverlap(volume,grid,bounds) && Accessible(owner,tool,grip,target,block))
                    {
                        double score=Vector3D.DistanceSquared(target,from);
                        if(Better(block,score,selected,best)) { best=score; selected=block; selectedHit=target; }
                        continue;
                    }
                    var direction=target-from;
                    if(direction.LengthSquared()<.000001) direction=volume.Frame.Forward;
                    direction.Normalize();
                    var line=new LineD(from-volume.Frame.Forward*.035,target+direction*(radius+.01));
                    for(int probe=-1;probe<8;probe++)
                    {
                        if(probe==7) line=new LineD(grip,volume.Bounds.Center);
                        else if(probe>=0) line=volume.Probe(probe);
                        var hit=MyEntities.GetIntersectionWithLine(ref line,owner,tool,ignoreChildren:false,ignoreFloatingObjects:false);
                        Vector3D actual;
                        if(PhysicsCast(owner,tool,line,out var physical) && physical.Entity is MyCubeGrid physicalGrid &&
                            ResolveBlock(physicalGrid,physical.DetectionPoint)==block) actual=physical.DetectionPoint;
                        else if(hit.HasValue && Block(hit.Value.UserObject,hit.Value.Entity)==block) actual=hit.Value.IntersectionPointInWorldSpace;
                        else continue;
                        if(!volume.Contains(actual)) continue;
                        var score=Vector3D.DistanceSquared(actual,from);
                        if(!Better(block,score,selected,best)) continue;
                        if(!Accessible(owner,tool,grip,actual,block)) continue;
                        best=score; selected=block; selectedHit=actual;
                    }
                }
            }
            return selected!=null;
        }
        private static bool Better(MySlimBlock block,double score,MySlimBlock selected,double best)
        {
            if(score<best-1e-10) return true;
            if(score>best+1e-10) return false;
            if(selected==null) return true;
            if(block.CubeGrid.EntityId!=selected.CubeGrid.EntityId) return block.CubeGrid.EntityId<selected.CubeGrid.EntityId;
            var a=block.Position; var b=selected.Position;
            return a.X!=b.X ? a.X<b.X:a.Y!=b.Y ? a.Y<b.Y:a.Z<b.Z;
        }
        internal static bool ConstructionOverlap(ToolVolume volume,MyCubeGrid grid,BoundingBox bounds)
        {
            if(!volume.Disc) return true;
            var transform=grid.WorldMatrix*MatrixD.Invert(volume.Frame);
            var local=new BoundingBoxD(new Vector3D(double.MaxValue),new Vector3D(double.MinValue));
            foreach(var corner in bounds.GetCorners()) local.Include(Vector3D.Transform(corner,transform));
            if(local.Min.Y>volume.HalfHeight || local.Max.Y< -volume.HalfHeight) return false;
            var x=MathHelper.Clamp(0,local.Min.X,local.Max.X); var z=MathHelper.Clamp(0,local.Min.Z,local.Max.Z);
            return x*x+z*z<=volume.Radius*volume.Radius;
        }
        private static MySlimBlock Block(object geometry,VRage.ModAPI.IMyEntity entity)
        {
            if(geometry is MyCube cube) return cube.CubeBlock;
            while(entity!=null && !(entity is MyCubeBlock)) entity=entity.Parent;
            return (entity as MyCubeBlock)?.SlimBlock;
        }
        private static void Set(MyCasterComponent sensor,MySlimBlock block,Vector3D point)
        {
            hitBlock.SetValue(sensor,block); hitGrid.SetValue(sensor,block?.CubeGrid); hitPosition.SetValue(sensor,point);
            distance.SetValue(sensor,Vector3D.DistanceSquared(sensor.PointOfReference,point));
            foreach(var field in otherHits) field.SetValue(sensor,null);
        }
    }
    [HarmonyPatch(typeof(MyCasterComponent),nameof(MyCasterComponent.OnWorldPosChanged))]
    internal static class ToolSensorPatch
    {
        private static void Prefix(MyCasterComponent __instance,ref MatrixD newTransform)
        {
            if(__instance.Entity is MyEngineerToolBase tool && HeldItemPose.TryGet(HeldItemPose.Owner(tool),out var model,out var profile))
            { newTransform=HeldItemPose.ToolAim(HeldItemPose.Owner(tool),model,profile); __instance.SetPointOfReference(newTransform.Translation); }
        }
        private static void Postfix(MyCasterComponent __instance)
        {
            if(__instance.Entity is MyEngineerToolBase tool && HeldItemPose.TryGet(HeldItemPose.Owner(tool),out var model,out var profile))
                ToolContact.Apply(tool,__instance,HeldItemPose.Working(model,profile),profile);
        }
    }
    [HarmonyPatch(typeof(MyEngineerToolBase),nameof(MyEngineerToolBase.GetTargetBlock))]
    internal static class ToolTargetPatch
    {
        private static bool Prefix(MyEngineerToolBase __instance,ref MySlimBlock __result)
        {
            if(!HeldItemPose.TryGet(HeldItemPose.Owner(__instance),out _,out _)) return true;
            __result=((MyCasterComponent)ToolContact.Sensor.GetValue(__instance))?.HitBlock; return false;
        }
    }
    [HarmonyPatch(typeof(MyWelder),"CheckProjection")]
    internal static class ToolProjectionPatch
    {
        private static readonly FieldInfo target=AccessTools.Field(typeof(MyWelder),"m_targetProjectionGrid");
        private static bool Prefix(MyWelder __instance)
        {
            if(!(ToolContact.Near(__instance) || HeldItemPose.Supported(HeldItemPose.Owner(__instance))) || !HeldItemPose.TryGet(HeldItemPose.Owner(__instance),out _,out _)) return true;
            target.SetValue(__instance,null); return false;
        }
    }
    [HarmonyPatch(typeof(MyHandDrill),"WorldPositionChanged")]
    internal static class HeldDrillPosePatch
    {
        private static readonly FieldInfo drill=AccessTools.Field(typeof(MyHandDrill),"m_drillBase");
        private static bool Prefix(MyHandDrill __instance)
        {
            if(!HeldItemPose.TryGet(__instance.Owner,out var model,out var profile)) return true;
            var core=(MyDrillBase)drill.GetValue(__instance);
            if(core!=null) { core.UpdatePosition(model); DrillContact.Register(core,__instance); }
            return false;
        }
    }
    [HarmonyPatch]
    internal static class ToolFeedbackPatch
    {
        private static readonly FieldInfo grindEffect=AccessTools.Field(typeof(MyAngleGrinder),"m_effectId");
        private static readonly PropertyInfo spark=AccessTools.Property(typeof(MyWelder),"ShowContactSpark");
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(MyWelder),"Weld");
            yield return AccessTools.Method(typeof(MyAngleGrinder),"Grind");
            yield return AccessTools.Method(typeof(MyHandDrill),"DoDrillAction");
        }
        private static void Postfix(object __instance)
        {
            var owner=HeldItemPose.Owner(__instance);
            if(!HeldItemPose.TryGet(owner,out _,out var profile)) return;
            bool worked=__instance is MyWelder welder ? (bool)spark.GetValue(welder,null):
                __instance is MyHandDrill drill ? drill.IsDrillingAnObject:
                __instance is MyAngleGrinder grinder && grindEffect.GetValue(grinder)!=null && grinder.GetTargetBlock()?.CubeGrid.Immune!=true;
            if(worked) HeldItemPose.Feedback?.Invoke(owner,profile.Kind);
        }
    }
}
