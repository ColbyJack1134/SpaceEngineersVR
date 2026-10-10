using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    [HarmonyPatch(typeof(MyCubeBuilder),nameof(MyCubeBuilder.Draw))]
    internal static class SymmetryFramePatch
    {
        private static void Prefix() => SymmetryPlanes.Begin();
        private static void Postfix() => SymmetryPlanes.Commit();
    }
    [HarmonyPatch(typeof(MyCubeBuilder),"DrawSymmetryPlane")]
    internal static class SymmetryPlanePatch
    {
        private static void Prefix(out bool __state)
        {
            __state=SymmetryPlanes.Capturing;
            SymmetryPlanes.Capturing=__state || Main.VrActive;
        }
        private static void Finalizer(bool __state) => SymmetryPlanes.Capturing=__state;
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.DebugDrawTriangle))]
    internal static class SymmetryTrianglePatch
    {
        private static void Prefix(Vector3D vertex0,Vector3D vertex1,Vector3D vertex2,Color color)
        {
            if(SymmetryPlanes.Capturing) SymmetryPlanes.Add(new SymmetryPlanes.View(vertex0,vertex1,vertex2,color));
        }
    }
}
