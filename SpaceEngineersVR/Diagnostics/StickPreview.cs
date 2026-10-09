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
            if(rig==null || !rig.HasSticks && rig.Wheel==null) throw new ArgumentException("Choose a cockpit rig with steering or sticks",nameof(subtype));
            Directory.CreateDirectory(output);
            var content=Path.GetFullPath(Path.Combine(gameBin,"..","Content"));
            var mesh=rig.Geometry(content);
            var poses=new Dictionary<string,Vector3> { {"neutral",Vector3.Zero},{"pitch-back",Vector3.Left},{"pitch-forward",Vector3.Right},
                {"roll-left",Vector3.Forward},{"roll-right",Vector3.Backward},{"twist-left",Vector3.Down},{"twist-right",Vector3.Up},
                {"diagonal",Vector3.One} };
            if(rig.Wheel==null)
            {
                var tuning=new Multiplayer.FlightTuning();
                var half=CockpitStickMath.Rotation(Matrix.Identity,Matrix.CreateRotationX(-CockpitStickMath.TiltRange*.5f),tuning.TiltDeadzone,false);
                poses.Add("half-travel",CockpitStickMath.CommandVisual(half,tuning.RotationCurve,false));

            }
            var tilts=new Dictionary<string,Vector2>();
            var yokes=new Dictionary<string,Vector2>();
            if(rig.Wheel!=null)
            {
                poses.Clear();
                var steering=new CockpitSteering(); var wheel=rig.Wheel;
                steering.Update(wheel,true,wheel.LeftContact,false,wheel.RightContact,0);
                poses.Add("wheel-000",Vector3.Zero);
                for(int frame=1;frame<=12;frame++)
                {
                    steering.Update(wheel,true,wheel.Contact(true,1),false,wheel.RightContact,1f/60);
                    if(frame<=4 || frame==8 || frame==12) poses.Add("wheel-"+(frame*1000/60).ToString("D3"),new Vector3(steering.VisualPosition,0,0));
                }
                poses.Add("wheel-left-stop",new Vector3(1,0,0));
                poses.Add("wheel-right-stop",new Vector3(-1,0,0));
                if(wheel.ThrottleActor<0)
                {
                    yokes.Add("yoke-neutral",Vector2.Zero);
                    yokes.Add("yoke-pitch-up",new Vector2(0,1));
                    yokes.Add("yoke-pitch-down",new Vector2(0,-1));
                    yokes.Add("yoke-roll-right",new Vector2(-.5f,0));
                    yokes.Add("yoke-combined",new Vector2(-.5f,1));
                    foreach(var pose in yokes) poses.Add(pose.Key,Vector3.Zero);
                }
                if(wheel.ThrottleActor>=0)
                {
                    var tuning=new Multiplayer.FlightTuning();
                    tilts.Add("bar-neutral",Vector2.Zero);
                    tilts.Add("bar-pitch-up",new Vector2(tuning.BarPitchTravel,0));
                    tilts.Add("bar-pitch-down",new Vector2(-tuning.BarPitchTravel,0));
                    tilts.Add("bar-roll-left",new Vector2(0,-tuning.BarRollTravel));
                    tilts.Add("bar-roll-right",new Vector2(0,tuning.BarRollTravel));
                    tilts.Add("bar-yaw-roll-gas",new Vector2(tuning.BarPitchTravel,tuning.BarRollTravel));
                    foreach(var tilt in tilts) poses.Add(tilt.Key,tilt.Key=="bar-yaw-roll-gas" ? new Vector3(.6f,.6f,0):Vector3.Zero);
                    poses.Add("throttle-full",new Vector3(0,1,0));
                    var pair=new CockpitBarTilt();
                    pair.Update(true,wheel.LeftContact,wheel.RightContact,tuning,0);
                    var target=CockpitStickMath.Around((wheel.LeftContact+wheel.RightContact)*.5f,Matrix.CreateRotationZ(-tuning.BarRollTravel))*Matrix.CreateTranslation(0,tuning.BarPitchTravel,0);
                    for(int frame=0;frame<=12;frame++)
                    {
                        if(frame>0) pair.Update(true,Vector3.Transform(wheel.LeftContact,target),Vector3.Transform(wheel.RightContact,target),tuning,1f/60);
                        if(frame<=4 || frame==8 || frame==12)
                        {
                            string name="bar-"+(frame*1000/60).ToString("D3");
                            poses.Add(name,Vector3.Zero); tilts.Add(name,pair.Visual);
                        }
                    }
                }
            }
            foreach(var pose in poses)
            {
                tilts.TryGetValue(pose.Key,out Vector2 tilt);
                bool yoke=yokes.TryGetValue(pose.Key,out Vector2 yokePose);
                var byActor=new Dictionary<int,List<ModelInspection.Part>>();
                var vertices=new List<float[]>(); var uv=new List<float[]>(); var parts=new List<ModelInspection.Part>();
                for(int actor=0;actor<mesh.Parts.Length;actor++)
                {
                    var source=mesh.Parts[actor];
                    var stick=rig.Left?.Actor==actor ? rig.Left : rig.Right?.Actor==actor ? rig.Right:null;
                    Matrix visual=stick?.Visual(pose.Value) ?? Matrix.Identity;
                    if(rig.Wheel!=null)
                    {
                        if(actor==rig.Wheel.Actor) visual=yoke ? rig.Wheel.YokeVisual(yokePose.X,yokePose.Y):rig.Wheel.Visual(pose.Value.X,tilt);
                        else if(actor==rig.Wheel.ThrottleActor) visual=rig.Wheel.ThrottleVisual(pose.Value.Y)*rig.Wheel.Visual(pose.Value.X,tilt);
                    }
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
                    if(rig.Wheel!=null)
                    {
                        CockpitHandTests.ExportGrip(output,subtype,left,yoke ? rig.Wheel.YokePalm(left,yokePose.X,yokePose.Y):rig.Wheel.Palm(left,pose.Value.X,pose.Value.Y,tilt),0,"-"+pose.Key);
                        continue;
                    }
                    if(stick==null) continue;
                    report.Parts=byActor.Where(a=>stick.Actor==a.Key || stick.BaseActor==a.Key).SelectMany(a=>a.Value).ToArray();
                    using(var file=File.Create(Path.Combine(output,(left ? "L-":"R-")+pose.Key+".json")))
                        new DataContractJsonSerializer(typeof(ModelInspection.Report)).WriteObject(file,report);
                    Matrix visual=stick.Visual(pose.Value);
                    CockpitHandTests.ExportGrip(output,subtype,left,stick.Palm(left)*visual,0,"-"+pose.Key);
                }
            }
        }
    }
}
