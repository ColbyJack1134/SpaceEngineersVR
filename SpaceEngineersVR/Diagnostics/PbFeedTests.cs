#if SEVR_DIAGNOSTICS
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Scripting;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    public static class PbFeedTests
    {
        public static void Run(Action<string> log)
        {
            var message=new PbMessage {Controller=-1234567890123,Flags=PbMessage.Head|PbMessage.Left};
            message.HeadPose=Matrix.CreateFromYawPitchRoll(.4f,-.2f,.1f); message.HeadPose.Translation=new Vector3(.05f,-.1f,.02f);
            message.LeftHand.Pose=Matrix.CreateFromYawPitchRoll(-1.2f,.6f,2.5f); message.LeftHand.Pose.Translation=new Vector3(-.25f,-.35f,-.4f);
            message.LeftHand.Trigger=.5f; message.LeftHand.Grip=1; message.LeftHand.Stick=new Vector2(-.75f,.25f);
            message.LeftHand.Curls[0]=.5f; message.LeftHand.Curls[4]=.125f;
            message.RightHand.Trigger=float.NaN;
            message.Flags|=PbMessage.LeftJoints;
            message.LeftArm.Shoulder=new Vector3(-.18f,-.25f,.1f); message.LeftArm.Elbow=new Vector3(-.2f,-.5f,0); message.LeftArm.Wrist=new Vector3(-.25f,-.35f,-.3f);

            string text;
            var culture=Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture=new CultureInfo("de-DE");
                text=PbMessage.Format(7,message.Body());
            }
            finally { Thread.CurrentThread.CurrentCulture=culture; }
            string[] fields=text.Split(' ');
            if(fields.Length!=PbMessage.Fields || fields[0]!=PbMessage.Tag || text.Contains(",") || text.Contains("NaN"))
                throw new Exception("VR data message layout: "+text);
            if(text.Length>600) throw new Exception("VR data message is "+text.Length+" characters");
            Joints(log);
            // The example script stays local (ignored IngameScripts/); clean checkouts check the layout only.
            if(Assembly.GetEntryAssembly().GetManifestResourceInfo("SevrVrData.cs")==null)
            {
                log("PASS programmable block VR data: invariant layout (example script not present, parse check skipped)");
                return;
            }

            var data=Parser();
            if(!(bool)Call(data,"TryParse",text)) throw new Exception("Example script rejected: "+text);
            Expect(Get(data,"Sequence"),7u,"sequence"); Expect(Get(data,"Controller"),-1234567890123L,"controller"); Expect(Get(data,"Flags"),message.Flags,"flags");
            Pose((MatrixD)Get(data,"HeadPose"),message.HeadPose,"head");
            object left=Get(data,"LeftHand"),right=Get(data,"RightHand");
            Pose((MatrixD)Get(left,"Pose"),message.LeftHand.Pose,"left hand");
            Expect(Get(left,"Trigger"),.5f,"left trigger"); Expect(Get(left,"Grip"),1f,"left grip"); Expect(Get(left,"Stick"),new Vector2(-.75f,.25f),"left stick");
            var curls=(float[])Get(left,"Curls");
            if(Math.Abs(curls[0]-.5f)>.001f || Math.Abs(curls[4]-.125f)>.001f || curls[1]!=0) throw new Exception("Left curls: "+string.Join(" ",curls));
            Expect(Get(right,"Trigger"),0f,"untracked right trigger");
            object leftArm=Get(data,"LeftArm");
            Point((Vector3D)Get(leftArm,"Shoulder"),message.LeftArm.Shoulder,"left shoulder"); Point((Vector3D)Get(leftArm,"Elbow"),message.LeftArm.Elbow,"left elbow");
            Point((Vector3D)Get(leftArm,"Wrist"),message.LeftArm.Wrist,"left wrist");

            // Fields are appended over versions: a longer message parses, one without joints parses with the joint flags cleared.
            if(!(bool)Call(data,"TryParse",text+" 1.5")) throw new Exception("Example script rejected an appended field");
            foreach(string old in new[] {string.Join(" ",fields.Take(43)),string.Join(" ",fields.Take(60))})
                if(!(bool)Call(data,"TryParse",old) || ((int)Get(data,"Flags")&PbMessage.LeftJoints)!=0) throw new Exception("Message without joints: "+old);

            string stop=PbMessage.Format(8,new PbMessage {Controller=5,Flags=PbMessage.Stopped}.Body());
            if(!(bool)Call(data,"TryParse",stop) || (int)Get(data,"Flags")!=PbMessage.Stopped) throw new Exception("Stop message not parsed: "+stop);
            foreach(string bad in new[] {null,"","run","SEVR2"+text.Substring(5),string.Join(" ",fields.Take(42)),text.Replace(" 0.5 "," x ")})
                if((bool)Call(data,"TryParse",bad)) throw new Exception("Example script accepted: "+bad);
            Expect(Get(data,"Sequence"),8u,"sequence after rejected messages");
            log("PASS programmable block VR data: invariant layout, example script parses poses, inputs, curls, joints, appended fields and stop, rejects malformed arguments");
            MechArms(log);
        }

        private static void Joints(Action<string> log)
        {
            var bones=ArmTests.InstalledBones();
            Func<string,VRageRender.Animations.MyCharacterBone> find=name => bones.FirstOrDefault(b => b.Name==name);
            foreach(var bone in bones) { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
            bones[0].ComputeAbsoluteTransform(true,true);
            Vector3 headJoint=find("SE_RigHead").GetAbsoluteRigTransform().Translation;
            // Posed after the compute, so any recompute by the feed shows up in the absolute transforms.
            foreach(string s in new[] {"L","R"}) find("SE_Rig"+s+"Upperarm").Rotation=Quaternion.CreateFromAxisAngle(Vector3.Up,.5f);
            var before=bones.Select(b => (b.Rotation,b.Translation,b.AbsoluteTransform)).ToArray();
            foreach(float side in new[] {-1f,1f})
            {
                string s=side<0 ? "L":"R";
                var arm=PbArms.Measure(ArmSkeleton.Find(find,"SE_Rig"+s+"Upperarm","SE_Rig"+s+"Forearm1","SE_Rig"+s+"Palm",side),headJoint);
                if(arm==null || arm.Shoulder.X*side<.1f || arm.Shoulder.Y>-.1f || arm.Upper<.2f || arm.Lower<.2f)
                    throw new Exception("Installed arm measure: shoulder "+arm?.Shoulder+" lengths "+arm?.Upper+" "+arm?.Lower);
                arm.State.FixedStep=1;
                var head=Matrix.Identity;
                // A hand held in front of the chest, thumb up: the elbow sits below the shoulder at the arm's lengths.
                var wrist=Matrix.CreateTranslation(side*.15f,-.3f,-.35f);
                PbArms.Solve(arm,head,wrist,1,out var shoulder,out var elbow);
                if(Math.Abs(Vector3.Distance(shoulder,elbow)-arm.Upper)>.002f || Math.Abs(Vector3.Distance(elbow,wrist.Translation)-arm.Lower)>.002f)
                    throw new Exception("Elbow off the arm lengths: "+elbow);
                if(elbow.Y>shoulder.Y-.05f) throw new Exception("Elbow not below the shoulder: "+elbow+" shoulder "+shoulder);
                // Out of reach the arm points at the hand; the solve keeps a few degrees of bend at full reach.
                var far=Matrix.CreateTranslation(shoulder+new Vector3(0,0,-1.5f));
                PbArms.Solve(arm,head,far,1,out shoulder,out elbow);
                if(Vector3.Dot(Vector3.Normalize(elbow-shoulder),Vector3.Forward)<.98f) throw new Exception("Arm not straight toward a far hand: "+elbow);
            }
            // The feed reads the bind pose only; the avatar's bones belong to TrackedArms.
            for(int i=0;i<bones.Length;i++)
                if(bones[i].Rotation!=before[i].Rotation || bones[i].Translation!=before[i].Translation || bones[i].AbsoluteTransform!=before[i].AbsoluteTransform)
                    throw new Exception("Arm feed wrote bone "+bones[i].Name);
            log("PASS programmable block arm joints: installed bind shoulders and lengths, elbow at the arm lengths below the shoulder, near-straight arm out of reach, no bone written");
        }

        private static void MechArms(Action<string> log)
        {
            if(Assembly.GetEntryAssembly().GetManifestResourceInfo("SevrMechArms.cs")==null)
            {
                log("PASS programmable block mech arms: script not present, skipped");
                return;
            }
            string script=ModApiTests.Resource("SevrMechArms.cs");
            string example=ModApiTests.Resource("SevrVrData.cs");
            string parser=example.Substring(example.IndexOf("public class SevrData",StringComparison.Ordinal));
            if(!script.EndsWith(parser,StringComparison.Ordinal)) throw new Exception("Mech arms script carries a different SevrData copy than the example");
            var types=Compile(ModApiTests.Scripting(),script).GetTypes();
            var solverType=types.First(t => t.Name=="ArmSolver");

            // MyMotorStator.ComputeCurrentAngle: rotor and hinge angles are both right-handed turns about the stator's Down, the script's joint axis.
            var stator=MatrixD.CreateFromYawPitchRoll(.3,-.7,1.1);
            foreach(double angle in new[] {.4,1.2,-.9,2.5})
            {
                var top=stator*MatrixD.CreateFromQuaternion(QuaternionD.CreateFromAxisAngle(stator.Down,angle));
                double rotor=Math.Acos(MathHelper.Clamp(Vector3D.Dot(top.Right,stator.Right),-1,1));
                if(Vector3D.Dot(top.Right,stator.Backward)<0) rotor=2*Math.PI-rotor;
                double hinge=-Math.Atan2(Vector3D.Dot(Vector3D.Cross(stator.Left,top.Left),stator.Up),Vector3D.Dot(stator.Left,top.Left));
                double wrapped=angle<0 ? angle+2*Math.PI : angle;
                if(Math.Abs(rotor-wrapped)>1e-6 || Math.Abs(hinge-angle)>1e-6) throw new Exception("Joint sign convention: angle "+angle+" rotor "+rotor+" hinge "+hinge);
            }
            // The solver composes body-side turns on the left: q1*q2 applies q2 first.
            var a=QuaternionD.CreateFromAxisAngle(Vector3D.Up,.9); var b=QuaternionD.CreateFromAxisAngle(Vector3D.Right,.6);
            if(Vector3D.Distance(Vector3D.Transform(Vector3D.Forward,a*b),Vector3D.Transform(Vector3D.Transform(Vector3D.Forward,b),a))>1e-9)
                throw new Exception("Quaternion composition order");

            var random=new Random(7);
            int cases=0;
            double worst=0;
            // Chains by role: yaw-pitch-elbow, pitch-yaw-elbow, three-axis shoulder with elbow, and a three-axis wrist.
            var layouts=new[] {
                new[] {Tuple.Create(0,Vector3D.Up),Tuple.Create(0,Vector3D.Right),Tuple.Create(1,Vector3D.Right)},
                new[] {Tuple.Create(0,Vector3D.Right),Tuple.Create(0,Vector3D.Up),Tuple.Create(1,Vector3D.Right)},
                new[] {Tuple.Create(0,Vector3D.Up),Tuple.Create(0,Vector3D.Right),Tuple.Create(0,Vector3D.Forward),Tuple.Create(1,Vector3D.Right)},
                new[] {Tuple.Create(0,Vector3D.Up),Tuple.Create(0,Vector3D.Right),Tuple.Create(1,Vector3D.Right),Tuple.Create(2,Vector3D.Up),Tuple.Create(2,Vector3D.Right),Tuple.Create(2,Vector3D.Forward)} };
            foreach(var layout in layouts)
                for(int mount=0;mount<4;mount++)
                {
                    // Any mounting: the whole arm turned, and upside-down joints with negated axes.
                    var turn=mount==0 ? QuaternionD.Identity : QuaternionD.CreateFromYawPitchRoll(random.NextDouble()*6,random.NextDouble()*6,random.NextDouble()*6);
                    var axes=layout.Select((joint,i) => Vector3D.Transform(joint.Item2,turn)*(mount==3 && i%2==1 ? -1:1)).ToArray();
                    var reference=new[] {Vector3D.Forward,Vector3D.Right,Vector3D.Forward,Vector3D.Right,Vector3D.Forward,Vector3D.Up}.Select(v => Vector3D.Transform(v,turn)).ToArray();
                    var solver=Activator.CreateInstance(solverType,layout.Length);
                    var roles=(int[])Get(solver,"Roles"); var solverAxes=(Vector3D[])Get(solver,"Axes");
                    var angles=(double[])Get(solver,"Angles"); var lower=(double[])Get(solver,"Lower"); var upper=(double[])Get(solver,"Upper");
                    Array.Copy(reference,(Vector3D[])Get(solver,"Reference"),6);
                    for(int i=0;i<layout.Length;i++) { roles[i]=layout[i].Item1; solverAxes[i]=axes[i]; lower[i]=upper[i]=double.NaN; }
                    var target=(Vector3D[])Get(solver,"Target");
                    for(int trial=0;trial<40;trial++)
                    {
                        // A reachable pose: the elbow bends the hand up from the calibration pose, as an arm does.
                        var truth=layout.Select(joint => joint.Item1==1 ? .2+random.NextDouble()*1.6 : (random.NextDouble()-.5)*2.2).ToArray();
                        if(mount==3) for(int i=1;i<truth.Length;i+=2) truth[i]=-truth[i];
                        var layoutRoles=layout.Select(joint => joint.Item1).ToArray();
                        var pose=Chain(axes,layoutRoles,truth);
                        Vector3D upperArm=Vector3D.Transform(reference[0],pose[0]),forearm=Vector3D.Transform(reference[2],pose[1]);
                        Vector3D bend=Vector3D.Normalize(Vector3D.Cross(upperArm,forearm));
                        target[0]=upperArm; target[1]=bend; target[2]=forearm; target[3]=bend;
                        target[4]=Vector3D.Transform(reference[4],pose[2]); target[5]=Vector3D.Transform(reference[5],pose[2]);
                        // Cold for the first pose, then warm from the previous answer as in play.
                        if(trial==0) Array.Clear(angles,0,angles.Length);
                        using(VRage.Library.Compiler.IlInjector.BeginRunBlock(50000,10000,false))
                        {
                            solverType.GetMethod("Solve").Invoke(solver,new object[] {1.0});
                            solverType.GetMethod("Solve").Invoke(solver,new object[] {1.0});
                        }
                        var solved=Chain(axes,layoutRoles,angles);
                        double error=Math.Max(Angle(Vector3D.Transform(reference[0],solved[0]),upperArm),Angle(Vector3D.Transform(reference[2],solved[1]),forearm));
                        if(layout.Length==6) error=Math.Max(error,Math.Max(Angle(Vector3D.Transform(reference[4],solved[2]),target[4]),Angle(Vector3D.Transform(reference[5],solved[2]),target[5])));
                        worst=Math.Max(worst,error);
                        cases++;
                    }
                }
            if(worst>2) throw new Exception("Mech arm solver misses reachable poses by up to "+worst.ToString("0.0")+" degrees");

            // A limited hinge stops at its limit instead of wrapping past it.
            var limited=Activator.CreateInstance(solverType,1);
            ((int[])Get(limited,"Roles"))[0]=0; ((Vector3D[])Get(limited,"Axes"))[0]=Vector3D.Right;
            ((double[])Get(limited,"Lower"))[0]=-.5; ((double[])Get(limited,"Upper"))[0]=.5;
            Array.Copy(new[] {Vector3D.Forward,Vector3D.Right,Vector3D.Forward,Vector3D.Right,Vector3D.Forward,Vector3D.Up},(Vector3D[])Get(limited,"Reference"),6);
            var limitedTarget=(Vector3D[])Get(limited,"Target");
            limitedTarget[0]=Vector3D.Transform(Vector3D.Forward,QuaternionD.CreateFromAxisAngle(Vector3D.Right,1.2)); limitedTarget[1]=Vector3D.Right;
            using(VRage.Library.Compiler.IlInjector.BeginRunBlock(50000,10000,false)) solverType.GetMethod("Solve").Invoke(limited,new object[] {1.0});
            double clamped=((double[])Get(limited,"Angles"))[0];
            if(Math.Abs(clamped-.5)>1e-9) throw new Exception("Limited joint solved to "+clamped);
            log("PASS programmable block mech arms: compiles, joint axis matches the engine's rotor and hinge angle sign, "+cases+" reachable poses over 4 layouts and 4 mountings within "+worst.ToString("0.00")+" degrees, two arms of up to six joints within the instruction limit, limits hold");
        }

        // Orientations of the upper arm, forearm and hand: the product of each role's joint turns about their calibration axes.
        private static QuaternionD[] Chain(Vector3D[] axes,int[] roles,double[] angles)
        {
            var result=new[] {QuaternionD.Identity,QuaternionD.Identity,QuaternionD.Identity};
            var total=QuaternionD.Identity;
            for(int i=0;i<axes.Length;i++)
            {
                total=total*QuaternionD.CreateFromAxisAngle(axes[i],angles[i]);
                for(int role=roles[i];role<3;role++) result[role]=total;
            }
            return result;
        }
        private static double Angle(Vector3D a,Vector3D b) => MathHelper.ToDegrees(Math.Acos(MathHelper.Clamp(Vector3D.Dot(Vector3D.Normalize(a),Vector3D.Normalize(b)),-1,1)));
        private static void Point(Vector3D actual,Vector3 expected,string name)
        {
            if(Vector3D.Distance(actual,expected)>.0002) throw new Exception(name+": "+actual+" expected "+expected);
        }

        // Checks a programmable block script file against the game's compiler and whitelist without loading a world.
        public static void CompileFile(string path,Action<string> log)
        {
            Compile(ModApiTests.Scripting(),File.ReadAllText(path));
            log("PASS "+Path.GetFileName(path)+" compiles as a programmable block script");
        }

        // Compiled exactly as a programmable block compiles its program (CompileIngameScriptAsync).
        private static object Parser()
        {
            var scripting=ModApiTests.Scripting();
            bool rejected=false;
            try { Compile(scripting,"public void Main() { System.IO.File.ReadAllText(\"x\"); }"); }
            catch(Exception ex) { rejected=ex.Message.Contains("is prohibited"); }
            if(!rejected) throw new Exception("Programmable block compiler accepted a call outside the whitelist; the example check proves nothing");
            var type=Compile(scripting,ModApiTests.Resource("SevrVrData.cs")).GetTypes().First(t => t.Name=="SevrData");
            return Activator.CreateInstance(type);
        }
        private static Assembly Compile(IVRageScripting scripting,string program)
        {
            var assembly=scripting.CompileIngameScriptAsync("SEVR.PbExampleTest",program,out var messages,"SEVR programmable block example","Program","MyGridProgram").Result;
            var errors=messages.Where(m => m.IsError).ToArray();
            if(errors.Length>0 || assembly==null) throw new Exception("Programmable block compiler errors: "+string.Join(" | ",errors.Select(m => m.Text)));
            return assembly;
        }

        private static void Pose(MatrixD actual,Matrix expected,string name)
        {
            if(Vector3D.Distance(actual.Translation,expected.Translation)>.0002 || Vector3D.Dot(actual.Forward,expected.Forward)<.9999 || Vector3D.Dot(actual.Up,expected.Up)<.9999)
                throw new Exception(name+" pose: "+actual+" expected "+expected);
        }
        private static void Expect(object actual,object expected,string name)
        {
            if(!Equals(actual,expected)) throw new Exception(name+": "+actual+" expected "+expected);
        }
        // Compiled scripts count instructions; outside a run block the counter throws (MyProgrammableBlock.ExecuteCode).
        private static object Call(object target,string method,string argument)
        {
            using(VRage.Library.Compiler.IlInjector.BeginRunBlock(50000,10000,false))
                return target.GetType().GetMethod(method).Invoke(target,new object[] {argument});
        }
        private static object Get(object target,string field) => target.GetType().GetField(field).GetValue(target);
    }
}
#endif
