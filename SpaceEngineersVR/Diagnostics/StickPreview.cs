using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class StickPreview
    {
        public static void Export(string gameBin,string subtype,string output)
        {
            if(subtype=="all")
            {
                foreach(var item in CockpitRig.All.Where(r=>r.HasSticks)) Export(gameBin,item.Subtype,Path.Combine(output,item.Subtype));
                return;
            }
            var rig=CockpitRig.Find(subtype);
            if(rig==null || !rig.HasSticks) throw new ArgumentException("Choose a cockpit rig with sticks",nameof(subtype));
            Directory.CreateDirectory(output);
            var content=Path.GetFullPath(Path.Combine(gameBin,"..","Content"));
            var mesh=rig.Geometry(content);
            var poses=new Dictionary<string,Vector3> { {"neutral",Vector3.Zero},{"pitch-back",Vector3.Left},{"pitch-forward",Vector3.Right},
                {"roll-left",Vector3.Forward},{"roll-right",Vector3.Backward},{"twist-left",Vector3.Down},{"twist-right",Vector3.Up},
                {"diagonal",Vector3.One} };
            foreach(var pose in poses)
            {
                var byActor=new Dictionary<int,List<ModelInspection.Part>>();
                var vertices=new List<float[]>(); var uv=new List<float[]>(); var parts=new List<ModelInspection.Part>();
                for(int actor=0;actor<mesh.Parts.Length;actor++)
                {
                    var source=mesh.Parts[actor];
                    var stick=rig.Left?.Moves(actor)==true ? rig.Left : rig.Right?.Moves(actor)==true ? rig.Right:null;
                    Matrix visual=stick?.Visual(pose.Value) ?? Matrix.Identity;
                    int first=vertices.Count;
                    foreach(var position in source.Positions)
                    {
                        var p=Vector3.Transform(position,visual); vertices.Add(new[] {p.X,p.Y,p.Z});
                    }
                    foreach(var tex in source.TexCoords) uv.Add(new[] {tex.X,tex.Y});
                    var actorParts=source.Sections.Select(section=>new ModelInspection.Part { Material=section.MaterialName,
                        Indices=source.Indices.Skip(section.IndexStart).Take(section.TriCount*3).Select(i=>i+first).ToArray() }).ToList();
                    byActor.Add(actor,actorParts); parts.AddRange(actorParts);
                }
                var report=new ModelInspection.Report { Model=rig.Model,Vertices=vertices.ToArray(),UV=uv.ToArray(),Parts=parts.ToArray() };
                using(var file=File.Create(Path.Combine(output,pose.Key+".json")))
                    new DataContractJsonSerializer(typeof(ModelInspection.Report)).WriteObject(file,report);
                foreach(bool left in new[] {true,false})
                {
                    var stick=left ? rig.Left:rig.Right;
                    if(stick==null) continue;
                    report.Parts=byActor.Where(a=>stick.Moves(a.Key) || stick.BaseActor==a.Key).SelectMany(a=>a.Value).ToArray();
                    using(var file=File.Create(Path.Combine(output,(left ? "L-":"R-")+pose.Key+".json")))
                        new DataContractJsonSerializer(typeof(ModelInspection.Report)).WriteObject(file,report);
                    Matrix visual=stick.Visual(pose.Value);
                    CockpitHandTests.ExportGrip(output,subtype,left,stick.Palm(left)*visual,0,"-"+pose.Key);
                }
            }
        }
    }
}
