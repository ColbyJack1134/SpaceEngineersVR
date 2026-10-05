using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
using VRageRender.Messages;

namespace SpaceEngineersVR.Player
{
    internal static class FighterProfile
    {
        public const string Subtype="DBSmallBlockFighterCockpit";
        public const string Model="Models/Cubes/small/CockpitFighterInterior.mwm";
        public const string Material="CockpitSmall";
        public static readonly Vector3 LeftContact=new Vector3(-0.313f,-0.235f,0.365f);
        public static readonly Vector3 RightContact=new Vector3(0.305f,-0.235f,0.365f);
        public static readonly Vector3 LeftPivot=new Vector3(-0.352f,-0.355f,0.368f);
        public static readonly Vector3 RightPivot=new Vector3(0.349f,-0.355f,0.369f);
        public const float Tilt=MathHelper.Pi/6, Twist=MathHelper.Pi/6, CaptureRadius=0.14f;
    }

    // Only installed assets are read. No game mesh or texture is shipped with the plugin.
    internal sealed class CockpitGeometry
    {
        public readonly MyModelData[] Parts;
        public readonly int NativeTriangles;
        internal CockpitGeometry(MyModelData[] parts,int triangles) { Parts=parts; NativeTriangles=triangles; }
        public static CockpitGeometry Load(string content)
        {
            long started=FeatureTiming.Start();
            try { return LoadCore(content); }
            finally { FeatureTiming.End(FeatureTiming.Area.CockpitGeometry,started); }
        }
        private static CockpitGeometry LoadCore(string content)
        {
            var importer=new MyModelImporter();
            using (var reader=new BinaryReader(File.OpenRead(Path.Combine(content,FighterProfile.Model.Replace('/',Path.DirectorySeparatorChar)))))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {
                    reader,new[] { "Vertices","TexCoords0","MeshParts","Normals","Tangents" } });
            var tags=importer.GetTagData();
            var vertices=(HalfVector4[])tags["Vertices"];
            var uv=(HalfVector2[])tags["TexCoords0"];
            var parts=(List<MyMeshPartInfo>)tags["MeshParts"];
            var native=parts.Where(p=>p.m_MaterialDesc?.MaterialName==FighterProfile.Material).ToArray();
            if (native.Length!=1 || native[0].m_indices.Count!=568*3 || vertices.Length!=uv.Length)
                throw new InvalidDataException("Fighter stick material layout changed; native interior retained.");
            var indices=native[0].m_indices;
            var positions=vertices.Select(v=>new Vector3(v.ToVector4())).ToArray();
            var atVertex=new Dictionary<Vector3,List<int>>();
            for (int t=0;t<indices.Count/3;t++)
                for (int j=0;j<3;j++)
                {
                    Vector3 key=Weld(positions[indices[t*3+j]]);
                    if (!atVertex.TryGetValue(key,out var list)) atVertex.Add(key,list=new List<int>());
                    list.Add(t);
                }
            var groups=new List<List<int>>();
            var seen=new HashSet<int>();
            for (int t=0;t<indices.Count/3;t++)
            {
                if (!seen.Add(t)) continue;
                var todo=new Stack<int>(); todo.Push(t);
                var group=new List<int>(); groups.Add(group);
                while (todo.Count>0)
                {
                    int triangle=todo.Pop(); group.Add(triangle);
                    for (int j=0;j<3;j++)
                        foreach (int neighbor in atVertex[Weld(positions[indices[triangle*3+j]])])
                            if (seen.Add(neighbor)) todo.Push(neighbor);
                }
            }
            if (groups.Count!=6) throw new InvalidDataException("Fighter sticks no longer have six isolated pieces.");
            // Left base, left handle/stem, right handle/stem, right base.
            var output=Enumerable.Range(0,6).Select(_=>new MyModelData()).ToArray();
            foreach (var data in output) data.Clear();
            foreach (var group in groups)
            {
                float x=positions[indices[group[0]*3]].X;
                int slot=group.Count==104 ? (x<0 ? 0 : 5) : group.Count==164 ? (x<0 ? 1 : 3) : group.Count==16 ? (x<0 ? 2 : 4) : -1;
                if (slot<0 || output[slot].Indices.Count!=0) throw new InvalidDataException("Unexpected fighter stick topology.");
                foreach (int t in group)
                {
                    int a=indices[t*3],b=indices[t*3+1],c=indices[t*3+2];
                    Vector3 normal=Vector3.Cross(positions[b]-positions[a],positions[c]-positions[a]);
                    if (normal.LengthSquared()<1e-14f) throw new InvalidDataException("Degenerate fighter stick triangle.");
                    normal.Normalize();
                    Vector2 du=uv[b].ToVector2()-uv[a].ToVector2(),dv=uv[c].ToVector2()-uv[a].ToVector2();
                    float det=du.X*dv.Y-du.Y*dv.X;
                    Vector3 tangent=Math.Abs(det)>1e-8f ? ((positions[b]-positions[a])*dv.Y-(positions[c]-positions[a])*du.Y)/det : Vector3.Cross(normal,Vector3.Right);
                    tangent-=normal*Vector3.Dot(normal,tangent);
                    if (tangent.LengthSquared()<1e-8f) tangent=Vector3.Cross(normal,Vector3.Up);
                    tangent.Normalize();
                    foreach (int v in new[] { a,b,c })
                    {
                        var data=output[slot];
                        Vector3 p=positions[v];
                        if (Math.Abs(p.X)<0.26f || Math.Abs(p.X)>0.46f || p.Y< -0.50f || p.Y> -0.10f || p.Z<0.29f || p.Z>0.45f)
                            throw new InvalidDataException("CockpitSmall includes geometry outside the inspected stick bounds.");
                        data.Indices.Add(data.Positions.Count); data.Positions.Add(p);
                        data.Normals.Add(normal); data.Tangents.Add(tangent); data.TexCoords.Add(uv[v].ToVector2());
                        data.AABB.Include(p);
                    }
                }
            }
            int[] counts={104,164,16,164,16,104};
            for (int i=0;i<output.Length;i++)
            {
                if (output[i].Indices.Count!=counts[i]*3) throw new InvalidDataException("Missing fighter stick piece.");
                output[i].Sections.Add(new MyRuntimeSectionInfo { IndexStart=0,TriCount=counts[i],MaterialName=FighterProfile.Material });
            }
            return new CockpitGeometry(output.Concat(CockpitSwitchGeometry.Build(tags)).Concat(CockpitCoverGeometry.Build(tags)).ToArray(),568);
        }
        private static Vector3 Weld(Vector3 p) => new Vector3((float)Math.Round(p.X,4),(float)Math.Round(p.Y,4),(float)Math.Round(p.Z,4));
    }
}
