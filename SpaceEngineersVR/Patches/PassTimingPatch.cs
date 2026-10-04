using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Patches
{
    // GPU timestamps around native scene steps. Installed with the other renderer patches; a missing method only skips its step.
    internal static class PassTimingPatch
    {
        internal static void Install(Harmony harmony)
        {
            int installed=0;
            void Patch(string type,string method,string prefix,string postfix)
            {
                var target=AccessTools.Method(AccessTools.TypeByName(type),method);
                if(target==null) { Logger.Warning("GPU pass timing unavailable for "+type+"."+method); return; }
                harmony.Patch(target,prefix==null ? null:new HarmonyMethod(typeof(PassTimingPatch),prefix),postfix==null ? null:new HarmonyMethod(typeof(PassTimingPatch),postfix));
                installed++;
            }
            Patch("VRageRender.MyRender11","PrepareGameScene",nameof(BeginPrepare),nameof(EndPrepare));
            // Native Done submits the old geometry renderer, then the new one; together they hold shadow maps and the G-buffer.
            Patch("VRageRender.MyGeometryRendererOld","DoneFrame",nameof(BeginGeometry),null);
            Patch("VRage.Render11.GeometryStage2.Rendering.MyGeometryRenderer","DoneFrame",null,nameof(EndGeometry));
            Patch("VRage.Render11.Culling.Occlusion.MyOcclusionTask","Consume",nameof(BeginOcclusion),nameof(EndOcclusion));
            Patch("VRage.Render11.GBufferResolve.MyGBufferResolver","ConsumeWork",nameof(BeginLighting),nameof(EndLighting));
            Patch("VRageRender.MyTransparentRendering","ConsumeWork",nameof(BeginTransparent),nameof(EndTransparent));
            Patch("VRageRender.MyEyeAdaptation","Run",nameof(BeginAdaptation),nameof(EndAdaptation));
            Patch("VRageRender.MyModernBloom","Run",nameof(BeginBloom),nameof(EndBloom));
            Patch("VRageRender.MyToneMapping","Run",nameof(BeginToneMap),nameof(EndToneMap));
            Logger.Info("GPU pass timing attached to "+installed+" native methods");
        }
        private static void BeginPrepare() => GpuTiming.Begin(GpuTiming.Pass.Prepare);
        private static void EndPrepare() => GpuTiming.End(GpuTiming.Pass.Prepare);
        private static void BeginGeometry() => GpuTiming.Begin(GpuTiming.Pass.Geometry);
        private static void EndGeometry() => GpuTiming.End(GpuTiming.Pass.Geometry);
        private static void BeginOcclusion() => GpuTiming.Begin(GpuTiming.Pass.Occlusion);
        private static void EndOcclusion() => GpuTiming.End(GpuTiming.Pass.Occlusion);
        private static void BeginLighting() => GpuTiming.Begin(GpuTiming.Pass.Lighting);
        private static void EndLighting() => GpuTiming.End(GpuTiming.Pass.Lighting);
        private static void BeginTransparent() => GpuTiming.Begin(GpuTiming.Pass.Transparent);
        private static void EndTransparent() => GpuTiming.End(GpuTiming.Pass.Transparent);
        private static void BeginAdaptation() => GpuTiming.Begin(GpuTiming.Pass.Adaptation);
        private static void EndAdaptation() => GpuTiming.End(GpuTiming.Pass.Adaptation);
        private static void BeginBloom() => GpuTiming.Begin(GpuTiming.Pass.Bloom);
        private static void EndBloom() => GpuTiming.End(GpuTiming.Pass.Bloom);
        private static void BeginToneMap() => GpuTiming.Begin(GpuTiming.Pass.ToneMap);
        private static void EndToneMap() => GpuTiming.End(GpuTiming.Pass.ToneMap);
    }
}
