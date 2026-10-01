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
        internal const int Count=41;
        internal const float Travel=1.05f;
        // Slot order is persisted in cockpit toolbar assignments.
        internal static readonly Vector3[] Centers={
            new Vector3(-.385498f,-.385986f,.075073f),new Vector3(-.389160f,-.390869f,.138367f),
            new Vector3(-.334961f,-.421387f,.097839f),new Vector3(-.337646f,-.424683f,.140320f),
            new Vector3(-.332397f,-.418091f,.054489f),new Vector3(-.333740f,-.419678f,.075989f),
            new Vector3(-.336182f,-.423096f,.118988f),new Vector3(-.338745f,-.426270f,.160034f),
            new Vector3(-.370361f,-.408203f,.182922f),
            new Vector3(-.384033f,-.384277f,.053238f),new Vector3(-.386719f,-.387695f,.096954f),
            new Vector3(-.387939f,-.389282f,.118225f),new Vector3(-.390381f,-.392334f,.156433f),
            new Vector3(-.37561035f,-.45568848f,.57714843f),new Vector3(-.39025879f,-.44714356f,.59521485f),
            new Vector3(-.40515137f,-.43859863f,.61328125f),new Vector3(-.42285155f,-.42736816f,.62695313f),
            new Vector3(.37585449f,-.45581055f,.57666015f),new Vector3(.39355469f,-.44445801f,.59033203f),
            new Vector3(.40844727f,-.43591309f,.60864257f),new Vector3(.42004394f,-.43029785f,.6311035f),
            new Vector3(-0.148986817f,-0.403320312f,-0.253051758f),
            new Vector3(-0.124755860f,-0.402587890f,-0.255554200f),
            new Vector3(-0.101226808f,-0.402587890f,-0.255554200f),
            new Vector3(-0.077667238f,-0.402587890f,-0.255554200f),
            new Vector3(-0.054138184f,-0.402587890f,-0.255554200f),
            new Vector3(-0.031822205f,-0.403320312f,-0.253051758f),
            new Vector3(0.051971436f,-0.402587890f,-0.255554200f),
            new Vector3(0.075103760f,-0.403320312f,-0.253051758f),
            new Vector3(0.098297120f,-0.403320312f,-0.253051758f),
            new Vector3(0.120880128f,-0.403320312f,-0.253051758f),
            new Vector3(0.148620606f,-0.402587890f,-0.255554200f),
            new Vector3(0.171508789f,-0.402587890f,-0.255554200f),
            new Vector3(0.252136231f,-0.369384772f,-0.298583986f),
            new Vector3(0.269287110f,-0.364990228f,-0.281127924f),
            new Vector3(0.286376954f,-0.360595703f,-0.263671881f),
            new Vector3(0.300781250f,-0.359619140f,-0.242431640f),
            new Vector3(0.340209961f,-0.346923825f,-0.208801269f),
            new Vector3(0.354492182f,-0.345947265f,-0.187500000f),
            new Vector3(0.374511714f,-0.338134772f,-0.173828126f),
            new Vector3(0.388793950f,-0.337036137f,-0.152587890f) };
        internal static readonly Vector3[] Pivots={
            new Vector3(-.391052f,-.389557f,.074089f),new Vector3(-.394867f,-.394409f,.137344f),
            new Vector3(-.340637f,-.425018f,.096840f),new Vector3(-.343201f,-.428284f,.139343f),
            new Vector3(-.338074f,-.421692f,.053486f),new Vector3(-.339294f,-.423340f,.074974f),
            new Vector3(-.341919f,-.426636f,.117981f),new Vector3(-.344391f,-.429810f,.159043f),
            new Vector3(-.376038f,-.411835f,.181870f),
            new Vector3(-.389648f,-.388114f,.052224f),new Vector3(-.392264f,-.391462f,.095939f),
            new Vector3(-.393433f,-.393351f,.117421f),new Vector3(-.395671f,-.396281f,.155660f),
            new Vector3(-.3806979f,-.45931765f,.57439466f),new Vector3(-.39543481f,-.45077891f,.59250195f),
            new Vector3(-.41020472f,-.44226865f,.6106199f),new Vector3(-.42510876f,-.43361657f,.62862287f),
            new Vector3(.38088345f,-.45936456f,.5741471f),new Vector3(.39578591f,-.45076906f,.59203787f),
            new Vector3(.4105341f,-.44226321f,.6101948f),new Vector3(.4252087f,-.43387958f,.62853603f),
            new Vector3(-0.148948888f,-0.408472688f,-0.257350265f),
            new Vector3(-0.124734059f,-0.407844331f,-0.259791500f),
            new Vector3(-0.101188657f,-0.407844323f,-0.259791496f),
            new Vector3(-0.077666139f,-0.407844328f,-0.259791499f),
            new Vector3(-0.054128776f,-0.407844325f,-0.259791497f),
            new Vector3(-0.031814100f,-0.408472691f,-0.257350266f),
            new Vector3(0.051968156f,-0.407844331f,-0.259791500f),
            new Vector3(0.075081802f,-0.408472692f,-0.257350266f),
            new Vector3(0.098312774f,-0.408472691f,-0.257350266f),
            new Vector3(0.120856285f,-0.408472695f,-0.257350265f),
            new Vector3(0.148614415f,-0.407844327f,-0.259791498f),
            new Vector3(0.171550417f,-0.407844292f,-0.259791475f),
            new Vector3(0.254147617f,-0.375981952f,-0.298756263f),
            new Vector3(0.271242164f,-0.371550907f,-0.281293531f),
            new Vector3(0.288332176f,-0.367162637f,-0.263792470f),
            new Vector3(0.305380442f,-0.362924475f,-0.246131817f),
            new Vector3(0.342092685f,-0.353437750f,-0.208967097f),
            new Vector3(0.359122655f,-0.349226965f,-0.191275973f),
            new Vector3(0.376417690f,-0.344610142f,-0.173969992f),
            new Vector3(0.393385651f,-0.340343148f,-0.156315802f) };
        internal static readonly Vector3 Normal=Vector3.Normalize(new Vector3(.55f,.83f,.10f));
        internal static readonly Vector3 Up=Vector3.Normalize(Vector3.Cross(Vector3.Backward,Normal));
        internal static readonly Vector3 Axis=Vector3.Normalize(Vector3.Cross(Normal,Up));
        internal static int CoverIndex(int slot) => slot<13 ? slot : slot>=21 && slot<33 ? slot-8 : -1;
        internal static bool Covered(int slot) => CoverIndex(slot)>=0;
        internal static Vector3 NormalFor(int slot) => slot>=21 ? Vector3.Normalize(slot<33 ? new Vector3(0,.967f,.255f) : new Vector3(-.517f,.8f,.307f)) : slot<13 ? Normal :
            Vector3.Normalize(new Vector3(slot<17 ? .59f : -.59f,.8f,.105f));
        internal static Vector3 UpFor(int slot) => slot>=21 ? Vector3.Normalize(Vector3.Cross(slot<33 ? Vector3.Left : Vector3.Normalize(new Vector3(-.696f,-.179f,-.696f)),NormalFor(slot))) : slot<13 ? Up :
            Vector3.Normalize(Vector3.Cross(NormalFor(slot),new Vector3(.59f,slot<17 ? -.34f : .34f,slot<17 ? -.73f : .73f)));
        internal static Vector3 AxisFor(int slot) => slot<13 ? Axis : Vector3.Normalize(Vector3.Cross(NormalFor(slot),UpFor(slot)));
        internal static Matrix Visual(int slot,float value)
        {
            float angle=MathHelper.Clamp(value,0,1)*Travel;
            if(slot>=13)
            {
                // Later banks contain both native detent poses; normalize around each measured pivot.
                var stem=Centers[slot]-Pivots[slot];
                float rest=(float)Math.Atan2(Vector3.Dot(stem,UpFor(slot)),Vector3.Dot(stem,NormalFor(slot)));
                angle-=Travel*.5f+rest;
            }
            return CockpitStickMath.Around(Pivots[slot],Matrix.CreateFromAxisAngle(AxisFor(slot),angle));
        }

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
                if(slot>=0 && group.Count!=counts[slot]) throw new InvalidDataException("Fighter lever topology changed.");
                foreach(int triangle in group)
                    for(int j=0;j<3;j++) buckets[slot+1].Add(indices[triangle*3+j]);
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
