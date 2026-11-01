#if SEVR_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    // Production cockpit geometry with added keys and sliders in fixed states, plus pinch contact poses for the sliders.
    public static class AddedControlsPreview
    {
        public static void Export(string game,string output,Action<string> log)
        {
            UiTests.Initialize(game,Path.Combine(output,"data"));
            var content=Path.GetFullPath(Path.Combine(game,"..","Content"));
            foreach(var rig in CockpitRig.All.Where(r=>r.AddedGroups.Length>0))
            {
                string folder=Path.Combine(output,rig.Subtype); Directory.CreateDirectory(folder);
                var mesh=rig.Geometry(content);
                int first=rig.Count-rig.AddedGroups.Sum(g=>g.Slots.Length);
                var states=new Dictionary<string,(bool pressed,bool lit,float slider)> {
                    {"rest",(false,false,.5f)},{"lit",(false,true,.5f)},{"pressed",(true,true,.5f)},{"slider-0",(false,false,0)},{"slider-100",(false,false,1)}};
                foreach(var state in states)
                {
                    var visual=new Matrix?[mesh.Parts.Length];
                    for(int a=0;a<visual.Length;a++) visual[a]=Matrix.Identity;
                    for(int slot=first;slot<rig.Count;slot++)
                    {
                        if(rig.ButtonAt(slot) is CockpitRig.Button button)
                        {
                            var pose=button.Visual(state.Value.pressed);
                            visual[button.Actor]=pose;
                            if(button.LitActor>=0) { visual[button.LitActor]=state.Value.lit ? pose:(Matrix?)null; if(state.Value.lit) visual[button.Actor]=null; }
                        }
                        else if(rig.HandleAt(slot) is CockpitRig.Handle handle) visual[handle.Actor]=handle.Visual(state.Value.slider);
                        else if(rig.BarAt(slot) is CockpitRig.Bar bar) visual[bar.Actor]=bar.Visual(state.Value.pressed ? 1:0);
                    }
                    foreach(var stick in new[] {rig.Left,rig.Right}.Where(s=>s!=null)) visual[stick.Actor]=stick.Visual(Vector3.Zero);
                    if(rig.NativeActor>=0) visual[rig.NativeActor]=null;
                    Write(Path.Combine(folder,state.Key+".json"),rig,mesh,visual);
                }
                for(int slot=first;slot<rig.Count;slot++)
                {
                    if(!(rig.HandleAt(slot) is CockpitRig.Handle handle)) continue;
                    foreach(float position in new[] {0f,1f})
                    {
                        var contact=CockpitLayout.Control(rig.Subtype,slot,out _)*(MatrixD)handle.Visual(position);
                        foreach(bool left in new[] {true,false})
                            CockpitHandTests.ExportContact(folder,"contact-"+slot+"-"+(int)(position*100),left,contact,true);
                    }
                }
                File.WriteAllText(Path.Combine(folder,"replaced.txt"),string.Join("\n",rig.Pieces.Select(p=>p.Material).Distinct()));
                File.WriteAllText(Path.Combine(folder,"slots.txt"),string.Join("\n",rig.AddedGroups.Select(g=>g.Name+"\t"+string.Join(",",g.Slots)))+"\n"+
                    string.Join("\n",Enumerable.Range(first,rig.Count-first).Select(s=>s+"\t"+Describe(rig,s))));
                log(rig.Subtype+": "+(rig.Count-first)+" added controls exported");
            }
        }
        private static string Describe(CockpitRig rig,int slot)
        {
            var pose=CockpitLayout.Control(rig.Subtype,slot,out float size);
            var p=pose.Translation; string at=p.X.ToString("F4")+","+p.Y.ToString("F4")+","+p.Z.ToString("F4");
            if(rig.HandleAt(slot) is CockpitRig.Handle h)
            {
                var b=Vector3.Transform(h.Center,h.Visual(0)); var t=Vector3.Transform(h.Center,h.Visual(1));
                return "slider\t"+at+"\t"+b.X.ToString("F4")+","+b.Y.ToString("F4")+","+b.Z.ToString("F4")+"\t"+t.X.ToString("F4")+","+t.Y.ToString("F4")+","+t.Z.ToString("F4");
            }
            return "button\t"+at;
        }
        private static void Write(string path,CockpitRig rig,CockpitGeometry mesh,Matrix?[] visual)
        {
            var vertices=new List<float[]>(); var uv=new List<float[]>(); var parts=new List<ModelInspection.Part>();
            for(int actor=0;actor<mesh.Parts.Length;actor++)
            {
                if(!visual[actor].HasValue) continue;
                var source=mesh.Parts[actor]; int first=vertices.Count;
                foreach(var position in source.Positions) { var p=Vector3.Transform(position,visual[actor].Value); vertices.Add(new[] {p.X,p.Y,p.Z}); }
                foreach(var tex in source.TexCoords) uv.Add(new[] {tex.X,tex.Y});
                parts.AddRange(source.Sections.Select(section=>new ModelInspection.Part { Material=section.MaterialName,
                    Indices=source.Indices.Skip(section.IndexStart).Take(section.TriCount*3).Select(i=>i+first).ToArray() }));
            }
            var report=new ModelInspection.Report { Model=rig.Model,Vertices=vertices.ToArray(),UV=uv.ToArray(),Parts=parts.ToArray() };
            using(var file=File.Create(path)) new DataContractJsonSerializer(typeof(ModelInspection.Report)).WriteObject(file,report);
        }
    }
}
#endif
