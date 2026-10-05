using System;
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
            const float deadzone=0.08f;
            var fighter=CockpitRig.Find(CockpitLayout.Fighter);
            Matrix neutral=Matrix.CreateFromYawPitchRoll(0.7f,-0.45f,0.18f);
            neutral.Translation=new Vector3(0.31f,-0.23f,0.37f);
            Near(CockpitStickMath.Rotation(neutral,neutral,deadzone,true),Vector3.Zero,"Neutral rotation kicks");
            Near(CockpitStickMath.Translation(neutral,neutral,deadzone,true),Vector3.Zero,"Neutral translation kicks");
            foreach (int sign in new[] {-1,1})
            {
                Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationZ(-sign*CockpitStickMath.TiltRange),deadzone,true),sign*Vector3.Right,"Tilt strafe sign/axis");
                Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationY(-sign*CockpitStickMath.TwistRange),deadzone,true),sign*Vector3.Up,"Clockwise twist must lift");
                Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationX(-sign*CockpitStickMath.TiltRange),deadzone,true),sign*Vector3.Forward,"Forward tilt must move forward");
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationX(sign*CockpitStickMath.TiltRange),deadzone,true),-sign*Vector3.Right,"Pitch sign/axis");
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationY(sign*CockpitStickMath.TwistRange),deadzone,true),-sign*Vector3.Up,"Yaw twist sign/axis");
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationZ(sign*CockpitStickMath.TiltRange),deadzone,true),-sign*Vector3.Backward,"Roll tilt sign/axis");
            }
            foreach (Matrix grip in new[] {Matrix.Identity,neutral,Matrix.CreateFromYawPitchRoll(-1.1f,.8f,-.6f)})
            foreach (Matrix lean in new[] {Matrix.CreateRotationX(.3f),Matrix.CreateRotationZ(-.3f),Matrix.CreateRotationX(-.2f)*Matrix.CreateRotationZ(.25f)})
            foreach (float twistAngle in new[] {-.4f,0f,.4f})
            {
                // Row-vector order applies twist about the shaft before carrying it through the lean.
                Matrix current=grip*Matrix.CreateRotationY(twistAngle)*lean;
                Vector3 baseline=CockpitStickMath.Rotation(grip,grip*lean,deadzone,true);
                Vector3 combined=CockpitStickMath.Rotation(grip,current,deadzone,true);
                Near(new Vector3(combined.X,0,combined.Z),new Vector3(baseline.X,0,baseline.Z),"Shaft twist changes pitch or roll");
                Vector3 translation=CockpitStickMath.Translation(grip,current,deadzone,true);
                Near(new Vector3(translation.X,0,translation.Z),new Vector3(baseline.Z,0,-baseline.X),"Shaft twist changes lateral or forward thrust");
                Near(CockpitStickMath.Rotation(grip,current,deadzone,false),new Vector3(combined.X,0,combined.Z),"Disabling twist changes lean");
            }
            {
                float pitch=.3f,twistAngle=.4f;
                Matrix current=neutral*Matrix.CreateRotationY(twistAngle)*Matrix.CreateRotationX(pitch);
                float heading=(float)Math.Atan2(Math.Sin(twistAngle),Math.Cos(twistAngle)*Math.Cos(pitch));
                Vector3 expected=new Vector3(CockpitStickMath.Axis(-pitch/CockpitStickMath.TiltRange,deadzone),
                    CockpitStickMath.Axis(-heading/CockpitStickMath.TwistRange,deadzone),0);
                Near(CockpitStickMath.Rotation(neutral,current,deadzone,true),expected,"Tilted yaw must follow projected heading");
            }
            log("PASS stick axes: grabbed shaft lean, compound tilt/twist isolation, translation mapping and projected yaw.");
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationY(-1.1f),deadzone,true),Vector3.Up,"Twist not clamped");
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationX(0.01f),deadzone,true),Vector3.Zero,"Translation deadzone");
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateTranslation(0.1f,-0.2f,0.15f),deadzone,true),Vector3.Zero,"Arm displacement drives thrust");
            Require(Vector3.TransformNormal(Vector3.Forward,Matrix.CreateRotationY(-0.1f)).X>0,"Clockwise convention must turn forward toward right from above");
            Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationX(0.01f),deadzone,true),Vector3.Zero,"Rotation deadzone");
            Near(CockpitStickMath.Translation(neutral,neutral*Matrix.CreateRotationY(-CockpitStickMath.TwistRange),deadzone,false),Vector3.Zero,"Disabled twist lifts");
            Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationY(CockpitStickMath.TwistRange),deadzone,false),Vector3.Zero,"Disabled twist yaws");
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
                Matrix left=fighter.Left.Visual(new Vector3(-axes.Z,axes.Y,axes.X));
                Matrix right=fighter.Right.Visual(axes);
                Require(left.IsValid() && right.IsValid() && Math.Abs(left.Determinant()-1)<0.0001f,"Invalid/elongated control articulation");
                Near(Vector3.Transform(fighter.Left.Pivot,left),fighter.Left.Pivot,"Left pivot moved");
                Near(Vector3.Transform(fighter.Right.Pivot,right),fighter.Right.Pivot,"Right pivot moved");
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
                Near(CockpitStickMath.Rotation(neutral,neutral*Matrix.CreateRotationX(CockpitStickMath.TiltRange/sensitivity),deadzone,true,sensitivity),-Vector3.Right,"Sensitivity limits maximum authority");
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
            Require(pulse.Sample(true,new Vector3(.4f,.4f,0),now.AddMilliseconds(100))==1,"Leaving neutral has no light tick");
            Require(pulse.Sample(true,new Vector3(0,.4f,0),now.AddMilliseconds(250))==1,"Tilt deadzone entry is hidden by held twist");
            Require(pulse.Sample(true,Vector3.Zero,now.AddMilliseconds(400))==1,"Twist deadzone entry has no tick");
            for(int i=0;i<20;i++) Require(pulse.Sample(true,new Vector3(i%2==0 ? .01f:0,0,i%2==0 ? .01f:0),now.AddMilliseconds(420+i*10))==0,"Neutral jitter repeats pulses");
            Require(pulse.Sample(true,new Vector3(.03f,0,.03f),now.AddMilliseconds(700))==1,"Diagonal deadzone exit has no tick");
            Require(pulse.Sample(true,new Vector3(-.03f,0,.03f),now.AddMilliseconds(850))==0,"Crossing an axis outside the neutral circle ticks");
            Require(pulse.Sample(true,Vector3.Right,now.AddSeconds(1))==2,"End stop has no pulse");
            pulse.Sample(true,Vector3.Right*.95f,now.AddSeconds(1.1));
            Require(pulse.Sample(true,Vector3.Right,now.AddSeconds(1.3))==0,"Limit jitter repeats pulses");
            Require(pulse.Sample(false,Vector3.Zero,now.AddSeconds(1.4))==0 && pulse.Sample(true,Vector3.Zero,now.AddSeconds(1.5))==0,"Release or regrab emits a detent");
            log("PASS stick comfort: squared response, full authority, refresh-independent smoothing, immediate release/neutral, radial and twist deadzone boundaries, coalesced ticks and limit hysteresis.");
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
            Require(info!=null && AccessTools.Field(info.PropertyType,"Name")!=null && AccessTools.Field(typeof(MyMeshMaterialId),"Index")!=null,"Native material name cannot be verified");
            log("PASS cockpit sticks: tilt/twist translation, clockwise lift, no thrust from arm displacement, 6-axis signs/deadzones/clamps, neutral capture, 300 moving large-coordinate ship frames, fixed rigid pivots; reversible renderer flag mapping and material accessors");
        }
    }
}
