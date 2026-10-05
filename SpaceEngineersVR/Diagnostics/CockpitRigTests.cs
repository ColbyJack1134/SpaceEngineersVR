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
                if(rig.ActorCount==0) continue;
                var mesh=rig.Geometry(content);
                if(mesh.Parts.Length!=rig.ActorCount || !rig.Matches(rig.Model))
                    throw new Exception("Cockpit rig model or actor count mismatch: "+rig.Subtype);
                int copied=rig.Levers.Where(l=>l!=null && l.TemplateActor>=0).Sum(l=>mesh.Parts[l.Actor].Indices.Count+(l.TemplateBase?.Triangles ?? 0)*3);
                if((rig.IsFighter ? mesh.Parts.Take(6).Sum(p=>p.Indices.Count):mesh.Parts.Sum(p=>p.Indices.Count)-copied)!=mesh.NativeTriangles*3)
                    throw new Exception("Cockpit partition lost native triangles: "+rig.Subtype);
                foreach(var hand in new[] {true,false})
                {
                    var stick=hand ? rig.Left : rig.Right;
                    if(stick==null) continue;
                    var grip=stick.Palm(hand);
                    var cavity=Vector3.Transform(new Vector3(-.105f,-.035f,0),grip);
                    if(Vector3.Distance(cavity,stick.Contact+stick.Shaft*stick.GripLift+grip.Up*stick.GripInset)>.00001f || !grip.IsValid())
                        throw new Exception("Cockpit grip cavity missed handle: "+rig.Subtype);
                    var rest=stick.Visual(Vector3.Zero);
                    Vector3 expectedShaft=stick.Shaft;
                    if(Vector3.Distance(Vector3.TransformNormal(stick.Shaft,rest),expectedShaft)>.00001f)
                        throw new Exception("Cockpit stick neutral shaft changed unexpectedly: "+rig.Subtype);
                    if(Vector3.Distance(stick.Frame.Up,stick.Shaft)>.00001f)
                        throw new Exception("Cockpit stick input frame changed unexpectedly: "+rig.Subtype);
                    {
                        if(Vector3.Distance(Vector3.TransformNormal(stick.Shaft,stick.Visual(Vector3.Up)),stick.Shaft)>.00001f)
                            throw new Exception("Cockpit twist leans the shaft");
                        foreach(var grab in new[] {Matrix.Identity,Matrix.CreateFromYawPitchRoll(.7f,-.4f,.2f)})
                        foreach(var axis in new[] {Vector3.Right,Vector3.Up,Vector3.Backward})
                        foreach(float sign in new[] {-1f,1f})
                        {
                            var turn=Matrix.CreateFromAxisAngle(Vector3.TransformNormal(axis,stick.Frame),-sign*FighterProfile.Tilt);
                            var actual=CockpitStickMath.Rotation(grab,grab*turn,.08f,true,1,stick.Frame);
                            if(Vector3.Distance(actual,axis*sign)>.0002f)
                                throw new Exception("Cockpit shaft input axis or sign mismatch");
                            var thrust=CockpitStickMath.Translation(grab,grab*turn,.08f,true,1,stick.Frame);
                            if(Vector3.Distance(thrust,new Vector3(actual.Z,actual.Y,-actual.X))>.0002f)
                                throw new Exception("Cockpit shaft translation mismatch");
                        }
                    }
                    var visual=stick.Visual(new Vector3(.6f,-.3f,.5f));
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
                    if(handle.Hinged) drag.Begin(start,start,handle.Pivot,handle.Axis,0,handle.Range);
                    else drag.BeginLinear(start,handle.Axis,0,handle.Range);
                    drag.Move(Vector3.Transform(handle.Center,handle.Visual(.5f)));
                    if(Math.Abs(drag.Value-.5f)>.001f || surface.Width<=0 || surface.Height>.04f)
                        throw new Exception("Handle travel or contact surface differs from the model");
                    drag.Move(end); if(Math.Abs(drag.Value-1)>.001f) throw new Exception("Handle misses upper stop");
                    drag.Move(start); if(drag.Value>.001f) throw new Exception("Handle misses lower stop");
                    for(int i=0;i<=10;i++)
                    {
                        var visual=handle.Visual(i/10f);
                        var touch=surface.Pose*(MatrixD)visual;
                        var center=Vector3.Transform(handle.Center,visual);
                        if(Vector3D.Distance(touch.Translation,center+touch.Backward*handle.Radius)>.00001)
                            throw new Exception("Handle contact separates from the rendered grip");
                    }
                    foreach(bool left in new[] {true,false}) foreach(float offset in new[] {-.02f,0,.02f}) foreach(float position in new[] {0f,.5f,1f})
                    {
                        var palm=handle.Palm(left,position,offset);
                        var cavity=Vector3.Transform(new Vector3(-.105f,-.035f,0),palm);
                        var bar=Vector3.Transform(handle.Center+(Vector3)handle.TouchPose.Right*MathHelper.Clamp(offset,-handle.GripOffset,handle.GripOffset),handle.Visual(position));
                        if(Vector3.Distance(cavity,bar)>.00001f || !palm.IsValid())
                            throw new Exception("Bar grasp separates from the grip across hand/travel/offset");
                    }
                    int triangles=rig.Pieces.Where(p=>p.Actor==handle.Actor).Sum(p=>p.Triangles);
                    if(mesh.Parts[handle.Actor].Indices.Count!=triangles*3)
                        throw new Exception("Handle grip/stem partition differs from installed model");
                }
                log("PASS installed cockpit rig: "+rig.Subtype+"; "+mesh.Parts.Length+" actors, "+mesh.NativeTriangles+" conserved triangles");
            }
        }
    }
}
