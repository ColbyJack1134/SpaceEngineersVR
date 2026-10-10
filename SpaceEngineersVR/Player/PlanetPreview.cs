using System;
using System.Collections.Generic;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class PlanetPreview
    {
        [ThreadStatic] internal static bool Capturing;
        private static readonly List<MyRenderMessageDebugDrawSphere> pending=new List<MyRenderMessageDebugDrawSphere>();
        internal static MyRenderMessageDebugDrawSphere[] Current { get; private set; }=Array.Empty<MyRenderMessageDebugDrawSphere>();
        internal static void Begin() { pending.Clear(); Current=Array.Empty<MyRenderMessageDebugDrawSphere>(); }
        internal static void Add(MyRenderMessageDebugDrawSphere sphere) => pending.Add(sphere);
        internal static void Commit() => Current=pending.ToArray();
    }
}
