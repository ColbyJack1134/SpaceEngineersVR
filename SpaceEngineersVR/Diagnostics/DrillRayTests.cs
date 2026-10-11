using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Havok;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Weapons;
using Sandbox.Game.Weapons.Guns;
using SpaceEngineersVR.Multiplayer;
using VRage.Game.Entity;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class DrillRayTests
    {
        private static MyEntity[] entities;
        private static Vector3D[] points;
        private static int index;
        private static LineD line;
        private static bool Physics(Vector3D from,Vector3D to,List<MyPhysics.HitInfo> toList)
        {
            line=new LineD(from,to); index=0;
            foreach(var point in points) toList.Add(new MyPhysics.HitInfo(default(HkHitInfo),point));
            return false;
        }
        private static bool Entity(ref VRage.ModAPI.IMyEntity __result) { __result=entities[index++]; return false; }
        private static void Require(bool value,string reason) { if(!value) throw new InvalidOperationException(reason); }
        internal static void Run(Action<string> log)
        {
            var core=(MyDrillBase)FormatterServices.GetUninitializedObject(typeof(MyDrillBase));
            var tool=(MyHandDrill)FormatterServices.GetUninitializedObject(typeof(MyHandDrill));
            var sensor=new MyDrillSensorRayCast(-.5f,2.7f,null);
            AccessTools.Field(typeof(MyDrillBase),"m_sensor").SetValue(core,sensor);
            AccessTools.Field(typeof(MyDrillBase),"m_cutOut").SetValue(core,new MyDrillCutOut(1,.35f));
            DrillContact.Register(core,tool);
            var hits=new Dictionary<long,MyDrillSensorBase.DetectionInfo>();
            var front=new MyEntity {EntityId=101}; var behind=new MyEntity {EntityId=102};
            var harmony=new Harmony("SEVR.DrillRay.Tests");
            harmony.Patch(AccessTools.Method(typeof(MyPhysics),nameof(MyPhysics.CastRay),new[] {typeof(Vector3D),typeof(Vector3D),typeof(List<MyPhysics.HitInfo>),typeof(int)}),prefix:new HarmonyMethod(typeof(DrillRayTests),nameof(Physics)));
            harmony.Patch(AccessTools.Method(typeof(MyPhysicsExtensions),nameof(MyPhysicsExtensions.GetHitEntity)),prefix:new HarmonyMethod(typeof(DrillRayTests),nameof(Entity)));
            try
            {
                foreach(double origin in new[] {0d,1e9})
                {
                    var model=MatrixD.CreateFromYawPitchRoll(.6,-.4,.2); model.Translation=new Vector3D(origin,origin+2,origin-3);
                    var finger=MatrixD.CreateFromYawPitchRoll(-.9,.7,-.1); finger.Translation=model.Translation;
                    Require(DrillContact.Ray(model,finger,false)==finger,"Finger drill ray changed");
                    var supported=DrillContact.Ray(model,finger,true);
                    Require(Vector3D.Distance(supported.Translation,Vector3D.Transform(new Vector3D(0,0,-.45),model))<1e-7 && supported.Forward==model.Forward,"Supported drill ray is not at the metal base");
                    foreach(var ray in new[] {finger,supported})
                    {
                        entities=new[] {behind,front}; points=new[] {ray.Translation+ray.Forward*1.003,ray.Translation+ray.Forward};
                        DrillContact.UpdateTargets(core,null,ray,hits,out _);
                        Require(hits.Count==1 && hits.ContainsKey(front.EntityId),"Shared drill ray failed nearest-hit selection with overlapping hits");
                        Require(Vector3D.Distance(line.From,ray.Translation)<1e-7 && Math.Abs(line.Length-2.2)<1e-6,"Drill ray length depends on support");
                        Require(Vector3D.Distance(core.CutOut.Sphere.Center,ray.Translation+ray.Forward*1.005)<1e-6,"Drill cut center does not follow the shared ray hit");
                        var material=DrillContact.MaterialLine(core,model.Translation-model.Forward*.5,model.Translation);
                        Require(material.From==ray.Translation && Vector3D.Cross(material.Direction,ray.Forward).Length()<1e-6 && Vector3D.Distance(material.To,sensor.FrontPoint+ray.Forward)<1e-6,"Material feedback does not follow the selected ray");
                        entities=new MyEntity[0]; points=new Vector3D[0];
                        DrillContact.UpdateTargets(core,null,ray,hits,out _);
                        Require(hits.Count==0 && Vector3D.Distance(sensor.FrontPoint,ray.Translation+ray.Forward*2.2)<1e-7,"Ray miss retained a stale drilling target");
                    }
                }
            }
            finally { harmony.UnpatchAll(harmony.Id); entities=null; points=null; }
            log("PASS ray-only drill: shared nearest-hit selection, overlap ordering, finger/base origins, equal ray/cut behavior, aligned material feedback and stale-target clearing at rotated billion-metre coordinates");
        }
    }
}
