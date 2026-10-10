using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Wrappers;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class NativeGizmos
    {
        private static readonly Type primitives=AccessTools.TypeByName("VRageRender.MyPrimitivesRenderer"),lines=AccessTools.TypeByName("VRageRender.MyLinesRenderer");
        private static readonly FieldInfo[] buffers={AccessTools.Field(primitives,"m_vertexList"),AccessTools.Field(primitives,"m_debugMeshes"),
            AccessTools.Field(lines,"m_vertices"),AccessTools.Field(lines,"m_batches")};
        private static readonly object[] scratch=new object[buffers.Length];
        private static readonly MethodInfo process=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"ProcessDebugMessages",new[] {typeof(List<MyRenderMessageBase>)}),
            drawPrimitives=AccessTools.Method(primitives,"Draw"),drawLines=AccessTools.Method(lines,"Draw"),clearLines=AccessTools.Method(lines,"Clear");
        internal static void Draw(object target,SymmetryPlanes.View[] planes,MyRenderMessageDebugDrawSphere[] spheres=null)
        {
            if((planes==null || planes.Length==0) && (spheres==null || spheres.Length==0)) return;
            var messages=new List<MyRenderMessageBase>();
            if(planes!=null) foreach(var plane in planes) messages.Add(new MyRenderMessageDebugDrawTriangle {
                Vertex0=plane.A,Vertex1=plane.B,Vertex2=plane.C,Color=plane.Color,DepthRead=true});
            if(spheres!=null) messages.AddRange(spheres);
            var saved=new object[buffers.Length];
            for(int i=0;i<buffers.Length;i++)
            {
                saved[i]=buffers[i].GetValue(null);
                if(scratch[i]==null) scratch[i]=i==0 || i==2 ? Activator.CreateInstance(buffers[i].FieldType,new object[] {1024}):Activator.CreateInstance(buffers[i].FieldType);
            }
            for(int i=0;i<buffers.Length;i++) buffers[i].SetValue(null,scratch[i]);
            try
            {
                // Run native gameplay gizmos through the native processors using each eye's camera and depth.
                process.Invoke(null,new object[] {messages});
                drawPrimitives.Invoke(null,new object[] {target,MyRender11.Environment_Matrices.ViewProjectionAt0,true});
                drawLines.Invoke(null,new object[] {target,false});
            }
            finally
            {
                try
                {
                    clearLines.Invoke(null,null);
                    AccessTools.Method(buffers[0].FieldType,"ClearFast").Invoke(scratch[0],null);
                }
                finally { for(int i=0;i<buffers.Length;i++) buffers[i].SetValue(null,saved[i]); }
            }
        }
    }
}
