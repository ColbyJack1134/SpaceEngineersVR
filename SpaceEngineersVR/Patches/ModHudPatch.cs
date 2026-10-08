using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    internal static class ModHud
    {
        private const double Reach=.3,Depth=10;
        [ThreadStatic] private static int drawing;
        private static readonly List<MethodBase> patched=new List<MethodBase>();
        private static readonly HashSet<Type> attempted=new HashSet<Type>();
        private static readonly Dictionary<Type,RichPool> richPools=new Dictionary<Type,RichPool>();
        private static DateTime retry;
        private sealed class RichPool
        {
            internal readonly FieldInfo Triangles,FlatTriangles,Pool,FlatPool;
            internal RichPool(Type type)
            {
                Triangles=Field(type,"triangleList",false); FlatTriangles=Field(type,"flatTriangleList",false);
                Pool=Field(type,"triPoolBack",true); FlatPool=Field(type,"flatTriPoolBack",true);
            }
            private static FieldInfo Field(Type type,string name,bool pool)
            {
                var field=AccessTools.Field(type,name);
                if(field==null || (pool ? field.FieldType!=typeof(List<MyTriangleBillboard>[]):!typeof(IList).IsAssignableFrom(field.FieldType)))
                    throw new MissingFieldException(type.FullName,name);
                return field;
            }
            internal void Place(object instance)
            {
                PlacePool(instance,Triangles,Pool); PlacePool(instance,FlatTriangles,FlatPool);
            }
            private static void PlacePool(object instance,FieldInfo data,FieldInfo pool)
            {
                var values=((List<MyTriangleBillboard>[])pool.GetValue(instance))[0];
                int count=Math.Min(((IList)data.GetValue(instance)).Count,values?.Count??0);
                for(int i=0;i<count;i++) if(!PlaceKnown(values[i])) values[i].Color=Vector4.Zero;
            }
        }
        internal static void RefreshRegistrations()
        {
            if(!Main.VrActive || MySession.Static==null || DateTime.UtcNow<retry) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies().Reverse())
            {
                var text=assembly.GetType("UIFun.ModUI",false);
                if(text!=null && AccessTools.Field(text,"instance")?.GetValue(null)!=null) AttachText(assembly,text);
                var rich=assembly.GetType("RichHudFramework.UI.Rendering.BillBoardUtils",false);
                if(rich!=null && AccessTools.Field(rich,"instance")?.GetValue(null)!=null) AttachRich(rich);
            }
        }
        private static void Patch(MethodBase method,string prefix=null,string postfix=null,string finalizer=null)
        {
            Common.Plugin.Harmony.Patch(method,prefix==null ? null:new HarmonyMethod(typeof(ModHud),prefix),
                postfix==null ? null:new HarmonyMethod(typeof(ModHud),postfix),
                finalizer:finalizer==null ? null:new HarmonyMethod(typeof(ModHud),finalizer));
            patched.Add(method);
        }
        private static void AttachText(Assembly assembly,Type root)
        {
            if(!attempted.Add(root)) return;
            int start=patched.Count;
            try
            {
                var methods=new[] {"ModHUDMessage","ModBillboardHUDMessage","ModBillboardTriHUDMessage"}
                    .Select(name=>assembly.GetType("UIFun.Messagesv2."+name,true).GetMethod("Draw",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null)).ToArray();
                if(methods.Any(method=>method==null)) throw new MissingMethodException("Text HUD API drawing contract changed");
                foreach(var method in methods) Patch(method,nameof(Begin),finalizer:nameof(End));
                Logger.Info("Text HUD API VR billboard adapter attached to "+assembly.GetName().Name);
            }
            catch(Exception ex) { Rollback(start); Logger.Warning(ex,"Text HUD API VR adapter unavailable"); }
        }
        private static void AttachRich(Type type)
        {
            if(!attempted.Add(type)) return;
            int start=patched.Count;
            try
            {
                var pools=new RichPool(type);
                var method=type.GetMethod("UpdateBillboards",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null);
                if(method==null) throw new MissingMethodException(type.FullName,"UpdateBillboards");
                richPools.Add(type,pools);
                Patch(method,postfix:nameof(RichUpdated));
                Logger.Info("Rich HUD Framework VR billboard adapter attached to "+type.Assembly.GetName().Name);
            }
            catch(Exception ex) { richPools.Remove(type); Rollback(start); Logger.Warning(ex,"Rich HUD Framework VR adapter unavailable"); }
        }
        private static void Rollback(int start)
        {
            foreach(var method in patched.Skip(start).ToArray())
                foreach(string name in new[] {nameof(Begin),nameof(End),nameof(RichUpdated)})
                    Common.Plugin.Harmony.Unpatch(method,AccessTools.Method(typeof(ModHud),name));
            patched.RemoveRange(start,patched.Count-start);
        }
        internal static void Reset()
        {
            Rollback(0); attempted.Clear(); richPools.Clear(); drawing=0; retry=DateTime.MinValue;
        }
        private static void Begin(out int __state) { __state=drawing; if(Main.VrActive) drawing++; }
        private static Exception End(Exception __exception,int __state) { drawing=__state; return __exception; }
        private static void RichUpdated(object __instance)
        {
            if(!Main.VrActive || !richPools.TryGetValue(__instance.GetType(),out var pools)) return;
            // RHF submits pooled objects before its worker update fills their vertices.
            try { pools.Place(__instance); }
            catch(Exception ex) { richPools.Remove(__instance.GetType()); Logger.Warning(ex,"Rich HUD Framework VR billboard placement disabled"); }
        }
        internal static bool Place(MyBillboard billboard) => drawing==0 || PlaceKnown(billboard);
        private static bool PlaceKnown(MyBillboard billboard)
        {
            var camera=MySector.MainCamera;
            return !Main.VrActive || camera==null || Place(billboard,camera.Position,camera.ForwardVector,HelmetHud.Visible);
        }
        internal static bool Place(MyBillboard billboard,Vector3D origin,Vector3D forward,bool visible)
        {
            if(billboard==null || billboard.CustomViewProjection!=-1) return true;
            bool triangle=billboard is MyTriangleBillboard;
            double reach=Reach*Reach;
            if(Vector3D.DistanceSquared(billboard.Position0,origin)>reach || Vector3D.DistanceSquared(billboard.Position1,origin)>reach ||
                Vector3D.DistanceSquared(billboard.Position2,origin)>reach || (!triangle && Vector3D.DistanceSquared(billboard.Position3,origin)>reach)) return true;
            if(!visible) return false;
            var center=triangle ? (billboard.Position0+billboard.Position1+billboard.Position2)/3:(billboard.Position0+billboard.Position2)*.5;
            double near=Vector3D.Dot(center-origin,forward);
            if(near<.01) return true;
            // Keep each HUD vertex on its original view ray.
            double scale=Depth/near;
            billboard.Position0=origin+(billboard.Position0-origin)*scale;
            billboard.Position1=origin+(billboard.Position1-origin)*scale;
            billboard.Position2=origin+(billboard.Position2-origin)*scale;
            if(!triangle) billboard.Position3=origin+(billboard.Position3-origin)*scale;
            billboard.DistanceSquared=(float)Vector3D.DistanceSquared(origin+(center-origin)*scale,origin);
            return true;
        }
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.AddBillboard))]
    internal static class ModHudBillboardPatch
    {
        private static bool Prefix(MyBillboard billboard) => BuildOrientationHud.Capturing!=null || ModHud.Place(billboard);
    }
}
