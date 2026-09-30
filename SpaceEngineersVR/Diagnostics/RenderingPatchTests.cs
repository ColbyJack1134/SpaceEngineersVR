using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SpaceEngineersVR.Patches;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class RenderingPatchTests
    {
        public static void Run(Action<string> log)
        {
            Assembly.Load("VRage.Render11");
            var renderer=AccessTools.TypeByName("VRageRender.MyRender11");
            var scene=AccessTools.Method(renderer,"DrawGameScene");
            var debugScene=AccessTools.Method(renderer,"DrawDebugScene");
            if(debugScene==null || debugScene.GetParameters().Length!=1 ||
                debugScene.GetParameters()[0].ParameterType!=scene.GetParameters()[1].ParameterType.GetElementType())
                throw new Exception("Native debug-scene cleanup signature changed");
            foreach(var patch in new[] {typeof(StereoFlareQueries),typeof(StereoFlareVisibility)})
            {
                var method=(MethodBase)AccessTools.Method(patch,"TargetMethod").Invoke(null,null);
                var original=PatchProcessor.GetOriginalInstructions(method).ToList();
                var transformed=((IEnumerable<CodeInstruction>)AccessTools.Method(patch,"Transpiler").Invoke(null,new object[] {original})).ToList();
                if(transformed.Count(x=>x.opcode==OpCodes.Call && (x.operand as MethodInfo)?.DeclaringType==patch)!=1)
                    throw new Exception("Stereo flare patch did not replace exactly one installed engine instruction");
            }
            var data=AccessTools.TypeByName("VRage.Render11.Culling.Occlusion.MyFlareOcclusionData");
            foreach(string name in new[] {"LastVolumeSquared","Size","Shift","OcclusionFactor"}) Field(data,name,typeof(float));
            Field(data,"Position",typeof(Vector3D));
            var cascades=AccessTools.TypeByName("VRageRender.MyShadowCascades");
            var state=AccessTools.Field(cascades,"m_cascadeStates")?.FieldType.GetElementType();
            if(state==null || AccessTools.Field(state,"Info")==null) throw new Exception("Cascade snapshot layout changed");
            Field(cascades,"m_shadowCascadeSplitDepths",typeof(float[]));
            if(AccessTools.Property(cascades,"Enabled")?.PropertyType!=typeof(bool)) throw new Exception("Cascade enabled flag changed");
            var prepare=AccessTools.Method(cascades,"PrepareQueries");
            var fill=AccessTools.Method(cascades,"FillConstantBuffer");
            if(prepare==null || prepare.GetParameters().Length!=2 || prepare.GetParameters()[0].Name!="rc" ||
                fill==null || fill.GetParameters().Length!=2 || fill.GetParameters()[0].ParameterType!=prepare.GetParameters()[0].ParameterType)
                throw new Exception("Shadow context signature changed");
            foreach(string name in new[] {"m_csmConstants","m_csmConstants2"}) Field(cascades,name,fill.GetParameters()[1].ParameterType);
            if(AccessTools.Field(cascades,"m_validCascades")==null || AccessTools.Method(cascades,"Gather")==null)
                throw new Exception("Shadow history layout changed");
            log("PASS installed rendering contracts: both flare IL replacements, query fields and shadow history/context signatures");
        }
        private static void Field(Type type,string name,Type expected)
        { if(AccessTools.Field(type,name)?.FieldType!=expected) throw new Exception(type+"."+name+" changed type"); }
    }
}
