using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System;
using System.Reflection.Emit;
using Sandbox.Engine.Physics;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;
using Sandbox.Game.Weapons.Guns;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal static class DrillContact
    {
        private sealed class State
        {
            internal MyHandDrill Tool;
            internal MyDrillBase Core;
            internal MySlimBlock Block;
            internal double At;
            internal bool Active;
            internal readonly Dictionary<long,MyDrillSensorBase.DetectionInfo> Hits=new Dictionary<long,MyDrillSensorBase.DetectionInfo>();
        }
        private static readonly ConditionalWeakTable<MyDrillSensorBase,State> states=new ConditionalWeakTable<MyDrillSensorBase,State>();
        private static readonly FieldInfo center=AccessTools.Field(typeof(MyDrillSensorBase),"m_center"),front=AccessTools.Field(typeof(MyDrillSensorBase),"m_frontPoint");
        private static readonly ConditionalWeakTable<MyHandDrill,State> tools=new ConditionalWeakTable<MyHandDrill,State>();
        internal static void Refresh(MyHandDrill tool)
        {
            if(!tools.TryGetValue(tool,out var state)) return;
            Dictionary<long,MyDrillSensorBase.DetectionInfo> hits=null; Read(state.Core.Sensor,ref hits);
        }
        internal static void Register(MyDrillBase core,MyHandDrill tool)
        {
            var state=states.GetValue(core.Sensor,_=>new State()); state.Tool=tool; state.Core=core; tools.GetValue(tool,_=>state);
            Dictionary<long,MyDrillSensorBase.DetectionInfo> hits=null; Read(core.Sensor,ref hits);
        }
        internal static MatrixD Ray(MatrixD model,MatrixD finger,bool supported)
        {
            if(!supported) return finger;
            var ray=model;
            ray.Translation=Vector3D.Transform(new Vector3D(0,0,-.45),model);
            return ray;
        }
        internal static void Read(MyDrillSensorBase sensor,ref Dictionary<long,MyDrillSensorBase.DetectionInfo> result)
        {
            if(!states.TryGetValue(sensor,out var state)) return;
            state.Active=false; state.Block=null; state.At=MultiplayerRuntime.Now;
            if(!HeldItemPose.TryGet(state.Tool.Owner,out var model,out var profile)) { state.Hits.Clear(); return; }
            state.Active=true; result=state.Hits;
            var supported=HeldItemPose.Supported(state.Tool.Owner);
            var ray=Ray(model,HeldItemPose.ToolAim(state.Tool.Owner,model,profile,supported),supported);
            var grip=Vector3D.Transform(profile.Primary,model);
            if(!HeldItemPose.Clear(state.Tool.Owner,state.Tool.Owner.GetHeadMatrix(true,true).Translation,grip) ||
                !HeldItemPose.Clear(state.Tool.Owner,grip,ray.Translation)) { state.Hits.Clear(); return; }
            UpdateTargets(state.Core,state.Tool,ray,state.Hits,out state.Block);
        }
        internal static void UpdateTargets(MyDrillBase core,MyHandDrill tool,MatrixD ray,
            Dictionary<long,MyDrillSensorBase.DetectionInfo> hits,out MySlimBlock block)
        {
            hits.Clear(); block=null;
            if(states.TryGetValue(core.Sensor,out var state)) state.Active=true;
            center.SetValue(core.Sensor,ray.Translation);
            // Native drill sensor: 2.7 m from the neck.
            var endpoint=ray.Translation+ray.Forward*(tool?.Owner==null ? 2.2:Math.Min(2.2,HeldItemPose.ReachAlong(HeldItemPose.ReachOrigin(tool.Owner),ray.Translation,ray.Forward,2.7)));
            front.SetValue(core.Sensor,endpoint);
            core.CutOut.UpdatePosition(ref ray);
            if(!ToolContact.PhysicsCast(tool?.Owner,tool,new LineD(ray.Translation,endpoint),out var hit)) return;
            if(hit.Entity is MyCubeGrid grid) block=ToolContact.ResolveBlock(grid,hit.DetectionPoint);
            var point=hit.DetectionPoint+ray.Forward*.005;
            ray.Translation=point-ray.Forward*core.CutOut.CenterOffset;
            core.CutOut.UpdatePosition(ref ray);
            hits[hit.Entity.EntityId]=new MyDrillSensorBase.DetectionInfo(hit.Entity,point,hit.ItemId);
            front.SetValue(core.Sensor,point);
        }
        internal static LineD MaterialLine(MyDrillBase core,Vector3D from,Vector3D to)
        {
            if(!states.TryGetValue(core.Sensor,out var state) || !state.Active) return new LineD(from,to);
            from=core.Sensor.Center; to=core.Sensor.FrontPoint;
            var direction=to-from;
            return direction.LengthSquared()>1e-10 ? new LineD(from,to+Vector3D.Normalize(direction)):new LineD(from,to);
        }
        internal static bool Selected(MyDrillBase core,MyCubeGrid grid,out Vector3D at,out Vector3D direction,out bool valid)
        {
            at=default(Vector3D); direction=Vector3D.Forward; valid=false;
            if(!states.TryGetValue(core.Sensor,out var state) || !state.Active || state.Block==null) return false;
            var block=state.Block;
            if(block.CubeGrid!=grid || MultiplayerRuntime.Now-state.At>=.2 || grid.GetCubeBlock(block.Position)!=block ||
                !HeldItemPose.TryGet(state.Tool.Owner,out var model,out var profile)) return true;
            valid=true;
            var local=Vector3D.Transform((Vector3)block.Position*grid.GridSize,grid.WorldMatrix);
            at=local; direction=HeldItemPose.Working(model,profile).Forward; return true;
        }
    }
    [HarmonyPatch(typeof(MyDrillBase),"DrillVoxel")]
    internal static class DrillMaterialRayPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var cast=AccessTools.Method(typeof(MyPhysics),nameof(MyPhysics.CastRay),new[] {typeof(Vector3D),typeof(Vector3D),typeof(List<MyPhysics.HitInfo>),typeof(int)});
            int count=0;
            foreach(var instruction in instructions)
            {
                if(instruction.Calls(cast))
                {
                    count++;
                    yield return new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(instruction);
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(DrillMaterialRayPatch),nameof(Cast)));
                }
                else yield return instruction;
            }
            if(count!=1) throw new InvalidOperationException("Native drill material ray changed");
        }
        private static void Cast(Vector3D from,Vector3D to,List<MyPhysics.HitInfo> hits,int layer,MyDrillBase core)
        {
            // Material feedback must follow the same ray that selected the drilling target.
            var line=DrillContact.MaterialLine(core,from,to);
            MyPhysics.CastRay(line.From,line.To,hits,layer);
        }
    }
    [HarmonyPatch(typeof(MyDrillBase),"TryDrillBlocks")]
    internal static class HeldDrillBlockPatch
    {
        private static readonly FieldInfo center=AccessTools.Field(typeof(MyDrillSensorBase),"m_center"),front=AccessTools.Field(typeof(MyDrillSensorBase),"m_frontPoint");
        private static bool Prefix(MyDrillBase __instance,MyCubeGrid grid,ref bool __result,out Vector3D[] __state)
        {
            __state=null;
            if(!DrillContact.Selected(__instance,grid,out var at,out var direction,out var valid)) return true;
            if(!valid) { __result=false; return false; }
            var sensor=__instance.Sensor;
            __state=new[] {(Vector3D)center.GetValue(sensor),(Vector3D)front.GetValue(sensor)};
            center.SetValue(sensor,at); front.SetValue(sensor,at+direction*.01); return true;
        }
        private static void Finalizer(MyDrillBase __instance,Vector3D[] __state)
        {
            if(__state==null) return;
            center.SetValue(__instance.Sensor,__state[0]); front.SetValue(__instance.Sensor,__state[1]);
        }
    }
    [HarmonyPatch(typeof(MyDrillSensorBase),"get_CachedEntitiesInRange")]
    internal static class HeldDrillContactPatch
    {
        private static void Postfix(MyDrillSensorBase __instance,ref Dictionary<long,MyDrillSensorBase.DetectionInfo> __result) => DrillContact.Read(__instance,ref __result);
    }
    [HarmonyPatch(typeof(MyWelder),"GetEffectMatrix")]
    internal static class WelderContactEffectPatch
    {
        private static void Postfix(MyWelder __instance,object effectType,ref MatrixD __result)
        {
            if(System.Convert.ToInt32(effectType)!=1 || !ToolContact.Near(__instance) || !HeldItemPose.TryGet(HeldItemPose.Owner(__instance),out var model,out var profile)) return;
            var sensor=(Sandbox.Game.EntityComponents.MyCasterComponent)ToolContact.Sensor.GetValue(__instance);
            if(sensor.HitBlock!=null) __result=MatrixD.CreateWorld(sensor.HitPosition,-HeldItemPose.Working(model,profile).Forward,model.Up);
        }
    }
}
