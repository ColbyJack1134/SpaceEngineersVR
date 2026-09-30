using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Wrappers;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class StereoShadowPatch
    {
        private static readonly Type type=AccessTools.TypeByName("VRageRender.MyShadowCascades");
        private static readonly FieldInfo states=AccessTools.Field(type,"m_cascadeStates"),splits=AccessTools.Field(type,"m_shadowCascadeSplitDepths"),
            valid=AccessTools.Field(type,"m_validCascades"),currentBuffer=AccessTools.Field(type,"m_csmConstants"),oldBuffer=AccessTools.Field(type,"m_csmConstants2");
        private static readonly MethodInfo fill=AccessTools.Method(type,"FillConstantBuffer"),clone=AccessTools.Method(typeof(object),"MemberwiseClone");
        private static readonly PropertyInfo enabled=AccessTools.Property(type,"Enabled");
        private static readonly ConditionalWeakTable<object,History> histories=new ConditionalWeakTable<object,History>();
        private sealed class History
        {
            public long Frame=-1;
            public object[] OldStates;
            public object OldSplits,OldValid;
        }
        internal sealed class Scope
        {
            public EnvironmentMatrices Camera;
            public Dictionary<FieldInfo,object> Snapshot;
            public void Restore() { if(Snapshot!=null) { Camera.Restore(Snapshot); Snapshot=null; } }
        }
        private static MethodBase TargetMethod() => AccessTools.Method(type,"PrepareQueries");
        private static bool Prefix(object __instance,object rc,ref Scope __state)
        {
            if(!StereoRenderState.Active || !Common.Config.StableShadows || !(bool)enabled.GetValue(__instance)) return true;
            var history=histories.GetValue(__instance,_=>new History());
            if(history.Frame==StereoRenderState.Frame)
            {
                Rebase(__instance,rc,history);
                StereoRenderState.Record("shadow_reuse");
                return false;
            }
            var array=(Array)states.GetValue(__instance);
            history.OldStates=new object[array.Length];
            for(int i=0;i<array.Length;i++)
            {
                var original=array.GetValue(i);
                var copy=clone.Invoke(original,null);
                var info=AccessTools.Field(original.GetType(),"Info");
                info.SetValue(copy,clone.Invoke(info.GetValue(original),null));
                history.OldStates[i]=copy;
            }
            history.OldSplits=((float[])splits.GetValue(__instance)).Clone();
            history.OldValid=valid.GetValue(__instance);
            history.Frame=StereoRenderState.Frame;
            var camera=MyRender11.Environment_Matrices;
            __state=new Scope { Camera=camera,Snapshot=camera.Capture() };
            MatrixD view=StereoRenderState.CenterView;
            camera.CameraPosition=MatrixD.Invert(view).Translation;
            view.Translation=Vector3D.Zero;
            camera.InvViewAt0=MatrixD.Invert(view);
            camera.Projection=StereoRenderState.ShadowProjection;
            camera.InvViewProjectionD=MatrixD.Invert(StereoRenderState.CenterView*(MatrixD)StereoRenderState.ShadowProjection);
            StereoRenderState.Record("shadow_update",camera.CameraPosition.X,camera.CameraPosition.Y,camera.CameraPosition.Z);
            return true;
        }
        private static void Postfix(object __instance,object rc,Scope __state)
        {
            if(__state==null) return;
            __state.Restore();
            Rebase(__instance,rc,histories.GetValue(__instance,_=>new History()));
        }
        private static Exception Finalizer(Exception __exception,Scope __state)
        {
            __state?.Restore();
            return __exception;
        }
        private static void Rebase(object instance,object rc,History history)
        {
            // Depth maps advance once per headset frame. Sampling constants still
            // need each eye's camera-relative translation, including the old map.
            fill.Invoke(instance,new[] {rc,currentBuffer.GetValue(instance)});
            var array=(Array)states.GetValue(instance);
            var now=new object[array.Length]; array.CopyTo(now,0);
            object nowSplits=splits.GetValue(instance),nowValid=valid.GetValue(instance);
            try
            {
                for(int i=0;i<array.Length;i++) array.SetValue(history.OldStates[i],i);
                splits.SetValue(instance,history.OldSplits); valid.SetValue(instance,history.OldValid);
                fill.Invoke(instance,new[] {rc,oldBuffer.GetValue(instance)});
            }
            finally
            {
                for(int i=0;i<array.Length;i++) array.SetValue(now[i],i);
                splits.SetValue(instance,nowSplits); valid.SetValue(instance,nowValid);
            }
        }
    }

    [HarmonyPatch]
    internal static class StereoShadowStatsPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRageRender.MyShadowCascades"),"Gather");
        private static bool Prefix() => !StereoRenderState.Active || !Common.Config.StableShadows || StereoRenderState.View==0;
    }
}
