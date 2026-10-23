#if SEVR_DIAGNOSTICS
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
        // The public CastRay wrapper can be inlined into its caller before this patch is installed.
        internal static readonly System.Reflection.MethodInfo CastRayInternal=AccessTools.Method(typeof(MyPhysics),"CastRayInternal",
            new[] {typeof(Vector3D).MakeByRefType(),typeof(Vector3D).MakeByRefType(),typeof(List<MyPhysics.HitInfo>),typeof(int)});
        private static bool PhysicsRay(ref Vector3D from,ref Vector3D to,List<MyPhysics.HitInfo> toList,int raycastFilterLayer)
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
        private static bool Pruning(List<MyEntity> result) { result.Add(constructionGrid); return false; }
        // The engine inlines the BuildLevelRatio getter into this method, so the stage is stubbed one level up.
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
            var physics=CastRayInternal;
            var entityMethod=AccessTools.Method(typeof(ToolContact),nameof(ToolContact.HitEntity));
            var treeItem=AccessTools.Method(typeof(ToolContact),nameof(ToolContact.HitItem));
            var clearMethod=AccessTools.Method(typeof(HeldItemPose),nameof(HeldItemPose.Clear));
            harmony.Patch(physics,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(PhysicsRay)));
            harmony.Patch(entityMethod,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(HitEntity)));
            harmony.Patch(treeItem,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(TreeItem)));
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Intersection)));
            harmony.Patch(clearMethod,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Clear)));
            var pruning=AccessTools.Method(typeof(ToolContact),nameof(ToolContact.EntitiesInSphere));
            harmony.Patch(pruning,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(EmptyPruning)));
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
                var grid=new MyCubeGrid();
                AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSize)).Invoke(grid,new object[] {.5f});
                var frontBlock=AddBlock(grid,Vector3I.Zero); var backBlock=AddBlock(grid,Vector3I.Forward);
                AccessTools.Field(typeof(MyCubeGrid),"m_min").SetValue(grid,Vector3I.Forward);
                AccessTools.Field(typeof(MyCubeGrid),"m_max").SetValue(grid,Vector3I.Zero);
                var forwardRay=MatrixD.CreateTranslation(0,0,.5);
                entities=new MyEntity[] {grid}; positions=new[] {new Vector3D(0,0,-.3)};
                result=null;
                ToolContact.CastTarget(null,tool,forwardRay,2.2,out block,out entity,out point,out item);
                Require(ReferenceEquals(block,backBlock) && ReferenceEquals(entity,grid),"One-hand ray ignored a native grid collider");
                result=new MyIntersectionResultLineTriangleEx {Entity=grid,UserObject=new MyCube {CubeBlock=frontBlock},IntersectionPointInWorldSpace=new Vector3D(0,0,.25)};
                ToolContact.CastTarget(null,tool,forwardRay,2.2,out block,out entity,out point,out item);
                Require(ReferenceEquals(block,frontBlock),"Farther physics hit replaced a nearer block on the same grid");
                Require(!ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.3),backBlock),"Same-grid wall did not block a farther target");
                result=null;
                Require(ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.3),backBlock),"Selected-block penetration blocked contact");
                entities=new MyEntity[] {obstacle}; positions=new[] {new Vector3D(0,0,.25)};
                Require(!ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.3),backBlock),"Eligibility hid a physical wall before the selected block");
                entities=new MyEntity[] {grid,obstacle}; positions=new[] {new Vector3D(0,0,-.3),new Vector3D(0,0,-.4)};
                Require(!ToolContact.Accessible(null,tool,forwardRay.Translation,new Vector3D(0,0,-.5),backBlock),"Selected collider hid another wall farther along the approach");
            }
            finally
            {
                harmony.Unpatch(pruning,AccessTools.Method(typeof(ToolContactTests),nameof(EmptyPruning)));
                harmony.Unpatch(physics,AccessTools.Method(typeof(ToolContactTests),nameof(PhysicsRay)));
                harmony.Unpatch(entityMethod,AccessTools.Method(typeof(ToolContactTests),nameof(HitEntity)));
                harmony.Unpatch(treeItem,AccessTools.Method(typeof(ToolContactTests),nameof(TreeItem)));
                harmony.Unpatch(method,AccessTools.Method(typeof(ToolContactTests),nameof(Intersection)));
                harmony.Unpatch(clearMethod,AccessTools.Method(typeof(ToolContactTests),nameof(Clear)));
                clear=true; result=null;
                entities=null; positions=null;
            }
            log("PASS tool ray targeting: native grid collider from one hand, nearer mesh block survives the same-grid physics hit, selected penetration allowed, separate walls rejected");
            log("PASS drill physics targeting: synthetic voxel hits, owner/tool filtering, nearest obstacle and stale target clearing");
            log("PASS grinder tree targeting: physics-only trees, native item propagation, closer obstacles and stale target clearing");
            SupportedAim(log);
            NativeDrillSelection(log);
            ConstructionTargeting(log);
            DrillRayTests.Run(log);
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
                var caster=new MyCasterComponent(new MyDrillSensorRayCast(0,2f,null));
                var casterOrigin=new Vector3D(1e9,2,-3);
                AccessTools.Field(typeof(MyDrillSensorBase),"m_center").SetValue(caster.Caster,casterOrigin);
                AccessTools.Field(typeof(MyDrillSensorBase),"m_frontPoint").SetValue(caster.Caster,casterOrigin+new Vector3D(0,0,-1.3));
                Require(Math.Abs(ToolContact.ProjectionMultiplier(caster)-.65)<1e-6,"Projected blocks ignored the clamped tool reach");
            }
            finally { HeldItemPose.LocalSupported=previous; harmony.UnpatchAll(harmony.Id); }
            log("PASS supported tools: exclusive tip-axis aim, unchanged one-hand ray, total reach budget at rotated billion-metre origins, projection reach from the caster ray");
        }
        private static void Size(MyCubeGrid grid,float size)
        {
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSize)).Invoke(grid,new object[] {size});
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSizeHalfVector)).Invoke(grid,new object[] {new Vector3(size*.5f)});
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSizeHalf)).Invoke(grid,new object[] {size*.5f});
            AccessTools.PropertySetter(typeof(MyCubeGrid),nameof(MyCubeGrid.GridSizeR)).Invoke(grid,new object[] {1f/size});
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
            Size(constructionGrid,.5f);
            AccessTools.Field(typeof(MyCubeGrid),"m_min").SetValue(constructionGrid,Vector3I.Zero);
            AccessTools.Field(typeof(MyCubeGrid),"m_max").SetValue(constructionGrid,Vector3I.Zero);
            AddBlock(constructionGrid,Vector3I.Zero);
            harmony.Patch(AccessTools.Method(typeof(ToolContact),nameof(ToolContact.EntitiesInSphere)),prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Pruning)));
            AccessTools.Field(typeof(MyEntity),"m_physics").SetValue(constructionGrid,constructionPhysics);
            harmony.Patch(AccessTools.Method(typeof(HeldItemPose),nameof(HeldItemPose.TryGet)),prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Pose)));
            var intersection=typeof(MyEntities).GetMethods().Single(m=>m.Name==nameof(MyEntities.GetIntersectionWithLine) && m.GetParameters()[0].ParameterType==typeof(LineD).MakeByRefType());
            harmony.Patch(intersection,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(Intersection)));
            harmony.Patch(CastRayInternal,prefix:new HarmonyMethod(typeof(ToolContactTests),nameof(PhysicsRay)));
            try
            {
                result=null; entities=new MyEntity[0]; positions=new Vector3D[0];
                var volume=new ToolVolume {Frame=MatrixD.CreateTranslation(.12,.12,.12),Radius=.038};
                Require(!ToolContact.FindNear(null,null,volume,out _,out _),"Unfinished cell was touched without a collider or mesh hit");
                foreach(float size in new[] {.5f,2.5f})
                {
                    Size(constructionGrid,size);
                    var world=MatrixD.CreateFromYawPitchRoll(-.2,.5,.3); world.Translation=new Vector3D(1e9,1e9+2,1e9-3);
                    AccessTools.Field(typeof(MyEntity),"m_physics").SetValue(constructionGrid,null);
                    constructionGrid.PositionComp.SetWorldMatrix(ref world);
                    AccessTools.Field(typeof(MyEntity),"m_physics").SetValue(constructionGrid,constructionPhysics);
                    foreach(double depth in new[] {size*.5+1,0})
                    {
                        var ray=MatrixD.CreateWorld(Vector3D.Transform(new Vector3D(size*.24,size*.24,depth),world),world.Forward,world.Up);
                        ToolContact.CastTarget(null,null,ray,1.2,out var reached,out _,out _,out _);
                        Require(reached==null,"Ray selected an unfinished cell without a collider or mesh hit on grid size "+size+(depth==0 ? " from inside":""));
                    }
                }
            }
            finally { harmony.UnpatchAll(harmony.Id); constructionGrid=null; constructionPhysics=null; result=null; entities=null; positions=null; }
            log("PASS construction targeting: unfinished blocks are selected only through native colliders or mesh, by contact and by ray from outside or inside the cell, on both cube sizes");
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
#endif
