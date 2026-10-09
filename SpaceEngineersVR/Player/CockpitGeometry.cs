using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
using VRageRender.Messages;
using VRage.Import;

namespace SpaceEngineersVR.Player
{
    // Only installed assets are read. No game mesh or texture is shipped with the plugin.
    internal sealed class CockpitGeometry
    {
        public readonly MyModelData[] Parts;
        public readonly int NativeTriangles;
        internal readonly Dictionary<string,MyMaterialDescriptor> Materials;
        internal CockpitGeometry(MyModelData[] parts,int triangles,Dictionary<string,MyMaterialDescriptor> materials=null)
        { Parts=parts; NativeTriangles=triangles; Materials=materials; }

        // Index 0 receives every triangle outside the measured pieces.
        internal static MyModelData[] Partition(Dictionary<string,object> tags,string name,int triangles,Vector3[] centers,int[] counts,int[] first=null,int[] moving=null)
        {
            var vertices=((HalfVector4[])tags["Vertices"]).Select(v=>new Vector3(v.ToVector4())).ToArray();
            var uv=(HalfVector2[])tags["TexCoords0"];
            var normals=(Byte4[])tags["Normals"]; var tangents=(Byte4[])tags["Tangents"];
            var material=((List<MyMeshPartInfo>)tags["MeshParts"]).Single(p=>p.m_MaterialDesc?.MaterialName==name);
            var indices=material.m_indices;
            if(indices.Count!=triangles*3 || normals.Length!=vertices.Length || tangents.Length!=vertices.Length)
                throw new InvalidDataException("Cockpit control mesh changed: "+name);
            var at=new Dictionary<Vector3,List<int>>();
            for(int t=0;t<indices.Count/3;t++)
                for(int j=0;j<3;j++)
                {
                    var p=vertices[indices[t*3+j]]; var key=new Vector3((float)Math.Round(p.X,4),(float)Math.Round(p.Y,4),(float)Math.Round(p.Z,4));
                    if(!at.TryGetValue(key,out var list)) at.Add(key,list=new List<int>());
                    list.Add(t);
                }
            var seen=new HashSet<int>(); var buckets=Enumerable.Range(0,centers.Length+1).Select(_=>new List<int>()).ToArray();
            for(int t=0;t<indices.Count/3;t++)
            {
                if(!seen.Add(t)) continue;
                var todo=new Stack<int>(); todo.Push(t); var group=new List<int>();
                var bounds=BoundingBox.CreateInvalid();
                while(todo.Count>0)
                {
                    int triangle=todo.Pop(); group.Add(triangle);
                    for(int j=0;j<3;j++)
                    {
                        var p=vertices[indices[triangle*3+j]]; bounds.Include(p);
                        var key=new Vector3((float)Math.Round(p.X,4),(float)Math.Round(p.Y,4),(float)Math.Round(p.Z,4));
                        foreach(int neighbor in at[key]) if(seen.Add(neighbor)) todo.Push(neighbor);
                    }
                }
                int slot=Array.FindIndex(centers,c=>Vector3.DistanceSquared(c,bounds.Center)<.000002f*.000002f);
                if(slot>=0 && group.Count!=counts[slot]) throw new InvalidDataException("Cockpit control topology changed: "+name);
                int begin=slot<0 ? 0:first?[slot] ?? 0,end=begin+(slot<0 ? group.Count:moving?[slot] ?? group.Count);
                if(begin<0 || end>group.Count) throw new InvalidDataException("Cockpit cap range changed: "+name);
                if(begin==0 && end==group.Count) buckets[slot+1].AddRange(group);
                else
                {
                    // Caps and fixed bezels can share welded vertices; their native triangle order separates them.
                    var ordered=group.OrderBy(i=>i).ToArray();
                    for(int i=0;i<ordered.Length;i++) buckets[i>=begin && i<end ? slot+1:0].Add(ordered[i]);
                }
            }
            var selected=moving ?? counts;
            if(buckets.Skip(1).Where((b,i)=>b.Count!=selected[i]).Any() || buckets[0].Count!=triangles-selected.Sum())
                throw new InvalidDataException("Could not isolate the inspected cockpit controls: "+name);
            return buckets.Select(bucket=>
            {
                var model=new MyModelData(); model.Clear();
                // Coplanar details depend on the installed mesh's triangle order.
                foreach(int triangle in bucket.OrderBy(t=>t))
                for(int corner=0;corner<3;corner++)
                {
                    int v=indices[triangle*3+corner];
                    model.Indices.Add(model.Positions.Count); model.Positions.Add(vertices[v]); model.AABB.Include(vertices[v]);
                    model.Normals.Add(VF_Packer.UnpackNormal(normals[v].PackedValue));
                    model.Tangents.Add(VF_Packer.UnpackNormal(tangents[v].PackedValue)); model.TexCoords.Add(uv[v].ToVector2());
                }
                model.Sections.Add(new MyRuntimeSectionInfo { IndexStart=0,TriCount=bucket.Count,MaterialName=name });
                return model;
            }).ToArray();
        }
    }
}
