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
using SpaceEngineersVR.Player;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;
using Sandbox.Game.Weapons.Guns;
using Sandbox.Definitions;

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

        private static MyCubeGrid constructionGrid;
        private static MyGridPhysics constructionPhysics;
        private static float buildRatio;
        private static bool Pruning(List<MyEntity> result) { result.Add(constructionGrid); return false; }
        private static bool BuildRatio(ref float __result) { __result=buildRatio; return false; }
        private static bool Pose(ref MatrixD model,ref WeaponProfile profile,ref bool __result)
        { model=MatrixD.Identity; profile=WeaponProfile.All.First(p=>p.Kind==ItemKind.Welder); __result=true; return false; }
        private static bool EmptyPruning() => false;
        private static bool supported,selectionValid=true;
        private static MatrixD finger;
        private static Vector3D selectedAt;
        private static int fingerCalls;
        private static bool Finger(ref MatrixD ray,ref bool __result) { ray=finger; __result=true; fingerCalls++; return false; }
        private static bool Selected(ref Vector3D at,ref Vector3D direction,ref bool valid,ref bool __result)
        { at=selectedAt; direction=Vector3D.Forward; valid=selectionValid; __result=true; return false; }
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
                var pruning=AccessTools.Method(typeof(MyGamePruningStructure),nameof(MyGamePruningStructure.GetAllEntitiesInSphere));
                harmony.Patch(pruning,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(EmptyPruning)));
                var grid=new MyCubeGrid();
                AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSize)).Invoke(grid,new object[] {.5f});
                var frontBlock=AddBlock(grid,Vector3I.Zero); var backBlock=AddBlock(grid,Vector3I.Forward);
                AccessTools.Field(typeof(MyCubeGrid),"m_min").SetValue(grid,Vector3I.Forward);
                AccessTools.Field(typeof(MyCubeGrid),"m_max").SetValue(grid,Vector3I.Zero);
                var forwardRay=MatrixD.CreateTranslation(0,0,.5);
                entities=new MyEntity[] {grid}; positions=new[] {new Vector3D(0,0,-.3)};
                result=new MyIntersectionResultLineTriangleEx {Entity=grid,UserObject=new MyCube {CubeBlock=frontBlock},IntersectionPointInWorldSpace=new Vector3D(0,0,.25)};
                ToolContact.CastTarget(null,tool,forwardRay,2.2,out block,out entity,out point,out item,true);
                Require(ReferenceEquals(block,frontBlock),"Farther physics hit replaced a nearer block on the same grid");
                Require(!ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.3),backBlock),"Same-grid wall did not block a farther target");
                result=null;
                Require(ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.3),backBlock),"Selected-block penetration blocked contact");
                entities=new MyEntity[] {obstacle}; positions=new[] {new Vector3D(0,0,.25)};
                Require(!ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.3),backBlock),"Eligibility hid a physical wall before the selected block");
                entities=new MyEntity[] {grid,obstacle}; positions=new[] {new Vector3D(0,0,-.3),new Vector3D(0,0,-.4)};
                Require(!ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.5),backBlock),"Selected collider hid another wall farther along the approach");
                entities=new MyEntity[] {tree}; positions=new[] {origin+Vector3D.Forward};
                result=new MyIntersectionResultLineTriangleEx {Entity=tree,IntersectionPointInWorldSpace=origin+Vector3D.Forward*.98};
                Require(ToolContact.Accessible(null,tool,origin,positions[0],null,tree,1),"Supported tree reach rejected its own render surface before its collider");
                Require(!ToolContact.Accessible(null,tool,origin,positions[0],null,tree,0),"Supported tree reach allowed a different tree item in the same sector");
                harmony.Unpatch(pruning,AccessTools.Method(typeof(ToolContactTests),nameof(EmptyPruning)));
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
            log("PASS supported tool occlusion: nearer mesh block survives same-grid physics hit, selected penetration allowed, separate walls rejected");
            log("PASS drill physics targeting: synthetic voxel hits, owner/tool filtering, nearest obstacle and stale target clearing");
            log("PASS grinder tree targeting: physics-only trees, native item propagation, closer obstacles and stale target clearing");
            SupportedAim(log);
            NativeDrillSelection(log);
            ConstructionTargeting(log);
        }
        private static void SupportedAim(Action<string> log)
        {
            var harmony=new Harmony("SEVR.ToolAim.Tests");
            var rayMethod=AccessTools.Method(typeof(HeldItemPose),nameof(HeldItemPose.TryToolRay));
            var previous=HeldItemPose.LocalSupported;
            harmony.Patch(rayMethod,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Finger)));
            HeldItemPose.LocalSupported=_=>supported;
            try
            {
                foreach(var profile in WeaponProfile.All.Where(p=>p.Tool))
                foreach(double origin in new[] {0d,1e9})
                {
                    var model=MatrixD.CreateFromYawPitchRoll(.6,-.4,.2); model.Translation=new Vector3D(origin,origin+2,origin-3);
                    finger=MatrixD.CreateFromYawPitchRoll(-.9,.7,-.1); finger.Translation=model.Translation;
                    supported=false; fingerCalls=0;
                    var aim=HeldItemPose.ToolAim(null,model,profile);
                    Require(aim==finger && fingerCalls==1 && HeldItemPose.ToolReach(null,model,profile,aim,2.2)==2.2,"One-hand tool aim/reach changed");
                    supported=true; fingerCalls=0; aim=HeldItemPose.ToolAim(null,model,profile);
                    Require(aim==HeldItemPose.Working(model,profile) && fingerCalls==0,"Supported aim consulted the competing finger ray");
                    var grip=Vector3D.Transform(profile.Primary,model);
                    var advance=Math.Max(0,Vector3D.Dot(aim.Translation-grip,aim.Forward));
                    Require(Math.Abs(advance+HeldItemPose.ToolReach(null,model,profile,aim,2.2)-2.2)<1e-7,"Tool length extended the total native reach");
                }
                var shaft=ToolVolume.DrillShaft(MatrixD.Identity);
                Require(shaft.Contains(new Vector3D(0,0,-.44)) && shaft.Contains(new Vector3D(0,0,-.58)) &&
                    !shaft.Contains(new Vector3D(0,0,-.40)) && !shaft.Contains(new Vector3D(.08,0,-.48)),"Drill shaft contact includes casing or misses exposed metal");
                var grid=new MyCubeGrid(); var disc=new ToolVolume {Frame=MatrixD.Identity,Disc=true,Radius=.108,HalfHeight=.018};
                Require(ToolContact.ConstructionOverlap(disc,grid,new BoundingBox(new Vector3(.05f,-.01f,-.05f),new Vector3(.07f,.01f,.05f))),"Grinder construction contact missed a cell through a mesh gap");
                Require(!ToolContact.ConstructionOverlap(disc,grid,new BoundingBox(new Vector3(-.05f,.04f,-.05f),new Vector3(.05f,.06f,.05f))),"Grinder construction contact included housing-side clearance");
            }
            finally { HeldItemPose.LocalSupported=previous; harmony.UnpatchAll(harmony.Id); }
            log("PASS supported tools: exclusive tip-axis aim, unchanged one-hand ray, total reach budget at rotated billion-metre origins, drill shaft/casing and grinder construction contact");
        }
        private static MySlimBlock AddBlock(MyCubeGrid grid,Vector3I cell)
        {
            var block=(MySlimBlock)FormatterServices.GetUninitializedObject(typeof(MySlimBlock));
            block.Position=cell; block.Min=cell; block.Max=cell;
            AccessTools.Field(typeof(MySlimBlock),"m_cubeGrid").SetValue(block,grid);
            block.BlockDefinition=new MyCubeBlockDefinition {PhysicalMaterial=new MyPhysicalMaterialDefinition()};
            var cubes=AccessTools.Field(typeof(MyCubeGrid),"m_cubes").GetValue(grid);
            var cubeType=cubes.GetType().GetGenericArguments()[1]; var cube=FormatterServices.GetUninitializedObject(cubeType);
            AccessTools.Field(cubeType,"CubeBlock").SetValue(cube,block);
            cubes.GetType().GetMethod("TryAdd").Invoke(cubes,new[] {(object)cell,cube}); return block;
        }
        private static void ConstructionTargeting(Action<string> log)
        {
            var harmony=new Harmony("SEVR.ConstructionTargeting.Tests");
            constructionGrid=new MyCubeGrid(); constructionPhysics=(MyGridPhysics)FormatterServices.GetUninitializedObject(typeof(MyGridPhysics)); AccessTools.Field(typeof(MyGridPhysics),"m_enabled").SetValue(constructionPhysics,true);
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSize)).Invoke(constructionGrid,new object[] {.5f});
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSizeHalfVector)).Invoke(constructionGrid,new object[] {new Vector3(.25f)});
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSizeHalf)).Invoke(constructionGrid,new object[] {.25f});
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSizeR)).Invoke(constructionGrid,new object[] {2f});
            AccessTools.Field(typeof(MyCubeGrid),"m_min").SetValue(constructionGrid,Vector3I.Zero);
            AccessTools.Field(typeof(MyCubeGrid),"m_max").SetValue(constructionGrid,Vector3I.Zero);
            var block=AddBlock(constructionGrid,Vector3I.Zero);
            block.BlockDefinition.BuildProgressModels=new[] {new MyCubeBlockDefinition.BuildProgressModel {BuildRatioUpperBound=.8f,Visible=true}};
            harmony.Patch(AccessTools.Method(typeof(MyGamePruningStructure),nameof(MyGamePruningStructure.GetAllEntitiesInSphere)),prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Pruning)));
            AccessTools.Field(typeof(MyEntity),"m_physics").SetValue(constructionGrid,constructionPhysics);
            harmony.Patch(AccessTools.PropertyGetter(typeof(MySlimBlock),nameof(MySlimBlock.BuildLevelRatio)),prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(BuildRatio)));
            harmony.Patch(AccessTools.Method(typeof(HeldItemPose),nameof(HeldItemPose.TryGet)),prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Pose)));
            var intersection=typeof(MyEntities).GetMethods().Single(m=>m.Name==nameof(MyEntities.GetIntersectionWithLine) && m.GetParameters()[0].ParameterType==typeof(LineD).MakeByRefType());
            harmony.Patch(intersection,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Intersection)));
            harmony.Patch(AccessTools.Method(typeof(MyPhysics),nameof(MyPhysics.CastRay),new[] {typeof(Vector3D),typeof(Vector3D),typeof(List<MyPhysics.HitInfo>),typeof(int)}),prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(PhysicsRay)));
            try
            {
                result=null; entities=new MyEntity[0]; positions=new Vector3D[0]; buildRatio=.4f;
                var volume=new ToolVolume {Frame=MatrixD.CreateTranslation(.12,.12,.12),Radius=.038};
                Require(block.CalculateCurrentModelID()==0,"Native construction stage fixture was not active");
                Require(ToolContact.FindNear(null,null,volume,out var found,out _) && ReferenceEquals(found,block),"Unfinished cell required a scaffold beam or collider hit");
                buildRatio=.99f;
                Require(!ToolContact.FindNear(null,null,volume,out _,out _),"Damaged completed model inherited unfinished full-cell contact");
                foreach(double origin in new[] {0d,1e9})
                {
                    var world=MatrixD.CreateFromYawPitchRoll(.6,.3,-.2); world.Translation=new Vector3D(origin,origin+2,origin-3);
                    AccessTools.Field(typeof(MyEntity),"m_physics").SetValue(constructionGrid,null);
                    constructionGrid.PositionComp.SetWorldMatrix(ref world);
                    AccessTools.Field(typeof(MyEntity),"m_physics").SetValue(constructionGrid,constructionPhysics);
                    var ray=MatrixD.CreateWorld(Vector3D.Transform(new Vector3D(.12,.12,1),world),world.Forward,world.Up);
                    buildRatio=.4f;
                    bool hit=ToolContact.ConstructionRay(ray,1.2,out found,out var at);
                    var traversed=new List<Vector3I>(); constructionGrid.RayCastCells(ray.Translation,ray.Translation+ray.Forward*1.2,traversed);
                    Require(hit && ReferenceEquals(found,block) && Math.Abs(Vector3D.Distance(at,ray.Translation)-.75)<1e-6,
                        "Construction reach missed an occupied cell: origin="+origin+", hit="+hit+", distance="+Vector3D.Distance(at,ray.Translation)+", cells="+string.Join(";",traversed)+", enabled="+constructionGrid.Physics.Enabled+", closed="+constructionGrid.Closed+
                        ", block="+ReferenceEquals(constructionGrid.GetCubeBlock(Vector3I.Zero),block)+", stage="+block.CalculateCurrentModelID()+
                        ", start="+Vector3D.Transform(ray.Translation,constructionGrid.PositionComp.WorldMatrixNormalizedInv)+
                        ", direction="+Vector3D.TransformNormal(ray.Forward,constructionGrid.PositionComp.WorldMatrixNormalizedInv));
                    Require(!ToolContact.ConstructionRay(ray,.7,out _,out _),"Construction fallback exceeded reach");
                    buildRatio=.99f;
                    Require(!ToolContact.ConstructionRay(ray,1.2,out _,out _),"Completed hollow model acquired full-cell distance targeting");
                }
            }
            finally { harmony.UnpatchAll(harmony.Id); constructionGrid=null; constructionPhysics=null; result=null; entities=null; positions=null; }
            log("PASS construction targeting: native occupied-cell contact without beam hits, rotated billion-metre reach, range limit and completed-model exclusion");
        }
        private static void NativeDrillSelection(Action<string> log)
        {
            var grid=new MyCubeGrid();
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSize)).Invoke(grid,new object[] {.5f});
            var wrong=AddBlock(grid,Vector3I.Zero); var target=AddBlock(grid,Vector3I.Forward);
            var sensor=new MyDrillSensorRayCast(-.5f,2.7f,null);
            var core=(MyDrillBase)FormatterServices.GetUninitializedObject(typeof(MyDrillBase));
            AccessTools.Field(typeof(MyDrillBase),"m_sensor").SetValue(core,sensor);
            var center=AccessTools.Field(typeof(MyDrillSensorBase),"m_center"); var front=AccessTools.Field(typeof(MyDrillSensorBase),"m_frontPoint");
            var chosen=AccessTools.Field(typeof(MyDrillBase),"m_target"); var drill=AccessTools.Method(typeof(MyDrillBase),"TryDrillBlocks");
            var harmony=new Harmony("SEVR.DrillSelection.Tests");
            try
            {
                foreach(double origin in new[] {0d,1e9})
                {
                    var world=MatrixD.CreateFromYawPitchRoll(.6,.3,-.2); world.Translation=new Vector3D(origin,origin+2,origin-3);
                    grid.PositionComp.SetWorldMatrix(ref world);
                    var start=world.Translation; selectedAt=Vector3D.Transform((Vector3D)target.Position*.5,world);
                    var end=selectedAt+world.Forward*.2;
                    center.SetValue(sensor,start); front.SetValue(sensor,end);
                    drill.Invoke(core,new object[] {grid,selectedAt,true,null});
                    Require(ReferenceEquals(chosen.GetValue(core),wrong),"Native center-first selection fixture did not reproduce the neighboring-block problem");
                    harmony.Patch(AccessTools.Method(typeof(DrillContact),nameof(DrillContact.Selected)),prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Selected)));
                    harmony.CreateClassProcessor(typeof(HeldDrillBlockPatch)).Patch();
                    selectionValid=true;
                    Require((bool)drill.Invoke(core,new object[] {grid,selectedAt,true,null}) && ReferenceEquals(chosen.GetValue(core),target),"Drill ignored the selected block");
                    Require((Vector3D)center.GetValue(sensor)==start && (Vector3D)front.GetValue(sensor)==end,"Drill retained temporary block sensor coordinates");
                    chosen.SetValue(core,null); selectionValid=false;
                    Require(!(bool)drill.Invoke(core,new object[] {grid,selectedAt,true,null}) && chosen.GetValue(core)==null,"Invalid/removed selected block fell back to its neighbor");
                    Require((Vector3D)center.GetValue(sensor)==start && (Vector3D)front.GetValue(sensor)==end,"Rejected selection changed sensor coordinates");
                    selectionValid=true; var definition=target.BlockDefinition; target.BlockDefinition=null;
                    bool failed=false;
                    try { drill.Invoke(core,new object[] {grid,selectedAt,true,null}); }
                    catch(System.Reflection.TargetInvocationException) { failed=true; }
                    finally { target.BlockDefinition=definition; }
                    Require(failed && (Vector3D)center.GetValue(sensor)==start && (Vector3D)front.GetValue(sensor)==end,"Drill exception did not restore sensor coordinates");
                    harmony.UnpatchAll(harmony.Id);
                }
            }
            finally { harmony.UnpatchAll(harmony.Id); selectionValid=true; }
            log("PASS native drill block selection: reproduced center-first neighbor, selected-cell override at rotated billion-metre origin, stale-selection rejection and sensor restoration");
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
