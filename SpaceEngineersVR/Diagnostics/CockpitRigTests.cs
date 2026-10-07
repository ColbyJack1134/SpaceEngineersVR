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
                int copied=rig.Levers.Where(l=>l.TemplateActor>=0).Sum(l=>mesh.Parts[l.Actor].Indices.Count+(l.TemplateBase?.Triangles ?? 0)*3)+
                    rig.Buttons.Where(b=>b.TemplateActor>=0).Sum(b=>mesh.Parts[b.Actor].Indices.Count);
                if(mesh.Parts.Sum(p=>p.Indices.Count)-copied!=mesh.NativeTriangles*3)
                    throw new Exception("Cockpit partition lost native triangles: "+rig.Subtype);
                for(int actor=0;actor<mesh.Parts.Length;actor++)
                {
                    var part=mesh.Parts[actor];
                    if(part.Indices.Count!=part.Positions.Count || part.Normals.Count!=part.Positions.Count || part.Tangents.Count!=part.Positions.Count || part.TexCoords.Count!=part.Positions.Count)
                        throw new Exception("Cockpit runtime mesh channel counts differ: "+rig.Subtype+"/"+actor);
                    if(!part.Normals.All(v=>v.IsValid() && Math.Abs(v.Length()-1)<.001f))
                        throw new Exception("Cockpit runtime normal is not unit: "+rig.Subtype+"/"+actor);
                }
                // Installed tangents are passed through unchanged; only the extended bar stem is recomputed.
                if(rig.Bars.Any(b=>!mesh.Parts[b.Actor].Tangents.All(v=>v.IsValid() && Math.Abs(v.Length()-1)<.001f)))
                    throw new Exception("Extended bar stem has an invalid tangent: "+rig.Subtype);
                Levers(rig,mesh);
                foreach(var button in rig.Buttons.Where(b=>b.Actor>=0))
                {
                    var delta=Vector3.Transform(button.Center,button.Visual(true))-button.Center;
                    if(Vector3.Distance(delta,-button.Normal*button.Travel)>.00001f || button.Travel<=0 ||
                        mesh.Parts[button.Actor].Indices.Count!=(button.TemplateActor>=0 ? mesh.Parts[button.TemplateActor].Indices.Count:
                            rig.Pieces.Where(p=>p.Actor==button.Actor).Sum(p=>p.MovingTriangles)*3))
                        throw new Exception("Pushbutton stroke or cap partition differs from installed model: "+rig.Subtype+"/"+button.Actor+"; delta="+delta+"; indices="+mesh.Parts[button.Actor].Indices.Count);
                }
                foreach(var bar in rig.Bars)
                {
                    var stem=mesh.Parts[bar.Actor].Positions.Select(p=>Vector3.Dot(p,bar.Normal)).ToArray();
                    if(!stem.Any(d=>d<bar.StemDepth-bar.Travel*.5f))
                        throw new Exception("Pull bar stem was not extended into its housing: "+rig.Subtype);
                    if(!(stem.Min()+bar.Travel<bar.StemDepth))
                        throw new Exception("Pulled bar stem leaves its housing: "+rig.Subtype);
                    var pulled=Vector3.Transform(bar.Front,bar.Visual(1))-bar.Front;
                    if(Math.Abs(Vector3.Dot(pulled,bar.Normal)-bar.Travel)>.00001f || !bar.TouchPose.IsValid())
                        throw new Exception("Pull bar travel or contact differs from its rig data: "+rig.Subtype);
                }
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
                            var turn=Matrix.CreateFromAxisAngle(Vector3.TransformNormal(axis,stick.Frame),-sign*CockpitStickMath.TiltRange);
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
                    int triangles=rig.Pieces.Where(p=>p.Actor==handle.Actor).Sum(p=>p.MovingTriangles);
                    if(mesh.Parts[handle.Actor].Indices.Count!=triangles*3)
                        throw new Exception("Handle grip/stem partition differs from installed model");
                }
                log("PASS installed cockpit rig: "+rig.Subtype+"; "+mesh.Parts.Length+" actors, "+mesh.NativeTriangles+" conserved triangles");
            }
            Layout(log);
        }
        private static void Levers(CockpitRig rig,CockpitGeometry mesh)
        {
            foreach(var lever in rig.Levers)
            {
                if(Vector3.Distance(Vector3.Transform(lever.Pivot,lever.Visual(1)),lever.Pivot)>.00001f)
                    throw new Exception("Lever pivot moves: "+rig.Subtype);
                var off=Vector3.Transform(lever.Center,lever.Visual(0));
                if(!(Vector3.Dot(Vector3.Transform(lever.Center,lever.Visual(1))-off,lever.Up)>0))
                    throw new Exception("Lever flips away from up: "+rig.Subtype);
                if(lever.TemplateActor<0 && mesh.Parts[lever.Actor].Indices.Count!=rig.Pieces.Where(p=>p.Actor==lever.Actor).Sum(p=>p.MovingTriangles)*3)
                    throw new Exception("Lever cap partition includes its fixed bezel: "+rig.Subtype);
                var contact=Vector3.Transform(lever.Center+lever.Normal*lever.ContactOffset,lever.Visual(0));
                var radial=contact-lever.Pivot; radial-=lever.Axis*Vector3.Dot(radial,lever.Axis);
                radial=Vector3.Normalize(radial)*Math.Max(.035f,radial.Length());
                var drag=new ControlDrag();
                if(lever.FingerSlide)
                {
                    var target=new CockpitTouch.Target { Lever=true,FingerSlide=true };
                    if(target.Pinch || target.Hinged || !target.Draggable) throw new Exception("Fingertip slider selects a grip or hinge gesture");
                    drag.BeginLinear(contact,lever.SlideAxis,0,CockpitRig.Lever.FingerTravel);
                    foreach(var direction in new[] {lever.Normal,lever.Axis})
                    {
                        drag.Move(contact+direction*CockpitRig.Lever.FingerTravel);
                        if(drag.State || drag.Value>.001f) throw new Exception("Motion across a red control toggles its slide state");
                    }
                    drag.Move(contact+lever.Up*CockpitRig.Lever.FingerTravel);
                }
                else
                {
                    drag.Begin(contact,contact,lever.Pivot,lever.Axis,0,lever.AngularTravel);
                    drag.Move(contact+Vector3.TransformNormal(radial,Matrix.CreateFromAxisAngle(lever.Axis,lever.AngularTravel))-radial);
                }
                if(Math.Abs(drag.Value-1)>.001f || !drag.State) throw new Exception("Lever hand travel misses on detent: "+rig.Subtype);
                drag.Move(contact);
                if(drag.Value>.001f || drag.State) throw new Exception("Lever hand travel misses off detent: "+rig.Subtype);
                if(lever.CoverActor<0) continue;
                for(int step=0;step<=10;step++)
                {
                    var visual=lever.CoverVisual(step/10f);
                    if(Vector3.Distance(Vector3.Transform(lever.Hinge,visual),lever.Hinge)>.00001f || Math.Abs(visual.Determinant()-1)>.00001f || !lever.CoverPose(step/10f).IsValid())
                        throw new Exception("Cover hinge, rigidity or hit plane is invalid: "+rig.Subtype);
                }
            }
            // Open and closed cover models on the same panel must agree at both endpoints once their initial pose is applied.
            // A wrong initial pose is off by the full 65 degree travel; measured covers on one panel vary by a few degrees.
            Vector3 Face(CockpitRig.Lever lever)
            {
                var part=mesh.Parts[lever.CoverActor];
                Vector3 face=Vector3.Zero; float largest=0;
                for(int t=0;t<part.Indices.Count;t+=3)
                {
                    var a=part.Positions[part.Indices[t]];
                    var cross=Vector3.Cross(part.Positions[part.Indices[t+1]]-a,part.Positions[part.Indices[t+2]]-a);
                    float area=cross.Length();
                    if(area>largest && Math.Abs(Vector3.Dot(cross/area,lever.CoverAxis))<.2f) { largest=area; face=cross/area; }
                }
                if(largest==0) throw new Exception("Cover leaf face missing: "+rig.Subtype);
                return face;
            }
            var faces=rig.Levers.Where(l=>l.CoverActor>=0).ToDictionary(l=>l,Face);
            foreach(var lever in faces.Keys)
            foreach(var reference in faces.Keys.Where(l=>l.CoverInitial!=lever.CoverInitial && Vector3.Dot(l.CoverAxis,lever.CoverAxis)>Math.Cos(Math.PI/180) && Vector3.Dot(l.Normal,lever.Normal)>Math.Cos(Math.PI/180)))
            foreach(float endpoint in new[] {0f,1f})
            {
                var face=Vector3.TransformNormal(faces[lever],lever.CoverVisual(endpoint));
                var expected=Vector3.TransformNormal(faces[reference],reference.CoverVisual(endpoint));
                if(Math.Abs(Vector3.Dot(face,expected))<Math.Cos(MathHelper.ToRadians(10)))
                    throw new Exception("Open and closed cover models disagree at the same endpoint: "+rig.Subtype+" levers "+Array.IndexOf(rig.Levers,lever)+"/"+Array.IndexOf(rig.Levers,reference)+
                        " at "+endpoint+", "+MathHelper.ToDegrees((float)Math.Acos(Math.Min(1,Math.Abs(Vector3.Dot(face,expected)))))+" degrees");
            }
        }
        // Slot numbers and cover indices are persisted in toolbars, saves and multiplayer state.
        private static void Layout(Action<string> log)
        {
            var fighter=CockpitRig.Find(CockpitLayout.Fighter);
            if(fighter.Count!=42 || fighter.Levers.Length!=41 || fighter.BarAt(41)==null || fighter.Levers.Count(l=>l.CoverActor>=0)!=25)
                throw new Exception("Fighter slot layout changed");
            for(int slot=0;slot<fighter.Count;slot++)
                if(fighter.CoverIndex(slot)!=(slot<13 ? slot : slot>=21 && slot<33 ? slot-8 : -1))
                    throw new Exception("Fighter cover storage index changed: "+slot);
            var seat=CockpitRig.Find(CockpitLayout.ControlSeat);
            if(seat.Buttons.Length!=14 || seat.Count!=77 || seat.HandleAt(69)==null || seat.HandleAt(70)==null || seat.Handles.Length!=8)
                throw new Exception("Control Seat slot layout changed");
            foreach(var rig in CockpitRig.All.Where(r=>r!=fighter))
                for(int slot=0;slot<rig.Count;slot++)
                    if(rig.CoverIndex(slot)!=(rig.LeverAt(slot)?.CoverActor>=0 ? slot : -1))
                        throw new Exception("Cover storage index is not the slot: "+rig.Subtype);
            log("PASS persisted cockpit slot layout and cover storage indices");
        }
    }
}
