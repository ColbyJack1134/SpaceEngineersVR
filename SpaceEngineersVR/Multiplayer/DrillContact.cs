using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Sandbox.Game.Entities;
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
            internal bool Near;
            internal double At;
            internal readonly Dictionary<long,MyDrillSensorBase.DetectionInfo> Hits=new Dictionary<long,MyDrillSensorBase.DetectionInfo>();
        }
        private static readonly ConditionalWeakTable<MyDrillSensorBase,State> states=new ConditionalWeakTable<MyDrillSensorBase,State>();
        private static readonly FieldInfo center=AccessTools.Field(typeof(MyDrillSensorBase),"m_center"),front=AccessTools.Field(typeof(MyDrillSensorBase),"m_frontPoint");
        private static readonly ConditionalWeakTable<MyHandDrill,State> tools=new ConditionalWeakTable<MyHandDrill,State>();
        internal static bool Near(MyHandDrill tool) => tools.TryGetValue(tool,out var state) && state.Near && MultiplayerRuntime.Now-state.At<.2;
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
        internal static void Read(MyDrillSensorBase sensor,ref Dictionary<long,MyDrillSensorBase.DetectionInfo> result)
        {
            if(!states.TryGetValue(sensor,out var state) || !HeldItemPose.TryGet(state.Tool.Owner,out var model,out var profile)) return;
            state.Hits.Clear(); result=state.Hits; state.Near=false; state.At=MultiplayerRuntime.Now;
            var working=HeldItemPose.Working(model,profile);
            var ray=HeldItemPose.TryToolRay(state.Tool.Owner,out var finger) ? finger:model;
            var origin=ray.Translation;
            center.SetValue(sensor,origin);
            // Preserve native reach and cutting radius; proximity moves the cutting center to the bit.
            var endpoint=origin+ray.Forward*2.2;
            front.SetValue(sensor,endpoint);
            var grip=Vector3D.Transform(profile.Primary,model);
            if(!HeldItemPose.Clear(state.Tool.Owner,state.Tool.Owner.GetHeadMatrix(true,true).Translation,grip)) return;
            var volume=ToolVolume.For(profile,model);
            var cutPose=ray; state.Core.CutOut.UpdatePosition(ref cutPose);
            bool blockNear=ToolContact.FindNear(state.Tool.Owner,state.Tool,volume,out var block,out var near);
            VRage.Game.Entity.MyEntity closest=blockNear ? block.CubeGrid:null;
            Vector3D contact=near;
            double best=blockNear ? Vector3D.DistanceSquared(near,volume.Start):double.MaxValue;
            for(int i=0;i<7;i++)
            {
                var probe=volume.Probe(i);
                if(!ToolContact.PhysicsCast(state.Tool.Owner,state.Tool,probe,out var sample) || !(sample.Entity is MyVoxelBase voxel)) continue;
                var samplePoint=sample.DetectionPoint;
                double score=Vector3D.DistanceSquared(samplePoint,volume.Start);
                var approach=samplePoint-grip;
                if(volume.Contains(samplePoint) && score<best && approach.LengthSquared()>1e-10 &&
                    HeldItemPose.Clear(state.Tool.Owner,grip,samplePoint-Vector3D.Normalize(approach)*.005))
                { closest=voxel; contact=samplePoint; best=score; blockNear=false; }
            }
            if(closest!=null)
            {
                state.Near=true;
                var direction=blockNear ? Vector3D.Transform((Vector3)block.Position*block.CubeGrid.GridSize,block.CubeGrid.WorldMatrix)-contact:working.Forward;
                if(direction.LengthSquared()<1e-10) direction=working.Forward;
                contact+=Vector3D.Normalize(direction)*.005;
                cutPose.Translation=volume.Bounds.Center-cutPose.Forward*state.Core.CutOut.CenterOffset;
                state.Core.CutOut.UpdatePosition(ref cutPose);
                state.Hits[closest.EntityId]=new MyDrillSensorBase.DetectionInfo(closest,contact);
                front.SetValue(sensor,contact); return;
            }
            if(HeldItemPose.Supported(state.Tool.Owner)) return;
            if(!HeldItemPose.Clear(state.Tool.Owner,grip,origin)) { state.Near=true; return; }
            var line=new LineD(origin,endpoint);
            if(!ToolContact.PhysicsCast(state.Tool.Owner,state.Tool,line,out var hit)) return;
            var point=hit.DetectionPoint+ray.Forward*.005;
            state.Hits[hit.Entity.EntityId]=new MyDrillSensorBase.DetectionInfo(hit.Entity,point,hit.ItemId);
            front.SetValue(sensor,point);
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
