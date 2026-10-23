using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using SpaceEngineersVR.Plugin;
using SpaceEngineersVR.Util;
using Valve.VR;
using VRageMath;
using Vertex=SpaceEngineersVR.Player.GloveGeometry.Vertex;

namespace SpaceEngineersVR.Player
{
    // SteamVR controller meshes in device space, loaded asynchronously on the render thread.
    internal static class ControllerModel
    {
        internal sealed class Part { public Vertex[] Vertices; public ushort[] Indices; public Matrix Transform; }
        private sealed class Entry { public string[] Pending; public Matrix[] Transforms; public readonly List<Part> Parts=new List<Part>(); public bool Done; }
        private static readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>();

        // Returns true once every part has loaded; parts may be empty if SteamVR has no mesh.
        internal static bool TryGet(Controller hand,out List<Part> parts)
        {
            parts=null;
            string model=hand.ProfileName;
            if(OpenVR.RenderModels==null || string.IsNullOrEmpty(model) || model=="default") return false;
            if(!entries.TryGetValue(model,out var entry)) entries[model]=entry=Create(model,hand.InputSource);
            if(entry.Done) { parts=entry.Parts; return true; }
            for(int i=0;i<entry.Pending.Length;i++)
            {
                if(entry.Pending[i]==null) continue;
                var ptr=IntPtr.Zero;
                var error=OpenVR.RenderModels.LoadRenderModel_Async(entry.Pending[i],ref ptr);
                if(error==EVRRenderModelError.Loading) return false;
                if(error==EVRRenderModelError.None) { entry.Parts.Add(Read(ptr,entry.Transforms[i])); OpenVR.RenderModels.FreeRenderModel(ptr); }
                else Logger.Warning("Controller mesh "+entry.Pending[i]+" unavailable: "+error);
                entry.Pending[i]=null;
            }
            entry.Done=true; parts=entry.Parts;
            return true;
        }
        private static Entry Create(string model,ulong source)
        {
            var names=new List<string>(); var transforms=new List<Matrix>();
            var name=new StringBuilder(256); var mesh=new StringBuilder(256);
            uint count=OpenVR.RenderModels.GetComponentCount(model);
            for(uint i=0;i<count;i++)
            {
                name.Clear(); mesh.Clear();
                if(OpenVR.RenderModels.GetComponentName(model,i,name,256)==0) continue;
                if(OpenVR.RenderModels.GetComponentRenderModelName(model,name.ToString(),mesh,256)==0) continue;
                var mode=new RenderModel_ControllerMode_State_t(); var state=new RenderModel_ComponentState_t();
                bool known=OpenVR.RenderModels.GetComponentStateForDevicePath(model,name.ToString(),source,ref mode,ref state);
                if(known && (state.uProperties & (uint)EVRComponentProperty.IsVisible)==0) continue;
                names.Add(mesh.ToString()); transforms.Add(known ? state.mTrackingToComponentRenderModel.ToMatrix():Matrix.Identity);
            }
            if(names.Count==0) { names.Add(model); transforms.Add(Matrix.Identity); }
            return new Entry { Pending=names.ToArray(),Transforms=transforms.ToArray() };
        }
        private static Part Read(IntPtr ptr,Matrix transform)
        {
#if LINUX
            // OpenVR packs RenderModel_t to 4 bytes on Linux.
            var model=new RenderModel_t();
            Marshal.PtrToStructure<RenderModel_t_Packed>(ptr).Unpack(ref model);
#else
            var model=Marshal.PtrToStructure<RenderModel_t>(ptr);
#endif
            var data=new float[model.unVertexCount*8];
            Marshal.Copy(model.rVertexData,data,0,data.Length);
            var vertices=new Vertex[model.unVertexCount];
            for(int i=0;i<vertices.Length;i++)
            {
                int v=i*8;
                var position=new Vector3(data[v],data[v+1],data[v+2]); var normal=new Vector3(data[v+3],data[v+4],data[v+5]);
                vertices[i]=new Vertex { Position=position,Normal=normal,UV=new Vector2(data[v+6],data[v+7]),Closed=position,ClosedNormal=normal };
            }
            var indices=new short[model.unTriangleCount*3];
            Marshal.Copy(model.rIndexData,indices,0,indices.Length);
            return new Part { Vertices=vertices,Indices=Array.ConvertAll(indices,x=>(ushort)x),Transform=transform };
        }
    }
}
