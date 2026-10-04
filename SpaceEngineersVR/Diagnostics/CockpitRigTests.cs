using System;
using System.IO;
using System.Linq;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Player.Control;
using SpaceEngineersVR.Multiplayer;
using VRageMath;
using VRageRender.Import;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitRigTests
    {
        internal static void Run(Action<string> log)
        {
            string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyModelImporter).Assembly.Location),"..","Content"));
            foreach(var rig in CockpitRig.All)
            {
                if(!SeatPanel.TryMount(rig.Subtype,out var mount,out float width,out float height) ||
                    !mount.IsValid() || Math.Abs(width-.108f)>.00001f || Math.Abs(height-.12f)>.00001f)
                    throw new Exception("Cockpit seat panel differs from the common module: "+rig.Subtype);
                if(!rig.HasSticks) continue;
                var mesh=rig.Geometry(content);
                int copied=rig.Levers.Where(l=>l!=null && l.TemplateActor>=0).Sum(l=>mesh.Parts[l.Actor].Indices.Count+(l.TemplateBase?.Triangles ?? 0)*3);
                if(mesh.Parts.Sum(p=>p.Indices.Count)-copied!=mesh.NativeTriangles*3)
                    throw new Exception("Cockpit partition lost native triangles: "+rig.Subtype);
                foreach(var hand in new[] {true,false})
                {
                    var stick=hand ? rig.Left : rig.Right;
                    if(stick==null) continue;
                    var grip=stick.Palm(hand);
                    var cavity=Vector3.Transform(new Vector3(-.105f,-.035f,0),grip);
                    if(Vector3.Distance(cavity,stick.Contact+stick.Shaft*stick.GripLift+grip.Up*stick.GripInset)>.00001f || !grip.IsValid())
                        throw new Exception("Cockpit grip cavity missed handle: "+rig.Subtype);
                    var visual=CockpitStickMath.Visual(stick.Pivot,new Vector3(.6f,-.3f,.5f));
                    if(Vector3.Distance(Vector3.Transform(stick.Pivot,visual),stick.Pivot)>.00001f)
                        throw new Exception("Cockpit stick pivot moves: "+rig.Subtype);
                }
                for(int slot=0;slot<CockpitLayout.Count(rig.Subtype);slot++)
                {
                    var handle=rig.HandleAt(slot);
                    if(AnalogControl.IsHandle(rig.Subtype,slot)!=(handle!=null))
                        throw new Exception("Host analog eligibility differs from authored handles: "+rig.Subtype+"/"+slot);
                    if(handle==null) continue;
                    var surface=CockpitButtons.Preview(rig.Subtype,slot);
                    var start=Vector3.Transform(handle.Center,handle.Visual(0));
                    var end=Vector3.Transform(handle.Center,handle.Visual(1));
                    var drag=new ControlDrag();
                    drag.BeginLinear(start,Vector3.Forward,0,CockpitRig.Handle.Travel);
                    drag.Move((start+end)*.5f);
                    if(Math.Abs(drag.Value-.5f)>.0001f || surface.Width<.1f || surface.Height>.04f)
                        throw new Exception("Handle travel or contact surface differs from the model");
                    drag.Move(end+Vector3.Forward); if(drag.Value!=1) throw new Exception("Handle exceeds forward stop");
                    drag.Move(start+Vector3.Backward); if(drag.Value!=0) throw new Exception("Handle exceeds rear stop");
                    for(int i=0;i<=10;i++)
                    {
                        var visual=handle.Visual(i/10f);
                        var touch=surface.Pose*(MatrixD)visual;
                        var center=Vector3.Transform(handle.Center,visual);
                        if(Vector3D.Distance(touch.Translation,center+Vector3.Up*.0155f)>.00001)
                            throw new Exception("Handle contact separates from the rendered grip");
                    }
                    foreach(bool left in new[] {true,false}) foreach(float offset in new[] {-.02f,0,.02f}) foreach(float position in new[] {0f,.5f,1f})
                    {
                        var palm=handle.Palm(left,position,offset);
                        var cavity=Vector3.Transform(new Vector3(-.105f,-.035f,0),palm);
                        var bar=Vector3.Transform(handle.Center+Vector3.Right*offset,handle.Visual(position));
                        if(Vector3.Distance(cavity,bar)>.00001f || Vector3.Dot(palm.Up,Vector3.Up)<.999f)
                            throw new Exception("Bar grasp twists or separates from the grip across hand/travel/offset");
                    }
                    if(mesh.Parts[handle.Actor].Indices.Count!=316*3)
                        throw new Exception("Handle grip/stem partition differs from installed model");
                }
                log("PASS installed cockpit rig: "+rig.Subtype+"; "+mesh.Parts.Length+" actors, "+mesh.NativeTriangles+" conserved triangles");
            }
        }
    }
}
