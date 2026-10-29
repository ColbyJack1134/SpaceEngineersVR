using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using VRageMath;
using VRageRender;
using TriangleData=VRage.MyTuple<VRageRender.MyBillboard.BlendTypeEnum,VRageMath.Vector2I,VRage.Utils.MyStringId,VRageMath.Vector4,
    VRage.MyTuple<VRageMath.Vector2,VRageMath.Vector2,VRageMath.Vector2>,VRage.MyTuple<VRageMath.Vector3D,VRageMath.Vector3D,VRageMath.Vector3D>>;
using FlatTriangleData=VRage.MyTuple<VRageRender.MyBillboard.BlendTypeEnum,VRageMath.Vector2I,VRage.Utils.MyStringId,VRage.MyTuple<VRageMath.Vector4,VRageMath.BoundingBox2?>,
    VRage.MyTuple<VRageMath.Vector2,VRageMath.Vector2,VRageMath.Vector2>,VRage.MyTuple<VRageMath.Vector2,VRageMath.Vector2,VRageMath.Vector2>>;

namespace SpaceEngineersVR.Patches
{
    internal static class ModHud
    {
        private const double Reach=.3,Depth=10;
        [ThreadStatic] private static int drawing;
        [ThreadStatic] private static int suppressing;
        private static readonly Dictionary<MethodBase,Func<bool>> suppressWhile=new Dictionary<MethodBase,Func<bool>>();
        private static readonly List<MethodBase> patched=new List<MethodBase>();
        private static readonly HashSet<Type> attempted=new HashSet<Type>();
        private static readonly Dictionary<Type,RichPool> richPools=new Dictionary<Type,RichPool>();
        private static DateTime retry;
        private sealed class RichPool
        {
            internal readonly FieldInfo Triangles,FlatTriangles,Pool,FlatPool,Matrices,MatrixIds;
            private readonly HashSet<int> moved=new HashSet<int>(),movedFlat=new HashSet<int>(),handMatrices=new HashSet<int>();
            private bool reported;
            internal bool Valid => Triangles!=null && FlatTriangles!=null && Pool!=null && FlatPool!=null;
            internal RichPool(Type type)
            {
                Triangles=Field(type,"triangleList",false); FlatTriangles=Field(type,"flatTriangleList",false);
                Pool=Field(type,"triPoolBack",true); FlatPool=Field(type,"flatTriPoolBack",true);
                Matrices=AccessTools.Field(type,"matrixBuf"); MatrixIds=AccessTools.Field(type,"matrixTable");
            }
            private static FieldInfo Field(Type type,string name,bool pool)
            {
                var field=AccessTools.Field(type,name);
                if(field==null || (pool ? field.FieldType!=typeof(List<MyTriangleBillboard>[]):!typeof(IList).IsAssignableFrom(field.FieldType)))
                    return null;
                return field;
            }
            internal void Place(object instance)
            {
                moved.Clear(); movedFlat.Clear(); handMatrices.Clear();
                // Build Vision labels use per-label copies of its plane matrix translated within the plane; match by basis and position.
                if(BuildVisionBridge.TryHandPlane(out var planes,out var center,out float reach,out var transform) && Matrices?.GetValue(instance) is List<MatrixD> matrices &&
                    MatrixIds?.GetValue(instance) is Dictionary<MatrixD[],int> ids)
                {
                    if(ids.TryGetValue(planes,out int own)) handMatrices.Add(own);
                    var plane=planes[0];
                    var inverse=MatrixD.Invert(plane);
                    for(int i=0;i<matrices.Count;i++)
                    {
                        var matrix=matrices[i];
                        if(matrix.Right!=plane.Right || matrix.Up!=plane.Up || matrix.Backward!=plane.Backward) continue;
                        var local=Vector3D.Transform(matrix.Translation,inverse);
                        if(Math.Abs(local.Z)<1e-3 && new Vector2D(local.X-center.X,local.Y-center.Y).Length()<=reach) handMatrices.Add(i);
                    }
                    if(handMatrices.Count>0 && Triangles.GetValue(instance) is List<TriangleData> triangles && FlatTriangles.GetValue(instance) is List<FlatTriangleData> flat)
                    {
                        Hand(triangles,entry=>entry.Item2,handMatrices,((List<MyTriangleBillboard>[])Pool.GetValue(instance))[0],moved,ref transform);
                        Hand(flat,entry=>entry.Item2,handMatrices,((List<MyTriangleBillboard>[])FlatPool.GetValue(instance))[0],movedFlat,ref transform);
                    }
                    else if(handMatrices.Count>0 && !reported) { reported=true; Logger.Info("Rich HUD Master billboard data changed; Build Vision stays in the head-locked HUD"); }
                    BuildVisionBridge.DrawPageDots();
                }
                PlacePool(instance,Triangles,Pool,moved); PlacePool(instance,FlatTriangles,FlatPool,movedFlat);
            }
            private static void Hand<T>(List<T> data,Func<T,Vector2I> ids,HashSet<int> matrices,List<MyTriangleBillboard> values,HashSet<int> moved,ref MatrixD transform)
            {
                for(int i=0;i<data.Count;i++)
                {
                    var id=ids(data[i]);
                    if(!matrices.Contains(id.Y) || id.X<0 || id.X>=(values?.Count ?? 0)) continue;
                    Move(values[id.X],ref transform);
                    moved.Add(id.X);
                }
            }
            private static void PlacePool(object instance,FieldInfo data,FieldInfo pool,HashSet<int> moved)
            {
                var values=((List<MyTriangleBillboard>[])pool.GetValue(instance))[0];
                int count=Math.Min(((IList)data.GetValue(instance)).Count,values?.Count??0);
                for(int i=0;i<count;i++) if(!moved.Contains(i) && !PlaceKnown(values[i])) values[i].Color=Vector4.Zero;
            }
        }
        internal static void RefreshRegistrations()
        {
            if(!Main.VrActive || MySession.Static==null || DateTime.UtcNow<retry) return;
            retry=DateTime.UtcNow.AddSeconds(1);
            foreach(var assembly in Util.Util.NewestAssemblies())
            {
                var text=assembly.GetType("UIFun.ModUI",false);
                if(text!=null && AccessTools.Field(text,"instance")?.GetValue(null)!=null) AttachText(assembly,text);
                var rich=assembly.GetType("RichHudFramework.UI.Rendering.BillBoardUtils",false);
                if(rich!=null && AccessTools.Field(rich,"instance")?.GetValue(null)!=null) AttachRich(rich);
                var weaponCore=assembly.GetType("CoreSystems.Session",false);
                if(weaponCore!=null && WeaponCoreBridge.ApiReady && ModReflection.StaticReader(weaponCore,"I")?.Invoke()!=null)
                    Suppress("WeaponCore",()=>WeaponCoreBridge.ApiReady,weaponCore,
                        assembly.GetType("WeaponCore.Data.Scripts.CoreSystems.Ui.Targeting.TargetUi",false)?.GetMethod("DrawTargetUi",Declared,null,Type.EmptyTypes,null),
                        assembly.GetType("WeaponCore.Data.Scripts.CoreSystems.Ui.Hud.Hud",false)?.GetMethod("DrawTextures",Declared,null,Type.EmptyTypes,null),
                        assembly.GetType("WeaponCore.Data.Scripts.CoreSystems.Ui.Hud.Hud",false)?.GetMethod("DrawText",Declared,null,Type.EmptyTypes,null));
                var shields=assembly.GetType("DefenseShields.Session",false);
                if(shields!=null && ShieldsBridge.Ready && ModReflection.StaticReader(shields,"Instance")?.Invoke()!=null)
                    Suppress("Defense Shields",()=>ShieldsBridge.Ready,shields,
                        assembly.GetType("DefenseShields.DefenseShields",false)?.GetMethod("UpdateIcon",Declared,null,new[] {typeof(bool)},null),
                        shields.GetMethod("DrawText",Declared,null,Type.EmptyTypes,null));
            }
        }
        private const BindingFlags Declared=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly;
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
                    .Select(name=>assembly.GetType("UIFun.Messagesv2."+name,false)?.GetMethod("Draw",BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null)).ToArray();
                if(methods.Any(method=>method==null)) return;
                foreach(var method in methods) Patch(method,nameof(Begin),finalizer:nameof(End));
                Logger.Info("Text HUD API VR billboard adapter attached to "+assembly.GetName().Name);
            }
            catch(Exception ex) { Rollback(start); Logger.Warning(ex,"Text HUD API VR adapter unavailable"); }
        }
        // WeaponCore and Defense Shields draw their HUD 10 cm in front of the camera, which cannot be right in both eyes. The
        // headset hides it once the mod's API answered and the VR markers and wrist pages replace it; camera feeds keep it.
        private static void Suppress(string mod,Func<bool> ready,Type session,params MethodBase[] methods)
        {
            if(!attempted.Add(session)) return;
            int start=patched.Count;
            try
            {
                if(methods.Any(method=>method==null)) { Logger.Info(mod+" HUD draw methods not found; its HUD is left unchanged in VR"); return; }
                foreach(var method in methods) { Patch(method,nameof(BeginSuppressed),finalizer:nameof(EndSuppressed)); suppressWhile[method]=ready; }
                Logger.Info(mod+" HUD hidden in the headset");
            }
            catch(Exception ex) { Rollback(start); Logger.Warning(ex,mod+" VR HUD adapter unavailable"); }
        }
        private static void AttachRich(Type type)
        {
            if(!attempted.Add(type)) return;
            int start=patched.Count;
            try
            {
                var method=type.GetMethod("UpdateBillboards",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly,null,Type.EmptyTypes,null);
                if(method==null) return;
                var pools=new RichPool(type);
                if(!pools.Valid) return;
                richPools.Add(type,pools);
                Patch(method,postfix:nameof(RichUpdated));
                Logger.Info("Rich HUD Framework VR billboard adapter attached to "+type.Assembly.GetName().Name);
            }
            catch(Exception ex) { richPools.Remove(type); Rollback(start); Logger.Warning(ex,"Rich HUD Framework VR adapter unavailable"); }
        }
        private static void Rollback(int start)
        {
            foreach(var method in patched.Skip(start).ToArray())
                foreach(string name in new[] {nameof(Begin),nameof(BeginSuppressed),nameof(End),nameof(EndSuppressed),nameof(RichUpdated)})
                    Common.Plugin.Harmony.Unpatch(method,AccessTools.Method(typeof(ModHud),name));
            patched.RemoveRange(start,patched.Count-start);
        }
        internal static void Reset()
        {
            Rollback(0); attempted.Clear(); richPools.Clear(); suppressWhile.Clear(); drawing=0; suppressing=0; retry=DateTime.MinValue;
            MenuHudLayer.Reset();
        }
        private static void Begin(out int __state) { __state=drawing; if(Main.VrActive) drawing++; }
        // Hidden only while the mod's VR replacement is live; a failed integration shows the mod's own HUD again.
        private static void BeginSuppressed(MethodBase __originalMethod,out int __state)
        {
            __state=suppressing;
            if(Main.VrActive && RemoteView.Current==null && suppressWhile.TryGetValue(__originalMethod,out var ready) && ready()) suppressing++;
        }
        private static Exception EndSuppressed(Exception __exception,int __state) { suppressing=__state; return __exception; }
        private static Exception End(Exception __exception,int __state) { drawing=__state; return __exception; }
        private static void RichUpdated(object __instance)
        {
            if(!Main.VrActive || !richPools.TryGetValue(__instance.GetType(),out var pools)) return;
            // RHF submits pooled objects before its worker update fills their vertices.
            try { pools.Place(__instance); }
            catch(Exception ex) { richPools.Remove(__instance.GetType()); Logger.Warning(ex,"Rich HUD Framework VR billboard placement disabled"); }
        }
        private static void Move(MyTriangleBillboard billboard,ref MatrixD transform)
        {
            billboard.Position0=Vector3D.Transform(billboard.Position0,transform);
            billboard.Position1=Vector3D.Transform(billboard.Position1,transform);
            billboard.Position2=Vector3D.Transform(billboard.Position2,transform);
            var camera=MySector.MainCamera;
            if(camera!=null) billboard.DistanceSquared=(float)Vector3D.DistanceSquared((billboard.Position0+billboard.Position1+billboard.Position2)/3,camera.Position);
        }
        internal static bool Place(MyBillboard billboard) => suppressing==0 && (drawing==0 || PlaceKnown(billboard));
        private static bool PlaceKnown(MyBillboard billboard)
        {
            var camera=MySector.MainCamera;
            if(!Main.VrActive || camera==null) return true;
            if(MenuHudLayer.Active && Near(billboard,camera.Position) && MenuHudLayer.Add(billboard)) return false;
            return Place(billboard,camera.Position,camera.ForwardVector,HelmetHud.HudAlpha);
        }
        private static bool Near(MyBillboard billboard,Vector3D origin)
        {
            double reach=Reach*Reach;
            return billboard!=null && billboard.CustomViewProjection==-1 && Vector3D.DistanceSquared(billboard.Position0,origin)<=reach &&
                Vector3D.DistanceSquared(billboard.Position1,origin)<=reach && Vector3D.DistanceSquared(billboard.Position2,origin)<=reach &&
                (billboard is MyTriangleBillboard || Vector3D.DistanceSquared(billboard.Position3,origin)<=reach);
        }
        internal static bool Place(MyBillboard billboard,Vector3D origin,Vector3D forward,float alpha)
        {
            if(!Near(billboard,origin)) return true;
            bool triangle=billboard is MyTriangleBillboard;
            if(alpha<=0) return false;
            if(alpha<1) billboard.Color*=alpha;
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
        private static bool Prefix(MyBillboard billboard)
        {
            // Before ModHud.Place, which moves the vertices and fades the color.
            var buildInfo=BuildInfoPanel.Capturing;
            if(buildInfo!=null) { buildInfo.Add(billboard); return false; }
            if(CardAdditions.Capturing) { CardAdditions.Add(billboard); return false; }
            if(BuildInfoBridge.SeeThrough(billboard)) return true;
            return BuildOrientationHud.Capturing!=null || (ModHud.Place(billboard) && PaintGunPatches.Keep(billboard));
        }
    }
}
