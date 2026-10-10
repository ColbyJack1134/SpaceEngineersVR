using System;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Entities;
using SpaceEngineersVR.Multiplayer;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRageMath;
using System.Collections.Generic;
using Sandbox.Engine.Physics;
using Havok;
using System.Runtime.Serialization;
using Sandbox.Game.WorldEnvironment;
using Sandbox.Game.EntityComponents;
using VRage.Library.Collections;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ToolContactTests
    {
        private static MyIntersectionResultLineTriangleEx? result;
        private static bool Intersection(ref MyIntersectionResultLineTriangleEx? __result)
        { __result=result; return false; }
        private static MyEntity[] entities;
        private static Vector3D[] positions;
        private static int hitIndex;
        private static bool clear=true;
        private static bool Clear(ref bool __result) { __result=clear; return false; }
        private sealed class TreeView : MyEnvironmentDataView { public override void Close() { } }
        private static bool TreeItem(ref int __result) { __result=1; return false; }
        private static bool PhysicsRay(Vector3D from,Vector3D to,List<MyPhysics.HitInfo> toList,int raycastFilterLayer)
        {
            Require(raycastFilterLayer==24,"Drill did not use the native tool collision layer");
            hitIndex=0;
            for(int i=0;i<positions.Length;i++) toList.Add(new MyPhysics.HitInfo(default(HkHitInfo),positions[i]));
            return false;
        }
        private static bool HitEntity(ref VRage.ModAPI.IMyEntity __result)
        { __result=entities[hitIndex++]; return false; }

        internal static void Run(Action<string> log)
        {
            var method=typeof(MyEntities).GetMethods().Single(m=>m.Name==nameof(MyEntities.GetIntersectionWithLine) &&
                m.GetParameters()[0].ParameterType==typeof(LineD).MakeByRefType());
            var harmony=new Harmony("SEVR.ToolContact.Tests");
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Intersection)));
            try
            {
                var ray=MatrixD.CreateTranslation(2000000,-3000000,4000000);
                var end=ray.Translation+ray.Forward*2;
                foreach(var hit in new MyIntersectionResultLineTriangleEx?[] {null,default(MyIntersectionResultLineTriangleEx)})
                {
                    result=hit;
                    Require(!ToolContact.Cast(null,null,ray,2,out var block,out var entity,out var point) &&
                        block==null && entity==null && point==end,"Empty/entityless tool ray retained a target or changed its endpoint");
                }
                var target=new MyEntity();
                var contact=ray.Translation+ray.Forward;
                result=new MyIntersectionResultLineTriangleEx {Entity=target,IntersectionPointInWorldSpace=contact};
                Require(ToolContact.Cast(null,null,ray,2,out var selected,out var found,out var position) &&
                    selected==null && ReferenceEquals(found,target) && position==contact,"Valid non-block tool hit was lost");
            }
            finally { result=null; harmony.Unpatch(method,AccessTools.Method(typeof(ToolContactTests),nameof(Intersection))); }
            log("PASS tool contact: native empty/entityless raycast results clear targets, valid entity hits retain contact position");
            var physics=AccessTools.Method(typeof(MyPhysics),nameof(MyPhysics.CastRay),new[] {typeof(Vector3D),typeof(Vector3D),typeof(List<MyPhysics.HitInfo>),typeof(int)});
            var entityMethod=AccessTools.Method(typeof(MyPhysicsExtensions),nameof(MyPhysicsExtensions.GetHitEntity));
            var treeItem=AccessTools.Method(typeof(MyEnvironmentSector),nameof(MyEnvironmentSector.GetItemFromShapeKey));
            var clearMethod=AccessTools.Method(typeof(HeldItemPose),nameof(HeldItemPose.Clear));
            harmony.Patch(physics,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(PhysicsRay)));
            harmony.Patch(entityMethod,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(HitEntity)));
            harmony.Patch(treeItem,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(TreeItem)));
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Intersection)));
            harmony.Patch(clearMethod,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Clear)));
            try
            {
                var owner=new MyEntity(); var tool=new MyEntity();
                var voxel=(MyVoxelMap)FormatterServices.GetUninitializedObject(typeof(MyVoxelMap)); var obstacle=new MyEntity();
                var origin=new Vector3D(2000000,-3000000,4000000);
                var line=new LineD(origin,origin+Vector3D.Forward*2.2);
                entities=new MyEntity[] {voxel,owner,tool,null};
                positions=new[] {origin+Vector3D.Forward,origin,origin+Vector3D.Forward*.1,origin};
                Require(ToolContact.PhysicsCast(owner,tool,line,out var hit) && ReferenceEquals(hit.Entity,voxel) &&
                    hit.DetectionPoint==positions[0],"Drill lost a physics-only voxel hit or selected its owner/tool");
                entities=new[] {voxel,obstacle}; positions=new[] {origin+Vector3D.Forward,origin+Vector3D.Forward*.5};
                Require(ToolContact.PhysicsCast(owner,tool,line,out hit) && ReferenceEquals(hit.Entity,obstacle),"Drill selected terrain through a closer obstacle");
                entities=new MyEntity[0]; positions=new Vector3D[0];
                Require(!ToolContact.PhysicsCast(owner,tool,line,out hit) && hit.Entity==null,"Drill retained a previous physics target");
                var tree=(MyEnvironmentSector)FormatterServices.GetUninitializedObject(typeof(MyEnvironmentSector));
                var view=new TreeView {Items=new MyList<ItemInfo>()};
                view.Items.Add(new ItemInfo {ModelIndex=0}); view.Items.Add(new ItemInfo {ModelIndex=0});
                AccessTools.Property(typeof(MyEnvironmentSector),nameof(MyEnvironmentSector.DataView)).SetValue(tree,view);
                entities=new MyEntity[] {tree}; positions=new[] {origin+Vector3D.Forward}; result=null;
                ToolContact.CastTarget(null,tool,MatrixD.CreateTranslation(origin),2.2,out var block,out var entity,out var point,out int item);
                Require(block==null && ReferenceEquals(entity,tree) && point==positions[0] && item==1,"Physics-only tree target lost its environment item");
                result=new MyIntersectionResultLineTriangleEx {Entity=tree,IntersectionPointInWorldSpace=origin+Vector3D.Forward*.98};
                ToolContact.CastTarget(null,tool,MatrixD.CreateTranslation(origin),2.2,out block,out entity,out point,out item);
                Require(ReferenceEquals(entity,tree) && item==1 && point==positions[0],"Tree render mesh prevented native collision item selection");
                result=null;
                var sensor=(MyCasterComponent)FormatterServices.GetUninitializedObject(typeof(MyCasterComponent));
                sensor.SetPointOfReference(origin);
                ToolContact.SetTarget(sensor,block,entity,point,item);
                Require(ReferenceEquals(sensor.HitEnvironmentSector,tree) && sensor.EnvironmentItem==1,"Grinder caster lost the tree item needed by native cutting");
                ToolContact.SetTarget(sensor,null,null,line.To,0);
                Require(sensor.HitEnvironmentSector==null,"Grinder retained a stale tree target");
                entities=new MyEntity[] {tree,obstacle}; positions=new[] {origin+Vector3D.Forward,origin+Vector3D.Forward*.5};
                ToolContact.CastTarget(null,tool,MatrixD.CreateTranslation(origin),2.2,out block,out entity,out point,out item);
                Require(ReferenceEquals(entity,obstacle),"Grinder selected a tree behind a closer obstacle");
                var volume=new ToolVolume {Frame=MatrixD.CreateTranslation(origin),Disc=true,Radius=.108,HalfHeight=.018};
                entities=new MyEntity[] {tree}; positions=new[] {origin+Vector3D.Right*.05};
                Require(ToolContact.FindTreeNear(null,tool,volume,origin+Vector3D.Backward,out var near) && ReferenceEquals(near.Entity,tree) && near.ItemId==1,
                    "Grinder blade contact did not retain a tree and its native item");
                clear=false;
                Require(!ToolContact.FindTreeNear(null,tool,volume,origin+Vector3D.Backward,out near),"Grinder tree contact ignored grip occlusion");
                clear=true;
                positions=new[] {origin+Vector3D.Right*.2};
                Require(!ToolContact.FindTreeNear(null,tool,volume,origin+Vector3D.Backward,out near),"Grinder tree contact extended beyond its blade volume");
            }
            finally
            {
                harmony.Unpatch(physics,AccessTools.Method(typeof(ToolContactTests),nameof(PhysicsRay)));
                harmony.Unpatch(entityMethod,AccessTools.Method(typeof(ToolContactTests),nameof(HitEntity)));
                harmony.Unpatch(treeItem,AccessTools.Method(typeof(ToolContactTests),nameof(TreeItem)));
                harmony.Unpatch(method,AccessTools.Method(typeof(ToolContactTests),nameof(Intersection)));
                harmony.Unpatch(clearMethod,AccessTools.Method(typeof(ToolContactTests),nameof(Clear)));
                clear=true; result=null;
                entities=null; positions=null;
            }
            log("PASS drill physics targeting: synthetic voxel hits, owner/tool filtering, nearest obstacle and stale target clearing");
            log("PASS grinder tree targeting: physics-only trees, native item propagation, closer obstacles and stale target clearing");
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
