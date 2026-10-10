using System;
using System.Collections.Generic;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class SymmetryPlanes
    {
        internal sealed class View
        {
            internal readonly Vector3D A,B,C;
            internal readonly Color Color;
            internal View(Vector3D a,Vector3D b,Vector3D c,Color color) { A=a; B=b; C=c; Color=color; }
        }
        [ThreadStatic] internal static bool Capturing;
        private static readonly List<View> pending=new List<View>();
        internal static View[] Current { get; private set; }=Array.Empty<View>();
        internal static void Begin() { pending.Clear(); Current=Array.Empty<View>(); }
        internal static void Add(View triangle) => pending.Add(triangle);
        internal static void Commit() => Current=pending.ToArray();
        internal static void Draw(object target,View[] planes) => NativeGizmos.Draw(target,planes);
    }
}
