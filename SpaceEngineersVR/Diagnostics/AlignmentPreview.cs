#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SpaceEngineersVR.Player;
using VRageMath;
using Vertex=SpaceEngineersVR.Player.GloveGeometry.Vertex;

namespace SpaceEngineersVR.Diagnostics
{
    // Hand alignment target glove with installed SteamVR controller meshes, held at the grip pose the plugin resolves for each.
    public static class AlignmentPreview
    {
        private static readonly string[] Models={"oculus_quest_plus_controller_right","oculus_quest2_controller_right","valve_controller_knu_1_0_right","vr_controller_vive_1_5",
            "frame_controller_right","vive_focus3_controller_right","pico_4_controller_right"};
        public static void Export(string game,string output,string steamVr,Action<string> log)
        {
            Directory.CreateDirectory(output);
            UiTests.Initialize(game,Path.Combine(output,"data"));
            var folders=new[] {Path.Combine(steamVr,"resources","rendermodels")}.Concat(Directory.GetDirectories(Path.Combine(steamVr,"drivers")).Select(d=>Path.Combine(d,"resources","rendermodels")));
            int rendered=0;
            using(var device=new Device(DriverType.Warp,DeviceCreationFlags.BgraSupport))
                foreach(string name in Models)
                {
                    string folder=folders.Select(f=>Path.Combine(f,name)).FirstOrDefault(f=>File.Exists(Path.Combine(f,name+".json")));
                    if(folder==null) { log("SKIP alignment preview: "+name+" not installed"); continue; }
                    string json=File.ReadAllText(Path.Combine(folder,name+".json"));
                    bool left=name.Contains("left");
                    var openXr=Component(json,"openxr_grip"); var handgrip=Component(json,"handgrip");
                    Controller.TryGrip(name,left,openXr,handgrip,out var grip,out string source);
                    var held=Matrix.Invert(grip);
                    var parts=Load(folder,json);
                    foreach(var view in new[] {Tuple.Create("side",new Vector3(.5f,.06f,.04f)),Tuple.Create("front",new Vector3(.22f,.16f,-.42f))})
                        UiTests.Save(MenuHands.PreviewAlignment(device,left,parts,held,view.Item2),Path.Combine(output,"alignment-"+name+"-"+view.Item1+".png"));
                    if(rendered++==0)
                        UiTests.Save(MenuHands.PreviewAlignment(device,left,parts,held*Matrix.CreateRotationX(MathHelper.ToRadians(40)),new Vector3(.5f,.06f,.04f)),
                            Path.Combine(output,"alignment-"+name+"-tilted-side.png"));
                    log("alignment preview "+name+": "+source);
                }
            log("PASS hand alignment previews: installed glove target with "+rendered+" SteamVR controller meshes");
        }
        private static Matrix? Component(string json,string name)
        {
            var match=Regex.Match(json,"\""+name+"\"\\s*:\\s*\\{\\s*\"component_local\"\\s*:\\s*\\{([^}]*)\\}");
            if(!match.Success) return null;
            float[] Array(string key) => Regex.Match(match.Groups[1].Value,"\""+key+"\"\\s*:\\s*\\[([^\\]]*)\\]").Groups[1].Value.Split(',').Select(P).ToArray();
            var origin=Array("origin"); var rotation=Array("rotate_xyz");
            return Matrix.CreateRotationX(MathHelper.ToRadians(rotation[0]))*Matrix.CreateRotationY(MathHelper.ToRadians(rotation[1]))*
                Matrix.CreateRotationZ(MathHelper.ToRadians(rotation[2]))*Matrix.CreateTranslation(origin[0],origin[1],origin[2]);
        }
        private static ControllerModel.Part[] Load(string folder,string json)
        {
            var vertices=new List<Vertex>(); var parts=new List<ControllerModel.Part>();
            foreach(Match file in Regex.Matches(json,"\"filename\"\\s*:\\s*\"([^\"]+)\""))
                if(File.Exists(Path.Combine(folder,file.Groups[1].Value))) Read(Path.Combine(folder,file.Groups[1].Value),vertices,parts);
            Flush(vertices,parts);
            if(parts.Count==0) throw new InvalidDataException("No controller triangles in "+folder);
            return parts.ToArray();
        }
        private static void Read(string path,List<Vertex> vertices,List<ControllerModel.Part> parts)
        {
            var positions=new List<Vector3>();
            foreach(var line in File.ReadLines(path))
            {
                var f=line.Split(new[] {' '},StringSplitOptions.RemoveEmptyEntries);
                if(f.Length>=4 && f[0]=="v") positions.Add(new Vector3(P(f[1]),P(f[2]),P(f[3])));
                if(f.Length<4 || f[0]!="f") continue;
                var corners=f.Skip(1).Select(c=>positions[int.Parse(c.Split('/')[0])-1]).ToArray();
                for(int i=1;i+1<corners.Length;i++)
                {
                    var normal=Vector3.Normalize(Vector3.Cross(corners[i]-corners[0],corners[i+1]-corners[0]));
                    foreach(var p in new[] {corners[0],corners[i],corners[i+1]}) vertices.Add(new Vertex {Position=p,Normal=normal,Closed=p,ClosedNormal=normal});
                    if(vertices.Count>=65532) Flush(vertices,parts);
                }
            }
        }
        private static float P(string value) => float.Parse(value.Trim(),CultureInfo.InvariantCulture);
        private static void Flush(List<Vertex> vertices,List<ControllerModel.Part> parts)
        {
            if(vertices.Count==0) return;
            parts.Add(new ControllerModel.Part {Vertices=vertices.ToArray(),Indices=Enumerable.Range(0,vertices.Count).Select(i=>(ushort)i).ToArray(),Transform=Matrix.Identity});
            vertices.Clear();
        }
    }
}
#endif
