#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class Warfare3InteriorPreview
    {
        public static void Export(string game,string output,Action<string> log)
        {
            UiTests.Initialize(game,Path.Combine(output,"data"));
            string content=Path.GetFullPath(Path.Combine(game,"..","Content"));
            foreach(string subtype in new[] {"FighterCockpitType2","LargeBlockCaptainChair"})
            {
                var rig=CockpitRig.Find(subtype); var mesh=rig.Geometry(content);
                if(subtype=="FighterCockpitType2") CockpitRigTests.Fighter2Contacts(rig,mesh,log);
                string folder=Path.Combine(output,subtype); Directory.CreateDirectory(folder);
                foreach(float state in new[] {0f,.5f,1f})
                {
                    string name=(state*100).ToString("F0");
                    var visual=Enumerable.Repeat<Matrix?>(Matrix.Identity,mesh.Parts.Length).ToArray();
                    if(rig.NativeActor>=0) visual[rig.NativeActor]=null;
                    for(int slot=0;slot<rig.Count;slot++)
                    {
                        var button=rig.ButtonAt(slot); var lever=rig.LeverAt(slot); var handle=rig.HandleAt(slot);
                        Matrix pose=button!=null ? button.Visual(state==1) : lever!=null ? lever.Visual(state) : handle.Visual(state);
                        int actor=button!=null ? button.Actor : lever!=null ? lever.Actor : handle.Actor;
                        visual[actor]=pose;
                        if(button?.LitActor>=0)
                        {
                            visual[button.LitActor]=state==1 ? pose:(Matrix?)null;
                            if(state==1) visual[actor]=null;
                        }
                        var contact=CockpitLayout.Control(subtype,slot,out _);
                        var off=Vector3D.Transform(contact.Translation,lever?.Visual(0) ?? Matrix.Identity);
                        var point=Vector3D.Transform(contact.Translation,pose);
                        foreach(bool left in new[] {true,false})
                        {
                            // Sample seated approach; keep wrist roll fixed during the drag.
                            var start=lever==null ? contact:MatrixD.CreateWorld(off,
                                Vector3D.Normalize(off-new Vector3D(left ? -.18:.18,.2,.3)),Vector3D.Up);
                            start.Translation=point;
                            if(handle!=null && !handle.Pinch)
                                CockpitHandTests.ExportGrip(folder,"handle-"+slot,left,handle.Palm(left,state),1,"-"+name);
                            else CockpitHandTests.ExportContact(folder,"contact-"+slot+"-"+name,left,start,lever!=null || handle!=null);
                        }
                    }
                    AddedControlsPreview.Write(Path.Combine(folder,"state-"+name+".json"),rig,mesh,visual);
                }
                foreach(bool left in new[] {true,false})
                {
                    var stick=left ? rig.Left:rig.Right;
                    if(stick!=null)
                    {
                        foreach(float trigger in new[] {0f,1f})
                            CockpitHandTests.ExportGrip(folder,"neutral",left,stick.Palm(left),trigger,trigger==0 ? "":"-pressed",stick.Curl);
                        var motions=new[] {Vector3.Right,Vector3.Left,Vector3.Up,Vector3.Down,Vector3.Forward,Vector3.Backward};
                        for(int motion=0;motion<motions.Length;motion++)
                        {
                            var pose=stick.Visual(motions[motion]);
                            CockpitHandTests.ExportGrip(folder,"motion-"+motion,left,stick.Palm(left)*pose,1,"",stick.Curl);
                            var visual=Enumerable.Repeat<Matrix?>(Matrix.Identity,mesh.Parts.Length).ToArray();
                            visual[stick.Actor]=pose;
                            if(rig.NativeActor>=0) visual[rig.NativeActor]=null;
                            foreach(var button in rig.AllButtons.Where(b=>b.LitActor>=0)) visual[button.LitActor]=null;
                            AddedControlsPreview.Write(Path.Combine(folder,"motion-"+motion+"-"+(left ? "L":"R")+".json"),rig,mesh,visual);
                        }
                    }
                    // Representative tracked rest samples, not runtime hand snap points.
                    var rest=subtype=="FighterCockpitType2" ? new Vector3(left ? -.49f:.49f,.015f,.32f) : new Vector3(left ? -.41f:.41f,-.305f,.06f);
                    CockpitHandTests.ExportFree(folder,"rest",left,CockpitStickMath.GripPalm(left,rest,left ? Vector3.Right:Vector3.Left));
                }
                File.WriteAllText(Path.Combine(folder,"replaced.txt"),string.Join("\n",rig.Pieces.Select(p=>p.Material).Distinct()));
                File.WriteAllText(Path.Combine(folder,"slots.txt"),string.Join("\n",CockpitAssignmentLayout.Find(subtype).Pages.Select(g=>g.Name+"\t"+string.Join(",",g.Controls))));
                log("PASS "+subtype+": production control geometry and both hands exported at rest, contacts and travel endpoints.");
            }
        }
    }
}
#endif
