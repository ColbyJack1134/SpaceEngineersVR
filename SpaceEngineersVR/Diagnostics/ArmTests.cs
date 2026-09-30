using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using SpaceEngineersVR.Player;
using VRageMath;
using VRageRender.Animations;
using VRageRender.Import;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class ArmTests
    {
        private static void Near(float actual,float expected,string message,float tolerance=0.0001f)
        { if(float.IsNaN(actual) || Math.Abs(actual-expected)>tolerance) throw new Exception(message+": "+actual); }
        public static void Run(Action<string> log)
        {
            // Real installed-engine bone objects, including an intermediate forearm
            // twist bone as used by the engineer model. Exercise production pose code.
            foreach(float side in new[] {-1f,1f})
            {
                var relative=new Matrix[5]; var absolute=new Matrix[5];
                var bones=new MyCharacterBone[5];
                bones[0]=new MyCharacterBone("root",null,Matrix.Identity,0,relative,absolute);
                bones[1]=new MyCharacterBone("upper",bones[0],Matrix.CreateTranslation(side*0.2f,1.4f,0),1,relative,absolute);
                bones[2]=new MyCharacterBone("forearm",bones[1],Matrix.CreateTranslation(side*0.3f,0,0),2,relative,absolute);
                bones[3]=new MyCharacterBone("twist",bones[2],Matrix.CreateTranslation(side*0.1f,0,0),3,relative,absolute);
                bones[4]=new MyCharacterBone("palm",bones[3],Matrix.CreateTranslation(side*0.15f,0,0),4,relative,absolute);
                Matrix offset=ArmMath.PalmCorrection(bones[4].GetAbsoluteRigTransform(),bones[2].GetAbsoluteRigTransform(),side);
                Vector3 shoulder=bones[1].AbsoluteTransform.Translation;
                Vector3 hint=new Vector3(side*0.55f,-1,0.3f);
                for(int i=0;i<500;i++)
                {
                    foreach(var bone in bones) { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
                    // Include vanilla IK's translated wrist, which must not define our arm length.
                    bones[4].Translation=new Vector3(0.05f,0.03f,-0.04f);
                    bones[0].ComputeAbsoluteTransform();
                    Matrix target=Matrix.CreateRotationX(i*0.04f)*Matrix.CreateRotationY(i*0.07f);
                    target.Translation=shoulder+new Vector3((float)Math.Sin(i*0.03)*0.9f,(float)Math.Cos(i*0.07)*0.7f,(float)Math.Sin(i*0.05)*0.8f);
                    if(i==0) target.Translation=shoulder;
                    if(!ArmMath.ApplyPose(bones[1],bones[2],bones[4],target,offset,hint,false)) throw new Exception("Arm solve failed");
                    Near(Vector3.Distance(bones[1].AbsoluteTransform.Translation,bones[2].AbsoluteTransform.Translation),0.3f,"upper arm stretched");
                    Near(Vector3.Distance(bones[2].AbsoluteTransform.Translation,bones[4].AbsoluteTransform.Translation),0.25f,"forearm stretched");
                    ArmMath.Solve(shoulder,target.Translation,0.3f,0.25f,hint,out Vector3 elbow,out Vector3 wrist);
                    Near(Vector3.Distance(bones[4].AbsoluteTransform.Translation,wrist),0,"wrist misses clamped target");
                    Near(Vector3.Distance(bones[2].AbsoluteTransform.Translation,elbow),0,"elbow misses pole target");
                    Near(Vector3.Distance(bones[4].AbsoluteTransform.Forward,(offset*target.GetOrientation()).Forward),0,"palm orientation mismatch");
                    Near(bones[4].Translation.Length(),0,"wrist translated out of its socket");
                    foreach(var bone in bones) { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
                    bones[0].ComputeAbsoluteTransform();
                    if(!ArmMath.ApplyPose(bones[1],bones[2],bones[4],target,offset,hint)) throw new Exception("Adaptive arm solve failed");
                    Near(Vector3.Distance(bones[4].AbsoluteTransform.Translation,target.Translation),0,"tracked hand misses controller");
                    if(Vector3.Distance(bones[1].AbsoluteTransform.Translation,shoulder)>.1201f) throw new Exception("Shoulder travel exceeds limit");
                    Near(Vector3.Distance(bones[4].AbsoluteTransform.Forward,(offset*target.GetOrientation()).Forward),0,"adaptive palm orientation mismatch");
                }
            }
            if(ArmMath.Solve(Vector3.Zero,new Vector3(float.NaN,0,0),0.3f,0.25f,Vector3.Down,out _,out _)) throw new Exception("Invalid tracking target accepted");
            Matrix flipped=ArmMath.AimBone(Matrix.Identity,Vector3.Right,Vector3.Left);
            Near(Vector3.Distance(Vector3.TransformNormal(Vector3.Right,flipped),Vector3.Left),0,"antiparallel bone rotation");
            for(int i=0;i<360;i++)
            {
                Matrix desired=Matrix.CreateRotationX(MathHelper.ToRadians(i));
                Matrix twist=ArmMath.ForearmTwist(Matrix.Identity,desired,Vector3.Right);
                Near(Vector3.Distance(twist.Up,desired.Up),0,"Wrist roll fails to rotate forearm");
                Near(Vector3.Distance(twist.Right,Vector3.Right),0,"Forearm twist moved hand axis");
            }
            log("PASS tracked arms: 1,000 mirrored engine-bone poses in fixed and adaptive modes, accurate controller reach, bounded shoulders, twist chains, wrist orientation, singular/invalid targets");
            RecordedWatchPose(log);
        }
        private static void RecordedWatchPose(Action<string> log)
        {
            string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyCharacterBone).Assembly.Location),"..","Content"));
            var importer=new MyModelImporter();
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(content,"Models/Characters/Astronaut/SE_astronaut.mwm"))))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {reader,new[] {"Bones"}});
            var native=(Array)importer.GetTagData()["Bones"];
            var relative=new Matrix[native.Length]; var absolute=new Matrix[native.Length]; var bones=new MyCharacterBone[native.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var n=native.GetValue(i); int parent=(int)CockpitRender.Member(n,"Parent");
                bones[i]=new MyCharacterBone((string)CockpitRender.Member(n,"Name"),parent<0 ? null : bones[parent],
                    (Matrix)CockpitRender.Member(n,"Transform"),i,relative,absolute);
            }
            var palm=bones.Single(b=>b.Name=="SE_RigLPalm");
            var lower=bones.Single(b=>b.Name=="SE_RigLForearm1"); var upper=lower.Parent;
            // First watch pose captured during the September 29 playtest.
            Matrix desired=Matrix.Identity;
            desired.Right=new Vector3(-.9470054f,-.2854625f,-.1475649f);
            desired.Up=new Vector3(-.3211800f,.8273234f,.4607469f);
            desired.Backward=new Vector3(-.0094374f,.4837239f,-.8751677f);
            desired.Translation=new Vector3(-.0685918f,1.2402833f,-.4206055f);
            var shoulder=new Vector3(-.2063112f,1.4152480f,.1555137f);
            var upperPose=upper.AbsoluteTransform; upperPose.Translation=shoulder;
            upper.SetCompleteTransformFromAbsoluteMatrix(ref upperPose,false); upper.ComputeAbsoluteTransform();
            var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),-1);
            var target=Matrix.Invert(correction)*desired;
            if(!ArmMath.ApplyPose(upper,lower,palm,target,correction,new Vector3(-.55f,-1,.3f))) throw new Exception("Recorded watch pose failed");
            Vector3 forearm=Vector3.Normalize(lower.AbsoluteTransform.Translation-palm.AbsoluteTransform.Translation);
            float deflection=MathHelper.ToDegrees((float)Math.Acos(MathHelper.Clamp(Vector3.Dot(forearm,palm.AbsoluteTransform.Right),-1,1)));
            Near(Vector3.Distance(palm.AbsoluteTransform.Translation,desired.Translation),0,"Watch pose loses tracked palm");
            Near(Vector3.Distance(palm.AbsoluteTransform.Up,desired.Up),0,"Watch pose changes tool/palm orientation",.0002f);
            if(deflection>65) throw new Exception("Recorded watch pose still folds at the wrist: "+deflection);
            if(Vector3.Distance(upper.AbsoluteTransform.Translation,shoulder)>.1201f) throw new Exception("Watch pose shoulder exceeds travel limit");
            log("PASS recorded watch pose with installed astronaut bones: palm target retained; wrist deflection "+deflection.ToString("0.0")+" degrees (capture: 81), shoulder travel bounded.");
        }
    }
}
