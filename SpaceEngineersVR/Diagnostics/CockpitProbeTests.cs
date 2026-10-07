using System;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitProbeTests
    {
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }
        internal static void Run(Action<string> log)
        {
            var random=new Random(7021);
            Func<Vector3D> point=()=>new Vector3D(random.NextDouble()*.2-.1,random.NextDouble()*.2-.1,random.NextDouble()*.2-.1);
            for(int i=0;i<500;i++)
            {
                var box=new BoundingBox(new Vector3(-.03f,-.02f,-.01f),new Vector3(.04f,.03f,i%2==0 ? -.01f : .02f));
                var probe=new CockpitProbe {Start=point(),End=point()};
                if(i%5==0) probe.End=probe.Start;
                float actual=probe.DistanceSquared(box,out var closest),reference=float.MaxValue;
                for(int j=0;j<=2000;j++)
                {
                    var p=(Vector3)Vector3D.Lerp(probe.Start,probe.End,j/2000d);
                    reference=Math.Min(reference,Vector3.DistanceSquared(p,Vector3.Clamp(p,box.Min,box.Max)));
                }
                Require(Math.Abs(actual-reference)<2e-8f,"Capsule distance disagrees with independent sampled segment");
                Require(Vector3.DistanceSquared(closest,Vector3.Clamp(closest,box.Min,box.Max))<1e-12f,"Closest contact lies outside target");
            }
            var face=new BoundingBox(new Vector3(-.02f,-.06f,0),new Vector3(.02f,.06f,0));
            Require(new CockpitProbe(MatrixD.CreateTranslation(0,0,-.0001)).Intersects(face),"Fingertip tangent misses face");
            Require(new CockpitProbe(MatrixD.CreateTranslation(0,0,.0029)).Intersects(face),"Fingertip approach tolerance misses the face");
            Require(!new CockpitProbe(MatrixD.CreateTranslation(0,0,.0031)).Intersects(face),"Fingertip approach tolerance reaches too far ahead");
            Require(!new CockpitProbe(MatrixD.CreateTranslation(double.NaN,0,0)).Intersects(face),"Invalid capsule enters control");

            var fighter=CockpitRig.Find(CockpitLayout.Fighter); var bar=fighter.Bars[0];
            var surface=CockpitButtons.Preview(CockpitLayout.Fighter,fighter.Count-1);
            var stick=fighter.Right.Contact+new Vector3(-.121576294f,.007814497f,-.120211542f);
            foreach(float position in new[] {0f,.5f,1f}) foreach(float y in new[] {-.045f,0,.045f})
            {
                surface.Pose=bar.TouchPose;
                surface.Pose.Translation+=bar.Normal*(position*bar.Travel);
                var tip=Vector3D.Transform(new Vector3D(0,y,0),surface.Pose);
                var direction=Vector3D.Normalize(tip-stick);
                var probe=new CockpitProbe(MatrixD.CreateWorld(tip,direction,Vector3D.Up));
                int key=CockpitTouch.NearKey(surface,probe.Transform(MatrixD.Invert(surface.Pose)),out float distance,out var contact,.004f);
                Require(key==0 && distance<.00001 && Math.Abs(contact.Y-y)<.00001,"Bar end/center contact lost on seated joystick-side approach");
                Require(CockpitPanelGuard.NearSurface(surface,probe),"Bar contact is not protected in every native state");
            }
            var panel=new SurfaceView {Style=SurfaceStyle.ModelControl,Width=.10f,Height=.10f,
                Keys=new[] {new SurfaceKey("Left",0,0,.45f,1),new SurfaceKey("Right",.55f,0,.45f,1)}};
            foreach(int side in new[] {-1,1})
            {
                var pointer=MatrixD.CreateTranslation(side*.012,0,0);
                int hit=CockpitTouch.NearKey(panel,new CockpitProbe(pointer),out _,out _);
                Require(hit==(side<0 ? 0 : 1),"Capsule selects neighboring control instead of nearest visible fingertip");
                var world=MatrixD.CreateFromYawPitchRoll(.7,.3,-.5)*MatrixD.CreateTranslation(1e6,-2e6,3e6);
                int moved=CockpitTouch.NearKey(panel,new CockpitProbe(pointer*world).Transform(MatrixD.Invert(world)),out _,out _);
                Require(hit==moved,"Capsule loses selection at rotated large-world coordinates");
            }
            RoundContacts(log);
            log("PASS cockpit capsule: 500 independent distance checks, tangent/invalid/large-world cases, adjacent keys and all bar states approached from the saved joystick side.");
        }
        private static void RoundContacts(Action<string> log)
        {
            var random=new Random(1723);
            var bounds=new BoundingBox(new Vector3(-.012f,-.012f,-.005f),new Vector3(.012f,.012f,.005f));
            for(int i=0;i<300;i++)
            {
                Func<Vector3D> point=()=>new Vector3D(random.NextDouble()*.1-.05,random.NextDouble()*.1-.05,random.NextDouble()*.1-.05);
                var a=point(); var b=i%5==0 ? a:point();
                float actual=SegmentDisk.DistanceSquared(a,b,bounds); double reference=double.MaxValue;
                for(int j=0;j<=2000;j++)
                {
                    var p=Vector3D.Lerp(a,b,j/2000d);
                    double radial=Math.Max(0,Math.Sqrt(p.X*p.X+p.Y*p.Y)-.012);
                    double axial=Math.Max(0,Math.Abs(p.Z)-.005);
                    reference=Math.Min(reference,radial*radial+axial*axial);
                }
                Require(Math.Abs(actual-reference)<2e-8,"Round cap distance disagrees with independent segment sampling");
            }
            Require(SegmentDisk.DistanceSquared(new Vector3D(double.NaN,0,0),Vector3D.Zero,bounds)==float.MaxValue,"Invalid segment enters round cap");
            int count=0;
            foreach(var rig in CockpitRig.All)
            for(int slot=0;slot<rig.Buttons.Length;slot++)
            {
                var button=rig.Buttons[slot]; if(!button.Round) continue;
                count++;
                var surface=CockpitButtons.Preview(rig.Subtype,slot);
                Require(Math.Abs(surface.Width-surface.Height)<1e-7,"Round cap is not circular");
                foreach(bool left in new[] {false,true}) foreach(double angle in new[] {0d,30d,45d,60d})
                foreach(double azimuth in new[] {0d,90d,180d,270d}) foreach(float state in new[] {0f,1f})
                foreach(float depth in new[] {-.006f,0f,.002f}) foreach(int axis in new[] {-1,0,1})
                {
                    surface.Pose=button.TouchPose*(MatrixD)button.Visual(state==1);
                    var pointer=MatrixD.CreateRotationY(angle*Math.PI/180)*MatrixD.CreateRotationZ(azimuth*Math.PI/180);
                    pointer.Translation=new Vector3D(axis*button.Size*.25,0,depth);
                    var local=new CockpitProbe(pointer,left);
                    Require(CockpitTouch.NearKey(surface,local,out float intended,out _)>=0,"Round cap misses centered/off-center approach: "+rig.Subtype+"/"+slot);
                    var world=local.Transform(surface.Pose);
                    Require(CockpitPanelGuard.NearSurface(surface,world),"Accepted round contact escapes input guard");
                    for(int other=0;other<rig.Buttons.Length;other++)
                    {
                        if(other==slot) continue;
                        var neighbor=CockpitButtons.Preview(rig.Subtype,other);
                        if(Vector3D.Distance(surface.Pose.Translation,neighbor.Pose.Translation)>.12) continue;
                        int hit=CockpitTouch.NearKey(neighbor,world.Transform(MatrixD.Invert(neighbor.Pose)),out float distance,out _);
                        Require(hit<0 || distance-.001f>intended,"Neighbor/hysteresis steals round-cap press: "+rig.Subtype+"/"+slot+" -> "+other);
                    }
                }
            }
            var cap=new SurfaceView {Style=SurfaceStyle.ModelControl,Width=.015f,Height=.015f,
                Keys=new[] {new SurfaceKey("",0,0,1,1) {Round=true}}};
            Require(CockpitTouch.NearKey(cap,new CockpitProbe(MatrixD.CreateTranslation(.012,.012,0)),out _,out _)>=0,"Off-center capsule contact rejected by fingertip disk check");
            var held=new CockpitTouch.Hand();
            held.Sample(true,0,false,"A",0); held.Sample(true,1,true,"A",0);
            held.Sample(true,1,true,"B",0);
            Require(held.Surface=="A" && !held.Pressed,"Round neighbor retargets or repeats held action");
            held.Sample(true,0,false,"B",0); held.Sample(true,1,true,"B",0);
            Require(held.Surface=="B" && held.Pressed,"Round neighbor cannot acquire after release");
            Require(count==56,"Round-contact regression inventory changed");
            log("PASS round contacts: 300 independent distances, all 56 caps at two travel states/depths/angles/both hands, guarded contact, neighbor ranking and held-action capture.");
        }
    }
}
