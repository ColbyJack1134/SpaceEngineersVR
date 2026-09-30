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
    internal static class CockpitSwitchGeometry
    {
        internal const string Material="Chrome";
        internal const float Travel=1.05f;
        // Preserve the original four assignments, then use the other exposed levers.
        internal static readonly Vector3[] Centers={
            new Vector3(-.385498f,-.385986f,.075073f),new Vector3(-.389160f,-.390869f,.138367f),
            new Vector3(-.334961f,-.421387f,.097839f),new Vector3(-.337646f,-.424683f,.140320f),
            new Vector3(-.332397f,-.418091f,.054489f),new Vector3(-.333740f,-.419678f,.075989f),
            new Vector3(-.336182f,-.423096f,.118988f),new Vector3(-.338745f,-.426270f,.160034f),
            new Vector3(-.370361f,-.408203f,.182922f),
            new Vector3(-.384033f,-.384277f,.053238f),new Vector3(-.386719f,-.387695f,.096954f),
            new Vector3(-.387939f,-.389282f,.118225f),new Vector3(-.390381f,-.392334f,.156433f) };
        internal static readonly Vector3[] Pivots={
            new Vector3(-.391052f,-.389557f,.074089f),new Vector3(-.394867f,-.394409f,.137344f),
            new Vector3(-.340637f,-.425018f,.096840f),new Vector3(-.343201f,-.428284f,.139343f),
            new Vector3(-.338074f,-.421692f,.053486f),new Vector3(-.339294f,-.423340f,.074974f),
            new Vector3(-.341919f,-.426636f,.117981f),new Vector3(-.344391f,-.429810f,.159043f),
            new Vector3(-.376038f,-.411835f,.181870f),
            new Vector3(-.389648f,-.388114f,.052224f),new Vector3(-.392264f,-.391462f,.095939f),
            new Vector3(-.393433f,-.393351f,.117421f),new Vector3(-.395671f,-.396281f,.155660f) };
        internal static readonly Vector3 Normal=Vector3.Normalize(new Vector3(.55f,.83f,.10f));
        internal static readonly Vector3 Up=Vector3.Normalize(Vector3.Cross(Vector3.Backward,Normal));
        internal static readonly Vector3 Axis=Vector3.Normalize(Vector3.Cross(Normal,Up));
        internal static Matrix Visual(int slot,float value) => CockpitStickMath.Around(Pivots[slot],
            Matrix.CreateFromAxisAngle(Axis,MathHelper.Clamp(value,0,1)*Travel));

        internal static MyModelData[] Build(Dictionary<string,object> tags) =>
            Partition(tags,Material,4046,Centers,Enumerable.Repeat(56,Centers.Length).ToArray());

        internal static MyModelData[] Partition(Dictionary<string,object> tags,string name,int triangles,Vector3[] centers,int[] counts)
        {
            var vertices=((HalfVector4[])tags["Vertices"]).Select(v=>new Vector3(v.ToVector4())).ToArray();
            var uv=(HalfVector2[])tags["TexCoords0"];
            var normals=(Byte4[])tags["Normals"]; var tangents=(Byte4[])tags["Tangents"];
            var material=((List<MyMeshPartInfo>)tags["MeshParts"]).Single(p=>p.m_MaterialDesc?.MaterialName==name);
            var indices=material.m_indices;
            if(indices.Count!=triangles*3 || normals.Length!=vertices.Length || tangents.Length!=vertices.Length)
                throw new InvalidDataException("Fighter switch mesh changed; native cockpit retained.");
            var at=new Dictionary<Vector3,List<int>>();
            for(int t=0;t<indices.Count/3;t++)
                foreach(int v in indices.Skip(t*3).Take(3))
                {
                    var p=vertices[v]; var key=new Vector3((float)Math.Round(p.X,4),(float)Math.Round(p.Y,4),(float)Math.Round(p.Z,4));
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
                if(slot>=0 && group.Count!=counts[slot]) throw new InvalidDataException("Fighter lever topology changed.");
                buckets[slot+1].AddRange(group.SelectMany(i=>indices.Skip(i*3).Take(3)));
            }
            if(buckets.Skip(1).Where((b,i)=>b.Count!=counts[i]*3).Any() || buckets[0].Count!=(triangles-counts.Sum())*3)
                throw new InvalidDataException("Could not isolate the inspected fighter controls.");
            return buckets.Select(bucket=>
            {
                var model=new MyModelData(); model.Clear();
                foreach(int v in bucket)
                {
                    model.Indices.Add(model.Positions.Count); model.Positions.Add(vertices[v]); model.AABB.Include(vertices[v]);
                    model.Normals.Add(VF_Packer.UnpackNormal(normals[v].PackedValue));
                    model.Tangents.Add(VF_Packer.UnpackNormal(tangents[v].PackedValue)); model.TexCoords.Add(uv[v].ToVector2());
                }
                model.Sections.Add(new MyRuntimeSectionInfo { IndexStart=0,TriCount=bucket.Count/3,MaterialName=name });
                return model;
            }).ToArray();
        }
    }
}
