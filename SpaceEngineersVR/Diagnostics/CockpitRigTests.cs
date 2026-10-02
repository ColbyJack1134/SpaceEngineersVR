using System;
using System.IO;
using System.Linq;
using SpaceEngineersVR.Player;
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
                log("PASS installed cockpit rig: "+rig.Subtype+"; "+mesh.Parts.Length+" actors, "+mesh.NativeTriangles+" conserved triangles");
            }
        }
    }
}
