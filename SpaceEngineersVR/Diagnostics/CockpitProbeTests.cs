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

            var surface=CockpitButtons.Preview(FighterProfile.Subtype,CockpitBarGeometry.Slot);
            var stick=FighterProfile.RightContact+new Vector3(-.121576294f,.007814497f,-.120211542f);
            foreach(float position in new[] {0f,.5f,1f}) foreach(float y in new[] {-.045f,0,.045f})
            {
                surface.Pose=CockpitBarGeometry.TouchPose;
                surface.Pose.Translation+=CockpitBarGeometry.Normal*(position*CockpitBarGeometry.Travel);
                var tip=Vector3D.Transform(new Vector3D(0,y,0),surface.Pose);
                var direction=Vector3D.Normalize(tip-stick);
                var probe=new CockpitProbe(MatrixD.CreateWorld(tip,direction,Vector3D.Up));
                int key=CockpitTouch.NearKey(surface,probe.Transform(MatrixD.Invert(surface.Pose)),out float distance,out var contact,.004f);
                Require(key==0 && distance<.00001 && Math.Abs(contact.Y-y)<.00001,"Bar end/center contact lost on seated joystick-side approach");
                Require(CockpitPanelGuard.Contains(FighterProfile.Subtype,probe),"Bar contact is not protected in every native state");
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
            log("PASS cockpit capsule: 500 independent distance checks, tangent/invalid/large-world cases, adjacent keys and all bar states approached from the saved joystick side.");
        }
    }
}
