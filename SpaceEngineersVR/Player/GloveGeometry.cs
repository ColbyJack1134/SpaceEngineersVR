using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;
using VRageRender.Messages;
using VRage.Import;

namespace SpaceEngineersVR.Player
{
    internal sealed class GloveGeometry
    {
        internal const string DefaultModel="Models/Characters/Astronaut/SE_astronaut.mwm";
        internal struct Vertex { public Vector3 Position,Normal; public Vector2 UV; public Vector3 Closed,ClosedNormal; }
        internal Vertex[] Vertices;
        internal ushort[] Indices;
        internal string ColorTexture,ExtraTexture;
        internal Matrix WristMount,PointFrame;
        internal Vector3 PinchPoint;
        internal MyModelData NativeModel;
        private static Dictionary<string,object> Tags(string file)
        {
            var importer=new MyModelImporter();
            using(var reader=new BinaryReader(File.OpenRead(file)))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] { reader,new[] { "Vertices","TexCoords0","MeshParts","Bones","BlendIndices","BlendWeights","Normals","Tangents","GeometryDataAsset" } });
            return importer.GetTagData();
        }
        private static object Member(object value,string name) => AccessTools.Field(value.GetType(),name)?.GetValue(value) ?? AccessTools.Property(value.GetType(),name)?.GetValue(value);
        internal static GloveGeometry Load(string content,string model,bool left)
        {
            var tags=Tags(Path.Combine(content,model));
            if(tags.TryGetValue("GeometryDataAsset",out var asset) && asset is string path && !string.IsNullOrEmpty(path))
            {
                if(!path.EndsWith(".mwm",StringComparison.OrdinalIgnoreCase)) path+=".mwm";
                var geometry=Tags(Path.Combine(content,path));
                foreach(var pair in geometry)
                    if(pair.Key!="Bones" || !tags.ContainsKey("Bones") || ((ICollection)tags["Bones"]).Count==0) tags[pair.Key]=pair.Value;
            }
            var bones=((IEnumerable)tags["Bones"]).Cast<object>().ToArray();
            var names=bones.Select(b=>(string)Member(b,"Name")).ToArray();
            var parents=bones.Select(b=>Convert.ToInt32(Member(b,"Parent"))).ToArray();
            var locals=bones.Select(b=>(Matrix)Member(b,"Transform")).ToArray();
            var bind=new Matrix[bones.Length]; var posed=new[] { new Matrix[bones.Length],new Matrix[bones.Length] };
            string side=left ? "L":"R",prefix="SE_Rig"+side+"_";
            int palm=Array.IndexOf(names,"SE_Rig"+side+"Palm"),forearm=Array.IndexOf(names,"SE_Rig"+side+"Forearm1");
            if(palm<0 || forearm<0) throw new InvalidDataException("Character glove skeleton unavailable: "+model);
            for(int i=0;i<bones.Length;i++)
            {
                bind[i]=locals[i]*(parents[i]<0 ? Matrix.Identity:bind[parents[i]]);
                for(int p=0;p<2;p++)
                {
                    Matrix rotation=names[i].StartsWith(prefix,StringComparison.Ordinal) ? Matrix.CreateFromQuaternion(CockpitHandPose.Rotation(names[i],p==1)):Matrix.Identity;
                    posed[p][i]=rotation*locals[i]*(parents[i]<0 ? Matrix.Identity:posed[p][parents[i]]);
                }
            }
            Matrix correction=ArmMath.PalmCorrection(bind[palm],bind[forearm],left ? -1:1);
            Matrix localPalm=Matrix.Invert(bind[palm])*correction;
            var normals=(Byte4[])tags["Normals"]; var tangents=(Byte4[])tags["Tangents"];
            var native=new MyModelData(); native.Clear();
            int indexTip=Array.IndexOf(names,"SE_Rig"+side+"_Index_3");
            var positions=(HalfVector4[])tags["Vertices"]; var texcoords=(HalfVector2[])tags["TexCoords0"];
            var weights=(Array)tags["BlendWeights"]; var blends=(Array)tags["BlendIndices"];
            var part=((List<MyMeshPartInfo>)tags["MeshParts"]).Single(p=>p.m_MaterialDesc?.MaterialName==(left ? "LeftGlove":"RightGlove"));
            var map=new Dictionary<int,ushort>(); var vertices=new List<Vertex>(); var indices=new List<ushort>();
            foreach(int index in part.m_indices)
            {
                if(!map.TryGetValue(index,out ushort mapped))
                {
                    var position=positions[index].ToVector4(); var source=new Vector3(position.X,position.Y,position.Z);
                    var w=weights.GetValue(index); var b=blends.GetValue(index); var points=new Vector3[2]; var ns=new Vector3[2]; Vector3 tangent=Vector3.Zero;
                    foreach(string component in new[] { "X","Y","Z","W" })
                    {
                        float weight=Convert.ToSingle(Member(w,component)); if(weight<=0) continue;
                        int bone=Convert.ToInt32(Member(b,component));
                        for(int p=0;p<2;p++)
                        {
                            Matrix transform=Matrix.Invert(bind[bone])*posed[p][bone]*localPalm;
                            points[p]+=Vector3.Transform(source,transform)*weight;
                            ns[p]+=Vector3.TransformNormal(VF_Packer.UnpackNormal(normals[index].PackedValue),transform)*weight;
                            if(p==0) tangent+=Vector3.TransformNormal(VF_Packer.UnpackNormal(tangents[index].PackedValue),transform)*weight;
                        }
                    }
                    mapped=checked((ushort)vertices.Count); map[index]=mapped;
                    vertices.Add(new Vertex { Position=points[0],Closed=points[1],Normal=Vector3.Normalize(ns[0]),ClosedNormal=Vector3.Normalize(ns[1]),UV=texcoords[index].ToVector2() });
                    native.Positions.Add(points[0]); native.Normals.Add(Vector3.Normalize(ns[0]));
                    native.Tangents.Add(Vector3.Normalize(tangent)); native.TexCoords.Add(texcoords[index].ToVector2()); native.AABB.Include(points[0]);
                }
                indices.Add(mapped);
            }
            var output=vertices.ToArray();
            native.Indices.AddRange(indices.Select(i=>(int)i));
            native.Sections.Add(new MyRuntimeSectionInfo { IndexStart=0,TriCount=indices.Count/3,MaterialName=part.m_MaterialDesc.MaterialName });
            part.m_MaterialDesc.Textures.TryGetValue("ColorMetalTexture",out string color);
            part.m_MaterialDesc.Textures.TryGetValue("AddMapsTexture",out string extra);
            if(string.IsNullOrEmpty(color) || string.IsNullOrEmpty(extra)) throw new InvalidDataException("Character glove materials unavailable: "+model);
            int wrist=Array.IndexOf(names,"SE_Rig"+side+"Forearm2");
            Matrix mount=TrackedArms.WristScreenLocal*(wrist<0 ? bind[forearm]:bind[wrist])*localPalm;
            Matrix point=(Matrix)CockpitHandPose.PointPose(Matrix.Identity,correction,posed[0][indexTip]*Matrix.Invert(bind[palm]));
            int thumbTip=Array.IndexOf(names,"SE_Rig"+side+"_Thumb_3");
            var pinch=Vector3.Transform(new Vector3(-.025f,0,0),posed[1][indexTip]*localPalm);
            if(thumbTip>=0) pinch=(pinch+Vector3.Transform(new Vector3(-.025f,0,0),posed[1][thumbTip]*localPalm))*.5f;
            return new GloveGeometry { PinchPoint=pinch,Vertices=output,Indices=indices.ToArray(),ColorTexture=color,ExtraTexture=extra,
                WristMount=mount,PointFrame=point,NativeModel=native };
        }
    }
}
