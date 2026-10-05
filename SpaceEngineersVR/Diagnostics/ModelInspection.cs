using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using HarmonyLib;
using VRageMath;
using VRageMath.PackedVector;
using VRageRender.Import;

namespace SpaceEngineersVR.Diagnostics
{
    public static class ModelInspection
    {
        [DataContract] public sealed class Report
        {
            [DataMember] public string Model,Sha256,GeometryModel,GeometrySha256,Coordinates="Meters; +Y up, -Z forward. Row-vector local * parent transforms.";
            [DataMember] public float RescaleFactor;
            [DataMember] public float[][] Vertices,UV,BlendWeights;
            [DataMember] public int[][] BlendIndices;
            [DataMember] public Part[] Parts;
            [DataMember] public Node[] Bones,Dummies;
        }
        [DataContract] public sealed class Part
        {
            [DataMember] public string Material,Technique,ColorMetalTexture;
            [DataMember] public int[] Indices;
        }
        [DataContract] public sealed class Node
        {
            [DataMember] public string Name;
            [DataMember] public int Parent=-1;
            [DataMember] public float[] Transform;
        }
        public static void Export(string gameBin,string model,string output)
        {
            string content=Path.GetFullPath(Path.Combine(gameBin,"..","Content"));
            string path=Path.GetFullPath(Path.Combine(content,model.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(content+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new FileNotFoundException("Choose an installed model under Content",model);
            var tags=ReadModel(content,path,out string geometryPath);
            if(!tags.ContainsKey("Vertices")) throw new InvalidDataException("Model has no geometry: "+model);
            var vertices=(HalfVector4[])tags["Vertices"];
            var parts=(List<MyMeshPartInfo>)tags["MeshParts"];
            var report=new Report {
                Model=model.Replace('\\','/'),GeometryModel=geometryPath.Substring(content.Length+1).Replace('\\','/'),
                Sha256=Hash(path),GeometrySha256=Hash(geometryPath),
                RescaleFactor=tags.ContainsKey("RescaleFactor") ? Convert.ToSingle(tags["RescaleFactor"]) : 1,
                Vertices=vertices.Select(v=> {var p=v.ToVector4(); return new[] {p.X,p.Y,p.Z};}).ToArray(),
                UV=tags.TryGetValue("TexCoords0",out var uv) && uv is HalfVector2[] tex ? tex.Select(t=> {var p=t.ToVector2();return new[] {p.X,p.Y};}).ToArray() : new float[0][],
                Parts=parts.Select(p=>new Part {Material=p.m_MaterialDesc?.MaterialName ?? "default",Indices=p.m_indices.ToArray(),
                    Technique=p.m_MaterialDesc?.Technique,ColorMetalTexture=p.m_MaterialDesc!=null && p.m_MaterialDesc.Textures.TryGetValue("ColorMetalTexture",out var texture) ? texture : null }).ToArray(),
                Bones=ReadBones(tags.TryGetValue("Bones",out var bones) ? bones as IEnumerable : null),
                Dummies=ReadDummies(tags.TryGetValue("Dummies",out var dummies) ? dummies as IDictionary : null),
                BlendWeights=ReadVectors(tags.TryGetValue("BlendWeights",out var weights) ? weights as IEnumerable : null),
                BlendIndices=ReadVectors(tags.TryGetValue("BlendIndices",out var indices) ? indices as IEnumerable : null).Select(v=>v.Select(x=>(int)x).ToArray()).ToArray()
            };
            Directory.CreateDirectory(output);
            string name=Path.GetFileNameWithoutExtension(path);
            using(var stream=File.Create(Path.Combine(output,name+".json"))) new DataContractJsonSerializer(typeof(Report)).WriteObject(stream,report);
            using(var writer=new StreamWriter(Path.Combine(output,name+".obj")))
            {
                writer.WriteLine("# SEVR model inspection: meters, Y up. No rescaling applied.");
                foreach(var v in report.Vertices) writer.WriteLine("v "+string.Join(" ",v.Select(Number)));
                foreach(var t in report.UV) writer.WriteLine("vt "+Number(t[0])+" "+Number(1-t[1]));
                foreach(var part in report.Parts)
                {
                    writer.WriteLine("g "+part.Material.Replace(' ','_'));
                    for(int i=0;i<part.Indices.Length;i+=3)
                    {
                        var face=part.Indices.Skip(i).Take(3).Select(v=>(v+1).ToString(CultureInfo.InvariantCulture));
                        writer.WriteLine("f "+string.Join(" ",report.UV.Length==report.Vertices.Length ? face.Select(v=>v+"/"+v) : face));
                    }
                }
            }
            Console.WriteLine($"Exported {name}: {report.Vertices.Length} vertices, {report.Parts.Length} materials, {report.Bones.Length} bones, {report.Dummies.Length} dummies");
        }
        internal static Dictionary<string,object> ReadModel(string content,string path,out string geometryPath)
        {
            var tags=ReadTags(path);
            geometryPath=path;
            if(!tags.ContainsKey("Vertices") && tags.TryGetValue("GeometryDataAsset",out var asset) && asset is string geometry)
            {
                geometry=geometry.Replace('\\',Path.DirectorySeparatorChar).Replace('/',Path.DirectorySeparatorChar);
                if(!geometry.EndsWith(".mwm",StringComparison.OrdinalIgnoreCase)) geometry+=".mwm";
                geometryPath=Path.GetFullPath(Path.Combine(content,geometry));
                if(!geometryPath.StartsWith(content+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Geometry reference leaves Content");
                foreach(var tag in ReadTags(geometryPath))
                    if(!tags.ContainsKey(tag.Key) || tags[tag.Key] is ICollection collection && collection.Count==0) tags[tag.Key]=tag.Value;
            }
            return tags;
        }
        private static Dictionary<string,object> ReadTags(string path)
        {
            var importer=new MyModelImporter();
            using(var reader=new BinaryReader(File.OpenRead(path)))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {reader,new[] {
                    "Vertices","TexCoords0","MeshParts","Dummies","Bones","BlendIndices","BlendWeights","RescaleFactor","GeometryDataAsset" }});
            return importer.GetTagData();
        }
        private static string Hash(string path)
        {
            using(var stream=File.OpenRead(path)) using(var hash=SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant();
        }
        private static string Number(float x) => x.ToString("R",CultureInfo.InvariantCulture);
        private static object Member(object value,string name) => value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(value)
            ?? value.GetType().GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(value);
        private static float[] MatrixValues(object value)
        {
            if(value is MatrixD d) value=(Matrix)d;
            var m=(Matrix)value;
            return new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
        }
        private static Node[] ReadBones(IEnumerable bones) => bones==null ? new Node[0] : bones.Cast<object>().Select(b=>new Node {
            Name=(string)Member(b,"Name"),Parent=Convert.ToInt32(Member(b,"Parent")),Transform=MatrixValues(Member(b,"Transform")) }).ToArray();
        private static Node[] ReadDummies(IDictionary dummies)
        {
            if(dummies==null) return new Node[0];
            var result=new List<Node>();
            foreach(DictionaryEntry pair in dummies) result.Add(new Node {Name=(string)pair.Key,Transform=MatrixValues(Member(pair.Value,"Matrix"))});
            return result.ToArray();
        }
        internal static float[][] ReadVectors(IEnumerable vectors) => vectors==null ? new float[0][] : vectors.Cast<object>()
            .Select(v=>new[] {"X","Y","Z","W"}.Select(n=>Convert.ToSingle(Member(v,n))).ToArray()).ToArray();
    }
}
