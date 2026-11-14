using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SpaceEngineersVR.Wrappers;
using VRageRender.Messages;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class NativeGizmos
    {
        private static readonly Type primitives=AccessTools.TypeByName("VRageRender.MyPrimitivesRenderer"),lines=AccessTools.TypeByName("VRageRender.MyLinesRenderer");
        private static readonly FieldInfo[] buffers={AccessTools.Field(primitives,"m_vertexList"),AccessTools.Field(primitives,"m_debugMeshes"),
            AccessTools.Field(lines,"m_vertices"),AccessTools.Field(lines,"m_batches")};
        private static readonly MethodInfo process=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyRender11"),"ProcessDebugMessages",new[] {typeof(List<MyRenderMessageBase>)}),
            drawPrimitives=AccessTools.Method(primitives,"Draw"),drawLines=AccessTools.Method(lines,"Draw"),clearLines=AccessTools.Method(lines,"Clear");
        internal static void DrawEye(object target,SymmetryPlanes.View[] planes,MyRenderMessageDebugDrawSphere[] spheres,MatrixD view,MatrixD projection)
        {
            if((planes==null || planes.Length==0) && (spheres==null || spheres.Length==0)) return;
            var matrices=MyRender11.Environment_Matrices;
            var camera=matrices.CameraPosition;
            var viewProjection=matrices.ViewProjectionAt0;
            try
            {
                // Native debug triangles subtract CameraPosition; debug lines read ViewProjectionAt0.
                MatrixD atZero=view; atZero.Translation=Vector3D.Zero;
                matrices.CameraPosition=MatrixD.Invert(view).Translation;
                matrices.ViewProjectionAt0=(Matrix)(atZero*projection);
                Dlss.DrawNativeGizmos(() => Draw(target,planes,spheres));
            }
            finally { matrices.CameraPosition=camera; matrices.ViewProjectionAt0=viewProjection; }
        }
        internal static void Draw(object target,SymmetryPlanes.View[] planes,MyRenderMessageDebugDrawSphere[] spheres=null)
        {
            if((planes==null || planes.Length==0) && (spheres==null || spheres.Length==0)) return;
            var messages=new List<MyRenderMessageBase>();
            if(planes!=null) foreach(var plane in planes) messages.Add(new MyRenderMessageDebugDrawTriangle {
                Vertex0=plane.A,Vertex1=plane.B,Vertex2=plane.C,Color=plane.Color,DepthRead=true});
            if(spheres!=null) messages.AddRange(spheres);
            var vertices=new VertexSnapshot(buffers[0].GetValue(null));
            var meshes=(IDictionary)buffers[1].GetValue(null);
            var savedMeshes=new List<DictionaryEntry>();
            foreach(DictionaryEntry entry in meshes) savedMeshes.Add(entry);
            var lineVertices=new VertexSnapshot(buffers[2].GetValue(null));
            var batches=(IList)buffers[3].GetValue(null);
            var savedBatches=new object[batches.Count];
            batches.CopyTo(savedBatches,0);
            bool isolated=false;
            try
            {
                vertices.Clear(); meshes.Clear(); lineVertices.Clear(); batches.Clear();
                isolated=true;
                // Run native gameplay gizmos through the native processors using each eye's camera and depth.
                process.Invoke(null,new object[] {messages});
                drawPrimitives.Invoke(null,new object[] {target,MyRender11.Environment_Matrices.ViewProjectionAt0,true});
                drawLines.Invoke(null,new object[] {target,false});
            }
            finally
            {
                try { if(isolated) clearLines.Invoke(null,null); }
                finally
                {
                    try { vertices.Restore(); }
                    finally
                    {
                        try { meshes.Clear(); foreach(var entry in savedMeshes) meshes.Add(entry.Key,entry.Value); }
                        finally
                        {
                            try { lineVertices.Restore(); }
                            finally { batches.Clear(); foreach(var batch in savedBatches) batches.Add(batch); }
                        }
                    }
                }
            }
        }
        private sealed class VertexSnapshot
        {
            private readonly object list;
            private readonly MethodInfo clear,add;
            private readonly Array saved;
            internal VertexSnapshot(object list)
            {
                this.list=list;
                var type=list.GetType();
                clear=AccessTools.Method(type,"ClearFast");
                var items=(Array)AccessTools.Method(type,"GetInternalArray").Invoke(list,null);
                int count=(int)AccessTools.Property(type,"Count").GetValue(list);
                saved=Array.CreateInstance(items.GetType().GetElementType(),count);
                Array.Copy(items,saved,count);
                add=AccessTools.Method(type,"Add",new[] {items.GetType().GetElementType()});
                if(clear==null || add==null) throw new MissingMemberException(type.FullName,"ClearFast/Add");
            }
            internal void Clear() => clear.Invoke(list,null);
            internal void Restore() { Clear(); foreach(var item in saved) add.Invoke(list,new[] {item}); }
        }
    }
}
