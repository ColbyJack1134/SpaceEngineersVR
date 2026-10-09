using System;
using System.Linq;
using HarmonyLib;
using Sandbox.Game.Entities;
using SpaceEngineersVR.Multiplayer;
using VRage.Game.Entity;
using VRage.Game.Models;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ToolContactTests
    {
        private static MyIntersectionResultLineTriangleEx? result;
        private static bool Intersection(ref MyIntersectionResultLineTriangleEx? __result)
        { __result=result; return false; }

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
        }
        private static void Require(bool value,string message) { if(!value) throw new Exception(message); }
    }
}
