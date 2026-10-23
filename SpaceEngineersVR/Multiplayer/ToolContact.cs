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
        // Kept out of line because these native accessors are otherwise inlined here, which diagnostics cannot intercept.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static VRage.ModAPI.IMyEntity HitEntity(MyPhysics.HitInfo hit) => hit.HkHitInfo.GetHitEntity();
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int HitItem(MyEnvironmentSector sector,MyPhysics.HitInfo hit) => sector.GetItemFromShapeKey(hit.HkHitInfo.GetShapeKey(0));
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void EntitiesInSphere(ref BoundingSphereD sphere,List<MyEntity> result) => MyGamePruningStructure.GetAllEntitiesInSphere(ref sphere,result);
        private static bool PhysicsHit(MyEntity owner,MyEntity tool,MyPhysics.HitInfo hit,out MyDrillSensorBase.DetectionInfo result)
        {
            result=default(MyDrillSensorBase.DetectionInfo);
            var raw=HitEntity(hit); var entity=raw?.GetTopMostParent() as MyEntity;
            if(entity==null || ReferenceEquals(entity,owner) || ReferenceEquals(entity,tool)) return false;
            var point=hit.Position;
            if(entity is MyCubeGrid) point-=hit.HkHitInfo.Normal*.005f;
            if(raw is MyEnvironmentSector sector)
            {
                int item=HitItem(sector,hit);
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
            var ray=HeldItemPose.ToolAim(owner,model,profile);
            double reach=HeldItemPose.ToolReach(owner,model,profile,ray,Reach(sensor));
            // Native projection targeting rebuilds its own ray from the caster, so keep it current in every branch.
            center.SetValue(sensor.Caster,ray.Translation); front.SetValue(sensor.Caster,ray.Translation+ray.Forward*reach);
            sensor.SetPointOfReference(ray.Translation);
            if(!HeldItemPose.Clear(owner,owner.GetHeadMatrix(true,true).Translation,grip))
            { Set(sensor,null,from); state.Near=true; return; }
            if(FindNear(owner,tool,volume,out var selected,out var selectedHit))
            { Set(sensor,selected,selectedHit); state.Near=true; }
            else if(tool is MyAngleGrinder && FindTreeNear(owner,tool,volume,grip,out var tree))
            { state.Near=true; SetTarget(sensor,null,tree.Entity,tree.DetectionPoint,tree.ItemId); }
            else if(!HeldItemPose.Clear(owner,grip,from)) { Set(sensor,null,from); state.Near=true; }
            else if(ray.Translation!=from && !HeldItemPose.Clear(owner,grip,ray.Translation)) Set(sensor,null,ray.Translation);
            else
            {
                CastTarget(owner,tool,ray,reach,out var block,out var entity,out var point,out var item);
                SetTarget(sensor,block,entity,point,item);
            }
        }
        internal static void CastTarget(MyCharacter owner,MyEntity tool,MatrixD ray,double reach,
            out MySlimBlock block,out MyEntity entity,out Vector3D point,out int item)
        {
            bool mesh=Cast(owner,tool,ray,reach,out block,out entity,out point);
            item=0;
            var line=new LineD(ray.Translation,ray.Translation+ray.Forward*reach);
            if(PhysicsCast(owner,tool,line,out var hit) &&
                (!mesh || !(hit.Entity is MyCubeGrid) && ReferenceEquals(entity,hit.Entity) || Vector3D.DistanceSquared(ray.Translation,hit.DetectionPoint)<=Vector3D.DistanceSquared(ray.Translation,point)+.0001))
            { block=hit.Entity is MyCubeGrid grid ? ResolveBlock(grid,hit.DetectionPoint):null; entity=hit.Entity; point=hit.DetectionPoint; item=hit.ItemId; }
            else if(entity is MyEnvironmentSector) entity=null;
        }
        internal static float ProjectionMultiplier(MyCasterComponent sensor) =>
            (float)(Vector3D.Distance(sensor.Caster.Center,sensor.Caster.FrontPoint)/MyEngineerToolBase.DEFAULT_REACH_DISTANCE);
        internal static MySlimBlock ResolveBlock(MyCubeGrid grid,Vector3D point)
        {
            var local=Vector3D.Transform(point,grid.PositionComp.WorldMatrixNormalizedInv)/grid.GridSize;
            grid.FixTargetCube(out var cell,local); return grid.GetCubeBlock(cell);
        }
        internal static BoundingBox BlockBounds(MySlimBlock block) => new BoundingBox(
            (block.Min-new Vector3(.5f))*block.CubeGrid.GridSize,(block.Max+new Vector3(.5f))*block.CubeGrid.GridSize);
        internal static bool Accessible(MyCharacter owner,MyEntity tool,Vector3D grip,Vector3D point,MySlimBlock selected)
        {
            var approach=point-grip;
            if(approach.LengthSquared()<=.005*.005) return true;
            var ray=MatrixD.CreateWorld(grip,Vector3D.Normalize(approach),Vector3D.CalculatePerpendicularVector(approach));
            double reach=approach.Length();
            PhysicsHits(new LineD(grip,point-ray.Forward*.005));
            foreach(var hit in physicsHits)
                if(PhysicsHit(owner,tool,hit,out var physical) &&
                    !(physical.Entity is MyCubeGrid grid && ReferenceEquals(ResolveBlock(grid,physical.DetectionPoint),selected))) return false;
            return !(Cast(owner,tool,ray,reach-.005,out var meshBlock,out var meshEntity,out _) && meshEntity!=null && !ReferenceEquals(meshBlock,selected));
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
            nearby.Clear(); EntitiesInSphere(ref sphere,nearby);
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
        internal static MySlimBlock Block(object geometry,VRage.ModAPI.IMyEntity entity)
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
    [HarmonyPatch(typeof(MyWelder),nameof(MyWelder.FindProjectedBlock))]
    internal static class ToolProjectionReachPatch
    {
        // Native projection targeting measures the reach constant from the caster origin instead of its front point.
        private static void Prefix(MyCasterComponent rayCaster,ref float distanceMultiplier)
        {
            if(!(rayCaster?.Entity is MyEngineerToolBase tool) || !HeldItemPose.TryGet(HeldItemPose.Owner(tool),out _,out _)) return;
            float scaled=ToolContact.ProjectionMultiplier(rayCaster);
            if(scaled>0) distanceMultiplier=scaled;
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
