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
        internal static float Reach(MyCasterComponent sensor) => (float)rayLength.GetValue(sensor.Caster);
        internal static bool Cast(MyCharacter owner,MyEntity tool,MatrixD ray,double reach,out MySlimBlock block,out MyEntity entity,out Vector3D point)
        {
            var line=new LineD(ray.Translation,ray.Translation+ray.Forward*reach);
            var hit=MyEntities.GetIntersectionWithLine(ref line,owner,tool,ignoreChildren:false,ignoreFloatingObjects:false);
            block=null; entity=null; point=line.To;
            if(!hit.HasValue) return false;
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
            else if(HeldItemPose.Supported(owner)) { Set(sensor,null,from); }
            else if(!HeldItemPose.Clear(owner,grip,from)) { Set(sensor,null,from); state.Near=true; }
            else
            {
                var ray=HeldItemPose.TryToolRay(owner,out var finger) ? finger:working;
                float reach=Reach(sensor);
                center.SetValue(sensor.Caster,ray.Translation); front.SetValue(sensor.Caster,ray.Translation+ray.Forward*reach);
                sensor.SetPointOfReference(ray.Translation);
                if(!HeldItemPose.Clear(owner,grip,ray.Translation)) { Set(sensor,null,ray.Translation); return; }
                // Native tool raycasts complete asynchronously; reject stale targets from a previous aim.
                Cast(owner,tool,ray,reach,out var block,out var entity,out var point);
                Set(sensor,block,point);
                if(entity!=null) foreach(var field in otherHits)
                    if(field.FieldType.IsInstanceOfType(entity) && !(entity is MyEnvironmentSector)) field.SetValue(sensor,entity);
                if(entity is MyEnvironmentSector sector)
                {
                    var hit=MyPhysics.CastRay(ray.Translation,point+ray.Forward*.01,24);
                    if(hit.HasValue && hit.Value.HkHitInfo.GetHitEntity()==sector)
                    {
                        otherHits[3].SetValue(sensor,sector);
                        environmentItem.SetValue(sensor,sector.GetItemFromShapeKey(hit.Value.HkHitInfo.GetShapeKey(0)));
                    }
                }
            }
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
                blocks.Clear(); grid.GetBlocksInsideSphere(ref sphere,blocks,true);
                var inv=grid.PositionComp.WorldMatrixNormalizedInv;
                var a=Vector3D.Transform(from,inv); var b=Vector3D.Transform(to,inv);
                foreach(var block in blocks)
                {
                    var bounds=new BoundingBox((block.Min-new Vector3(.5f))*grid.GridSize,(block.Max+new Vector3(.5f))*grid.GridSize);
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
                        if(!hit.HasValue || Block(hit.Value.UserObject,hit.Value.Entity)!=block) continue;
                        var actual=hit.Value.IntersectionPointInWorldSpace;
                        if(!volume.Contains(actual)) continue;
                        var score=Vector3D.DistanceSquared(actual,from);
                        if(score>=best) continue;
                        var approach=actual-grip;
                        if(approach.LengthSquared()<1e-10) continue;
                        var access=new LineD(grip,actual+Vector3D.Normalize(approach)*.003);
                        var obstruction=MyEntities.GetIntersectionWithLine(ref access,owner,tool,ignoreChildren:false,ignoreFloatingObjects:false);
                        if(obstruction.HasValue && Block(obstruction.Value.UserObject,obstruction.Value.Entity)!=block) continue;
                        best=score; selected=block; selectedHit=actual;
                    }
                }
            }
            return selected!=null;
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
            { newTransform=HeldItemPose.TryToolRay(HeldItemPose.Owner(tool),out var ray) ? ray:HeldItemPose.Working(model,profile); __instance.SetPointOfReference(newTransform.Translation); }
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
