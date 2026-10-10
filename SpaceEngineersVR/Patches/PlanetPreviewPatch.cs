using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch]
    internal static class PlanetPreviewFramePatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("Sandbox.Game.Entities.MyVoxelClipboard"),"Update");
        private static void Prefix(bool ___m_planetMode,out bool __state)
        {
            __state=PlanetPreview.Capturing;
            PlanetPreview.Begin(); PlanetPreview.Capturing=Main.VrActive && ___m_planetMode;
        }
        private static void Postfix() => PlanetPreview.Commit();
        private static void Finalizer(bool __state) => PlanetPreview.Capturing=__state;
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.DebugDrawSphere),new[] {typeof(Vector3D),typeof(float),typeof(Color),typeof(float),typeof(bool),typeof(bool),typeof(bool),typeof(bool)})]
    internal static class PlanetPreviewSpherePatch
    {
        private static void Prefix(Vector3D position,float radius,Color color,float alpha,bool depthRead,bool smooth,bool cull)
        {
            if(PlanetPreview.Capturing) PlanetPreview.Add(new MyRenderMessageDebugDrawSphere {
                Position=position,Radius=radius,Color=color,Alpha=alpha,DepthRead=depthRead,Smooth=smooth,Cull=cull});
        }
    }
}
