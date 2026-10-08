using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRage.Utils;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StereoFlareTests
    {
        private static object flare;
        private static Vector3D position;
        private static Vector3D[] corners;
        private static readonly Type native=AccessTools.TypeByName("VRageRender.MyFlareRenderer");
        private static readonly FieldInfo poolField=AccessTools.Field(AccessTools.TypeByName("VRageRender.MyBillboardRenderer"),"m_billboardsOncePool");
        private static readonly MethodInfo draw=native.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="Draw" && m.GetParameters().Length==5);
        private static int Count(object pool) => (int)AccessTools.Method(pool.GetType(),"GetAllocatedCount").Invoke(pool,null);
        private static void AddFlare()
        {
            if(flare==null) return;
            object pool=poolField.GetValue(null); int count=Count(pool);
            draw.Invoke(null,new[] {flare,(object)position,Vector3D.Forward,Vector4.One,1f});
            if(Count(pool)!=count+1) throw new Exception("Expected one generated flare");
            var board=(MyBillboard)AccessTools.Method(pool.GetType(),"GetAllocatedItem").Invoke(pool,new object[] {count});
            corners=new[] {board.Position0,board.Position1,board.Position2,board.Position3};
        }
        internal static void RunNative(BorrowedRtvTexture target,Action<string> log)
        {
            var camera=MyRender11.Environment_Matrices; var savedCamera=camera.Capture();
            var active=AccessTools.Field(typeof(StereoRenderState),"<Active>k__BackingField");
            var center=AccessTools.Field(typeof(StereoRenderState),"<CenterView>k__BackingField");
            var projection=AccessTools.Field(typeof(StereoRenderState),"<CenterProjection>k__BackingField");
            object oldActive=active.GetValue(null),oldCenter=center.GetValue(null),oldProjection=projection.GetValue(null);
            int oldView=StereoRenderState.View;
            bool mask=Common.Config.HiddenAreaMask,shadows=Common.Config.StableShadows;
            object pool=poolField.GetValue(null); int originalCount=Count(pool),initial=originalCount;
            var collection=native.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).Single(m=>m.Name=="Draw" && m.GetParameters().Length==2 && m.GetParameters()[0].ParameterType.IsGenericType);
            var harmony=new Harmony("SpaceEngineersVR.StereoFlareFixture");
            try
            {
                var baseline=(MyBillboard)AccessTools.Method(AccessTools.TypeByName("VRageRender.MyBillboardRenderer"),"AddBillboardOnce").Invoke(null,null);
                baseline.Material=MyStringId.GetOrCompute("SunDisk"); baseline.Color=Vector4.Zero;
                baseline.Position0=baseline.Position1=baseline.Position2=baseline.Position3=camera.CameraPosition;
                initial=Count(pool);
                var description=new MyFlareDesc {Enabled=true,MaxDistance=100000,Intensity=1,SizeMultiplier=Vector2.One,
                    Glares=new[] {new MySubGlare {Material=MyStringId.GetOrCompute("SunDisk"),Color=Vector4.One,FixedSize=true,Size=new Vector2(.05f),
                        Type=SubGlareType.Rotated,ScreenIntensityMultiplierCenter=1,ScreenIntensityMultiplierEdge=1}}};
                var nullId=AccessTools.Field(draw.GetParameters()[0].ParameterType,"NULL").GetValue(null);
                flare=AccessTools.Method(native,"Set").Invoke(null,new[] {nullId,null,(object)description});
                object manager=AccessTools.Field(AccessTools.TypeByName("VRage.Render11.Common.MyManagers"),"FlareOcclusionRenderer").GetValue(null);
                var queries=(System.Collections.IDictionary)AccessTools.Field(manager.GetType(),"m_queries").GetValue(manager);
                object query=queries[flare]; AccessTools.Field(query.GetType(),"OcclusionFactor").SetValue(query,0f);
                position=camera.CameraPosition+MatrixD.Invert(camera.ViewD).Forward*5;
                Common.Config.HiddenAreaMask=Common.Config.StableShadows=false;
                active.SetValue(null,true); center.SetValue(null,camera.ViewD); projection.SetValue(null,camera.Projection);
                harmony.Patch(collection,postfix:new HarmonyMethod(typeof(StereoFlareTests),nameof(AddFlare)));
                StereoRenderState.View=0; Draw(target); var left=corners;
                Require(Count(pool)==initial,"Left flare leaked out of its scene");
                camera.CameraPosition+=new Vector3D(.064,0,0);
                StereoRenderState.View=1; Draw(target);
                Require(Count(pool)==initial,"Right eye accumulated flare geometry");
                for(int i=0;i<4;i++) Require(Vector3D.Distance(left[i],corners[i])<1e-7,"Eye flare world corners disagree");
                using(var remote=new RemoteScene())
                {
                    camera.CameraPosition+=new Vector3D(30,0,0); Draw(target);
                    Require(Count(pool)==initial,"Remote flare leaked into physical scene");
                }
                camera.Restore(savedCamera); StereoRenderState.View=0; Draw(target);
                for(int i=0;i<4;i++) Require(Vector3D.Distance(left[i],corners[i])<1e-7,"Camera changed next physical flare geometry");
                Require(ReferenceEquals(baseline,AccessTools.Method(pool.GetType(),"GetAllocatedItem").Invoke(pool,new object[] {originalCount})),"Scene cleanup replaced a simulation billboard");
                log("PASS native stereo flares: one generated quad per scene, identical world corners in both eyes, camera geometry isolation and no pool accumulation");
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                if(flare!=null) AccessTools.Method(native,"Remove").Invoke(null,new[] {flare});
                flare=null; corners=null;
                AccessTools.Field(pool.GetType(),"m_nextAllocateIndex").SetValue(pool,originalCount);
                camera.Restore(savedCamera); active.SetValue(null,oldActive); center.SetValue(null,oldCenter); projection.SetValue(null,oldProjection); StereoRenderState.View=oldView;
                Common.Config.HiddenAreaMask=mask; Common.Config.StableShadows=shadows;
            }
        }
        private static void Draw(BorrowedRtvTexture target)
        { object ao=null; try { MyRender11.DrawGameScene(target,out ao); } finally { if(ao!=null) new BorrowedRtvTexture(ao).Release(); } }
        private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    }
}
