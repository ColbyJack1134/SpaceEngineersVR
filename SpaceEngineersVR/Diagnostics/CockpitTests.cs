using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using VRageMath;
using VRageRender;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitTests
    {
        private static void Require(bool value,string message) { if (!value) throw new Exception(message); }
        private static void Near(Vector3 a,Vector3 b,string message) => Require(Vector3.Distance(a,b)<0.0002f,message+": "+a+" != "+b);
        public static void Run(Action<string> log)
        {
            const float deadzone=0.12f;
            Matrix neutral=Matrix.CreateFromYawPitchRoll(0.7f,-0.45f,0.18f);
            neutral.Translation=new Vector3(0.31f,-0.23f,0.37f);
            Near(CockpitStickMath.Rotation(neutral,neutral,deadzone,true),Vector3.Zero,"Neutral rotation kicks");
            Near(CockpitStickMath.Translation(neutral,neutral,deadzone,true),Vector3.Zero,"Neutral translation kicks");
            foreach (int sign in new[] {-1,1})
            {
                Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationZ(-sign*FighterProfile.Tilt),deadzone,true),sign*Vector3.Right,"Tilt strafe sign/axis");
                Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationY(-sign*FighterProfile.Twist),deadzone,true),sign*Vector3.Up,"Clockwise twist must lift");
                Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationX(-sign*FighterProfile.Tilt),deadzone,true),sign*Vector3.Forward,"Forward tilt must move forward");
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationX(sign*FighterProfile.Tilt),deadzone,true),-sign*Vector3.Right,"Pitch sign/axis");
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationY(sign*FighterProfile.Twist),deadzone,true),-sign*Vector3.Up,"Yaw twist sign/axis");
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationZ(sign*FighterProfile.Tilt),deadzone,true),-sign*Vector3.Backward,"Roll tilt sign/axis");
            }
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationY(-1.1f),deadzone,true),Vector3.Up,"Twist not clamped");
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationX(0.01f),deadzone,true),Vector3.Zero,"Translation deadzone");
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateTranslation(0.1f,-0.2f,0.15f),deadzone,true),Vector3.Zero,"Arm displacement drives thrust");
            Require(Vector3.TransformNormal(Vector3.Forward,Matrix.CreateRotationY(-0.1f)).X>0,"Clockwise convention must turn forward toward right from above");
            Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationX(0.01f),deadzone,true),Vector3.Zero,"Rotation deadzone");
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationY(-FighterProfile.Twist),deadzone,false),Vector3.Zero,"Disabled twist lifts");
            Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationY(FighterProfile.Twist),deadzone,false),Vector3.Zero,"Disabled twist yaws");
            Require(CockpitStickMath.Tilt(new Vector2(1),deadzone)==Vector2.One,"Full diagonal tilt cannot reach both limits");
            Require(Math.Abs(CockpitStickMath.Response(.2f))<.2f && CockpitStickMath.Response(-1)==-1,"Center response is not softened");
            {
                Vector3 move=Vector3.Zero; Vector2 rotate=Vector2.Zero; float roll=0;
                CockpitStickMath.ApplyFlight(true,true,new Vector3(0,0,-1),Vector3.Zero,1,.5f,10,.6f,ref move,ref rotate,ref roll);
                Near(move,new Vector3(0,1,-1),"Strong tilt must reach full thrust; thumb must lift");
                Require(rotate==new Vector2(0,5) && roll==0,"Thumb yaw sign or scale");
            }
            foreach (bool l in new[] {false,true})
            foreach (bool r in new[] {false,true})
            {
                Vector3 move=new Vector3(0.2f,0.3f,0.4f);
                Vector2 rotate=new Vector2(2,3); float roll=0.7f;
                CockpitStickMath.ApplyFlight(l,r,Vector3.One,-Vector3.One,0,0,10,0.6f,ref move,ref rotate,ref roll);
                Near(move,l ? Vector3.One : new Vector3(0.2f,0.3f,0.4f),"Translation has two input owners");
                Require(rotate==(r ? new Vector2(-10) : new Vector2(2,3)) && Math.Abs(roll-(r ? -3f : 0.7f))<1e-6,"Rotation ownership or ship roll scaling changed");
                CockpitStickMath.ApplyFlight(l,r,Vector3.Zero,Vector3.Zero,0,0,10,0.6f,ref move,ref rotate,ref roll);
                if (l) Near(move,Vector3.Zero,"Released physical translation leaks held fallback");
                if (r) Require(rotate==Vector2.Zero && roll==0,"Released physical rotation leaks held fallback");
            }
            for (int i=0;i<300;i++)
            {
                MatrixD ship=MatrixD.CreateFromYawPitchRoll(i*0.1f,i*0.021f,i*0.045f);
                ship.Translation=new Vector3D(2000000+i*170,-7000000,3000000-i*94);
                Matrix local=neutral*Matrix.CreateRotationX(0.19f);
                local.Translation+=new Vector3(0.035f,0.018f,-0.072f);
                Matrix recovered=(Matrix)((MatrixD)local*ship*MatrixD.Invert(ship));
                Near(CockpitStickMath.Rotation(neutral,recovered,deadzone,true),CockpitStickMath.Rotation(neutral,local,deadzone,true),"Moving ship drives rotation");
                Near(CockpitStickMath.Translation(neutral,recovered,deadzone,true),CockpitStickMath.Translation(neutral,local,deadzone,true),"Moving ship drives translation");
                Vector3 axes=new Vector3((float)Math.Sin(i),(float)Math.Sin(i*0.3),(float)Math.Cos(i));
                Matrix left=CockpitStickMath.LeftVisual(axes);
                Matrix right=CockpitStickMath.RightVisual(axes);
                Require(left.IsValid() && right.IsValid() && Math.Abs(left.Determinant()-1)<0.0001f,"Invalid/elongated control articulation");
                Near(Vector3.Transform(FighterProfile.LeftPivot,left),FighterProfile.LeftPivot,"Left pivot moved");
                Near(Vector3.Transform(FighterProfile.RightPivot,right),FighterProfile.RightPivot,"Right pivot moved");
            }
            var config=new PluginConfig();
            Require(config.FighterCockpitSticks && config.StickTwist && config.PhysicalStickSensitivity==1 && config.PhysicalStickDeadzone==deadzone,"Old-config stick defaults");
            config.PhysicalStickDeadzone=float.NaN; config.PhysicalStickSensitivity=float.PositiveInfinity;
            Require(config.PhysicalStickSensitivity==1 && config.PhysicalStickDeadzone==deadzone,"Invalid comfort options");
            config.PhysicalStickDeadzone=9; config.PhysicalStickSensitivity=-8;
            Require(config.PhysicalStickSensitivity==0.25f && config.PhysicalStickDeadzone==0.35f,"Unbounded comfort options");
            Require(config.PhysicalStickExponent==2 && config.PhysicalStickSmoothing==.025f,"Missing stick comfort defaults");
            config.PhysicalStickExponent=float.NaN; config.PhysicalStickSmoothing=float.PositiveInfinity;
            Require(config.PhysicalStickExponent==2 && config.PhysicalStickSmoothing==.025f,"Invalid stick curve or smoothing");
            foreach(float exponent in new[] {1f,2f,3f})
            {
                float previous=0;
                for(int i=0;i<=100;i++)
                {
                    float x=i/100f,y=CockpitStickMath.Response(x,exponent);
                    Require(y>=previous && y<=1 && CockpitStickMath.Response(-x,exponent)==-y,"Curve loses monotonicity, symmetry or limits"); previous=y;
                }
                Require(previous==1,"Curve loses full authority");
            }
            Require(CockpitStickMath.Response(.5f)==.25f && CockpitStickMath.Response(.75f)==.5625f,"Squared response changed");
            foreach(float sensitivity in new[] {.5f,1f,2f})
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationX(FighterProfile.Tilt/sensitivity),deadzone,true,sensitivity),-Vector3.Right,"Sensitivity limits maximum authority");
            Vector3 FilterAt(int hz)
            {
                var f=new CockpitStickMath.Filter(); Vector3 result=Vector3.Zero;
                for(int i=0;i<hz;i++) result=f.Update(true,new Vector3(.5f),1f/hz,.025f);
                return result;
            }
            Near(FilterAt(72),FilterAt(144),"Smoothing depends on refresh rate");
            var filter=new CockpitStickMath.Filter();
            var smooth=filter.Update(true,new Vector3(.5f),.01f,.025f);
            Require(smooth.X>0 && smooth.X<.5f,"Smoothing bypassed");
            Near(filter.Update(true,new Vector3(0,1,-1),.01f,.025f),new Vector3(0,1,-1),"Smoothing delays neutral or full travel");
            Near(filter.Update(false,Vector3.One,.01f,.025f),Vector3.Zero,"Smoothing leaks after release");
            var pulse=new CockpitFeedback.StickPulse(); var now=DateTime.UtcNow;
            Require(pulse.Sample(true,Vector3.Zero,now)==0,"Grab emitted a center pulse");
            pulse.Sample(true,new Vector3(.4f,.4f,0),now.AddMilliseconds(100));
            Require(pulse.Sample(true,new Vector3(0,.4f,0),now.AddMilliseconds(110))==0,"One centered axis reported full neutral");
            Require(pulse.Sample(true,Vector3.Zero,now.AddMilliseconds(120))==1,"Full neutral has no pulse");
            for(int i=0;i<20;i++) Require(pulse.Sample(true,new Vector3(i%2==0 ? .02f:0),now.AddMilliseconds(140+i*10))==0,"Neutral jitter repeats pulses");
            Require(pulse.Sample(true,Vector3.Right,now.AddSeconds(1))==2,"End stop has no pulse");
            pulse.Sample(true,Vector3.Right*.95f,now.AddSeconds(1.1));
            Require(pulse.Sample(true,Vector3.Right,now.AddSeconds(1.3))==0,"Limit jitter repeats pulses");
            Require(pulse.Sample(false,Vector3.Zero,now.AddSeconds(1.4))==0 && pulse.Sample(true,Vector3.Zero,now.AddSeconds(1.5))==0,"Release or regrab emits a detent");
            log("PASS stick comfort: squared response, full authority, refresh-independent smoothing, immediate release/neutral, multi-axis center and limit hysteresis.");
            string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyRenderProxy).Assembly.Location),"..","Content"));
            var geometry=CockpitGeometry.Load(content);
            Require(geometry.Parts.Take(6).Sum(p=>p.Indices.Count)==568*3,"Native stick triangles lost or duplicated");
            int coverBase=7+CockpitSwitchGeometry.Count;
            Require(geometry.Parts.Length==coverBase+2+CockpitCoverGeometry.Count && geometry.Parts.Skip(6).Take(CockpitSwitchGeometry.Count+1).Sum(p=>p.Indices.Count)==4046*3,"Chrome triangles lost or duplicated");
            for(int i=0;i<CockpitSwitchGeometry.Count;i++)
            {
                Require(geometry.Parts[7+i].Indices.Count==56*3,"Missing isolated lever");
                var visual=CockpitSwitchGeometry.Visual(i,1);
                Near(Vector3.Transform(CockpitSwitchGeometry.Pivots[i],visual),CockpitSwitchGeometry.Pivots[i],"Switch pivot moves");
                var off=Vector3.Transform(CockpitSwitchGeometry.Centers[i],CockpitSwitchGeometry.Visual(i,0));
                Require(Vector3.Dot(Vector3.Transform(CockpitSwitchGeometry.Centers[i],visual)-off,CockpitSwitchGeometry.UpFor(i))>0,"Switch flips away from up");
            }
            Require(geometry.Parts.Skip(coverBase).Sum(p=>p.Indices.Count)==4126*3,"Cover material triangles lost or duplicated");
            var barMesh=geometry.Parts.Last();
            Require(barMesh.Indices.Count==70*3,"Striped bar is not isolated from its plate");
            var cap=barMesh.Positions.Where(p=>Vector3.Dot(p,CockpitBarGeometry.Normal)>-.49f);
            float capBack=cap.Min(p=>Vector3.Dot(Vector3.Transform(p,CockpitBarGeometry.Visual(1)),CockpitBarGeometry.Normal));
            Require(capBack>-.49798227f+.03f,"Pulled striped cap does not clear the installed mounting plate");
            float stemBase=barMesh.Positions.Min(p=>Vector3.Dot(Vector3.Transform(p,CockpitBarGeometry.Visual(1)),CockpitBarGeometry.Normal));
            Require(stemBase<-.49798227f && stemBase>-.500f,"Extended stem detaches from its mounting plate");
            Vector3 CoverFace(int index)
            {
                var mesh=geometry.Parts[coverBase+1+index];
                var axis=CockpitSwitchGeometry.AxisFor(CockpitCoverGeometry.Slot(index));
                Vector3 face=Vector3.Zero; float largest=0;
                for(int t=0;t<mesh.Indices.Count;t+=3)
                {
                    var a=mesh.Positions[mesh.Indices[t]];
                    var cross=Vector3.Cross(mesh.Positions[mesh.Indices[t+1]]-a,mesh.Positions[mesh.Indices[t+2]]-a);
                    float area=cross.Length();
                    if(area>largest && Math.Abs(Vector3.Dot(cross/area,axis))<.2f) { largest=area; face=cross/area; }
                }
                Require(largest>0,"Cover leaf face missing");
                return face;
            }
            for(int i=0;i<CockpitCoverGeometry.Count;i++) foreach(float endpoint in new[] {0f,1f})
            {
                int reference=i<13 ? 9 : 13;
                var face=Vector3.TransformNormal(CoverFace(i),CockpitCoverGeometry.Visual(i,endpoint));
                var expected=Vector3.TransformNormal(CoverFace(reference),CockpitCoverGeometry.Visual(reference,endpoint));
                Require(Math.Abs(Vector3.Dot(face,expected))>Math.Cos(Math.PI/180),"Native cover variants disagree at the same endpoint");
            }
            for(int i=0;i<CockpitCoverGeometry.Count;i++) for(int step=0;step<=10;step++)
            {
                float openness=step/10f;
                var visual=CockpitCoverGeometry.Visual(i,openness);
                Near(Vector3.Transform(CockpitCoverGeometry.Hinges[i],visual),CockpitCoverGeometry.Hinges[i],"Cover hinge moves");
                Require(CockpitCoverGeometry.TouchPose(i,openness).IsValid(),"Invalid cover hit plane");
                Require(Math.Abs(visual.Determinant()-1)<.00001,"Cover stretches while rotating");
            }
            foreach (var mesh in geometry.Parts)
            {
                Require(mesh.Indices.Count==mesh.Positions.Count && mesh.Normals.Count==mesh.Positions.Count && mesh.TexCoords.Count==mesh.Positions.Count,"Runtime mesh channels mismatch");
                Require(mesh.Normals.All(n=>n.IsValid() && Math.Abs(n.Length()-1)<0.001f),"Invalid mesh normals");
                Require(mesh.Tangents.All(n=>n.IsValid() && Math.Abs(n.Length()-1)<0.001f),"Invalid mesh tangents");
            }
            var convert=AccessTools.Method(AccessTools.TypeByName("VRageRender.MyProxiesFactory"),"GetRenderableProxyFlags");
            long Flags(RenderFlags f) => Convert.ToInt64(convert.Invoke(null,new object[] { f }));
            long hide=Flags(RenderFlags.Visible|CockpitRender.Hidden),none=Flags(RenderFlags.Visible);
            Require(none==0 && hide!=0,"Installed flag mapping changed");
            foreach (RenderFlags initial in new[] {RenderFlags.Visible,RenderFlags.Visible|RenderFlags.CastShadows,RenderFlags.Visible|RenderFlags.NoBackFaceCulling})
            {
                long original=Flags(initial);
                long hidden=(original|hide)&~none;
                long restored=(hidden|none)&~hide;
                Require((hidden&hide)==hide && restored==original,"Native material flags do not hide/restore reversibly");
            }
            var renderable=AccessTools.TypeByName("VRage.Render11.Scene.Components.MyRenderableComponent");
            Require(AccessTools.Property(renderable,"Owner")!=null && AccessTools.Property(renderable,"Lods")!=null,"Renderer verification accessors unavailable");
            var info=AccessTools.Property(typeof(MyMeshMaterialId),"Info");
            Require(info!=null && AccessTools.Field(info.PropertyType,"Name")!=null,"Native material name cannot be verified");
            log("PASS fighter sticks: tilt/twist translation, clockwise lift, no thrust from arm displacement, 6-axis signs/deadzones/clamps, neutral capture, 300 moving large-coordinate ship frames, fixed rigid pivots; installed native mesh partition (568 triangles), normals/UVs and reversible renderer flag mapping");
        }
    }
}
