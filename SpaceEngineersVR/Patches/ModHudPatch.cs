using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Game.World;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Patches
{
    // Mod HUD frameworks draw a camera-facing plane about 0.1 m from the camera, too close for the eyes to fuse.
    internal static class ModHud
    {
        private const double Reach=.3,Depth=10;
        internal static bool Place(MyBillboard billboard)
        {
            var camera=MySector.MainCamera;
            if(!Main.VrActive || camera==null || billboard==null || billboard.CustomViewProjection!=-1) return true;
            var origin=camera.Position;
            double reach=Reach*Reach;
            if(Vector3D.DistanceSquared(billboard.Position0,origin)>reach || Vector3D.DistanceSquared(billboard.Position1,origin)>reach ||
                Vector3D.DistanceSquared(billboard.Position2,origin)>reach || Vector3D.DistanceSquared(billboard.Position3,origin)>reach) return true;
            if(!HelmetHud.Visible) return false;
            double near=Vector3D.Dot((billboard.Position0+billboard.Position2)*.5-origin,camera.ForwardVector);
            if(near<.01) return true;
            // Scaling about the camera keeps every element on its original view ray, so world markers stay aligned.
            double scale=Depth/near;
            billboard.Position0=origin+(billboard.Position0-origin)*scale;
            billboard.Position1=origin+(billboard.Position1-origin)*scale;
            billboard.Position2=origin+(billboard.Position2-origin)*scale;
            billboard.Position3=origin+(billboard.Position3-origin)*scale;
            billboard.DistanceSquared=(float)Vector3D.DistanceSquared((billboard.Position0+billboard.Position2)*.5,origin);
            return true;
        }
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.AddBillboard))]
    internal static class ModHudBillboardPatch
    {
        private static bool Prefix(MyBillboard billboard) => BuildOrientationHud.Capturing!=null || ModHud.Place(billboard);
    }
    [HarmonyPatch(typeof(MyRenderProxy),nameof(MyRenderProxy.AddBillboards))]
    internal static class ModHudBillboardsPatch
    {
        private static void Prefix(IEnumerable<MyBillboard> billboards)
        {
            if(!Main.VrActive) return;
            foreach(var billboard in billboards)
                if(!ModHud.Place(billboard)) billboard.Color=Vector4.Zero;
        }
    }
}
