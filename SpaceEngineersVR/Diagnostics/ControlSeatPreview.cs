using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class ControlSeatPreview
    {
        private static double[] Elements(MatrixD m) => new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
        public static void Export(string game,string output,Action<string> log)
        {
            Directory.CreateDirectory(output); UiTests.Initialize(game,Path.Combine(output,"data"));
            var values=new Dictionary<string,double[]>();
            foreach(string subtype in new[] {CockpitLayout.ControlSeat,"OpenCockpitSmall","SmallBlockCockpitIndustrial","LargeBlockCockpitIndustrial"})
            {
                var rig=CockpitRig.Find(subtype);
                for(int i=0;i<rig.Count;i++)
                {
                    var button=rig.ButtonAt(i); var handle=rig.HandleAt(i);
                    if(button==null && handle==null) continue;
                    foreach(float state in new[] {0f,.5f,1f})
                    {
                        var visual=button!=null ? button.Visual(state==1):handle.Visual(state);
                        var contact=CockpitLayout.Control(subtype,i,out _)*(MatrixD)visual;
                        string name=subtype+"-"+i+"-"+(int)(state*100);
                        values[name+"-visual"]=Elements(visual); values[name+"-touch"]=Elements(contact);
                        if(button!=null || handle.Pinch)
                            foreach(bool left in new[] {true,false}) CockpitHandTests.ExportContact(output,name,left,contact,handle!=null);
                    }
                }
                if(subtype.Contains("Industrial"))
                {
                    for(int i=0;i<rig.Count;i++) values[subtype+"-control-"+i]=Elements(CockpitLayout.Control(subtype,i,out _));
                    foreach(bool left in new[] {true,false})
                    {
                        var stick=left ? rig.Left:rig.Right;
                        CockpitHandTests.ExportGrip(output,subtype,left,stick.Palm(left)*stick.Visual(Vector3.Zero),0,"-neutral");
                        values[subtype+"-grip-"+(left ? "L":"R")]=Elements(stick.Palm(left));
                    }
                    SeatPanel.TryMount(subtype,out var mount,out var width,out var height);
                    values[subtype+"-seat-mount"]=Elements(mount);
                    values[subtype+"-seat-size"]=new double[] {width,height};
                    values[subtype+"-coverage"]=new double[] {rig.Levers.Length,rig.Buttons.Length,rig.Handles.Length,rig.Bars.Length};
                }
                var guards=CockpitPanelGuard.Regions(rig);
                for(int i=0;i<guards.Length;i++)
                {
                    var region=guards[i];
                    var corners=new List<double>();
                    foreach(var corner in region.Bounds.GetCorners())
                    { var p=Vector3.Transform(corner,region.Frame); corners.Add(p.X); corners.Add(p.Y); corners.Add(p.Z); }
                    values[subtype+"-guard-"+i]=corners.ToArray();
                }
                values[subtype+"-counts"]=new double[] {rig.Count,rig.ActorCount,guards.Length};
            }
            using(var file=File.Create(Path.Combine(output,"control-seat-poses.json")))
                new DataContractJsonSerializer(values.GetType(),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,values);
            log("PASS Control Seat production transforms, guard bounds and installed hand contact previews exported.");
        }
    }
}
