using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class BodyProximityPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("VRage.Render11.Scene.Components.MyRenderableComponent"),
            "UpdateProxiesCustomAlpha",new[] {typeof(Vector2),typeof(Vector2)});
        private static void Postfix(object __instance)
        {
            if(!BodyProximity.Fading || BodyProximity.Actor==uint.MaxValue || Convert.ToUInt32(CockpitRender.Member(CockpitRender.Member(__instance,"Owner"),"ID"))!=BodyProximity.Actor) return;
            foreach(object lod in (IEnumerable)CockpitRender.Member(__instance,"Lods"))
            {
                var proxies=CockpitRender.Member(lod,"RenderableProxies") as IEnumerable;
                if(proxies==null) continue;
                foreach(object proxy in proxies)
                {
                    string material=CockpitRender.Member(CockpitRender.Member(CockpitRender.Member(proxy,"Material"),"Info"),"Name").ToString();
                    if(material!="LeftGlove" && material!="RightGlove") continue;
                    var field=AccessTools.Field(proxy.GetType(),"CommonObjectData");
                    var data=field.GetValue(proxy);
                    var alpha=AccessTools.Field(data.GetType(),"CustomAlpha");
                    var value=(Vector2)alpha.GetValue(data); value.Y=0;
                    alpha.SetValue(data,value); field.SetValue(proxy,data);
                }
            }
        }
    }
}
