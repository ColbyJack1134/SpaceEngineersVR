#if SEVR_DIAGNOSTICS
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Xml.Linq;
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
            RigidLeftWrist(log);
            BlendedLeftWrist(log);
            RecordedWatchPose(log);
            SkinnedHeads(log);
            SeatedLean(log);
            CockpitHandTests.Run(log);
        }
        private static void RigidLeftWrist(Action<string> log)
        {
            var bones=InstalledBones();
            var upper=bones.Single(b=>b.Name=="SE_RigLUpperarm");
            var lower=bones.Single(b=>b.Name=="SE_RigLForearm1");
            var display=bones.Single(b=>b.Name=="SE_RigLForearm2");
            var palm=bones.Single(b=>b.Name=="SE_RigLPalm");
            var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),-1);
            Matrix expected=display.GetAbsoluteRigTransform()*Matrix.Invert(palm.GetAbsoluteRigTransform());
            Matrix forearm=lower.GetAbsoluteRigTransform()*Matrix.Invert(palm.GetAbsoluteRigTransform());
            int independent=0;
            for(int i=0;i<120;i++)
            {
                foreach(var bone in bones) { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
                bones[0].ComputeAbsoluteTransform(true,true);
                var target=Matrix.CreateFromYawPitchRoll(i*.021f-.8f,i*.015f-.5f,i*.035f);
                target.Translation=new Vector3(-.25f+(float)Math.Sin(i*.04f)*.15f,1.2f,-.35f);
                if(!ArmMath.ApplyPose(upper,lower,palm,target,correction,new Vector3(-.55f,-1,.3f),rigidWrist:true))
                    throw new Exception("Rigid left wrist solve failed");
                Near(Vector3.Distance(palm.AbsoluteTransform.Translation,target.Translation),0,"Rigid wrist loses controller");
                Matrix relative=display.AbsoluteTransform*Matrix.Invert(palm.AbsoluteTransform);
                Matrix actualForearm=lower.AbsoluteTransform*Matrix.Invert(palm.AbsoluteTransform);
                Near(Vector3.Distance(actualForearm.Translation,forearm.Translation),0,"Rigid forearm changes length");
                Near(Vector3.Distance(relative.Translation,expected.Translation),0,"Tablet slides relative to wrist");
                Near(Vector3.Distance(relative.Up,expected.Up),0,"Tablet bends relative to wrist");
                Near(Vector3.Distance(relative.Right,expected.Right),0,"Tablet rolls relative to wrist");
                if(Vector3.Distance(actualForearm.Up,forearm.Up)>.01f) independent++;
                Vector3 sleeve=lower.AbsoluteTransform.Up;
                for(int repeat=0;repeat<3;repeat++)
                    ArmMath.ApplyPose(upper,lower,palm,target,correction,new Vector3(-.55f,-1,.3f),rigidWrist:true);
                Near(Vector3.Distance(lower.AbsoluteTransform.Up,sleeve),0,"Sleeve twist drifts while holding a pose");
            }
            if(independent<100) throw new Exception("Sleeve remains locked to tablet roll");
            log("PASS rigid left cuff: 120 installed-bone wrist rotations, tracked palm retained, sleeve roll independent and cuff/display fixed relative to palm; articulated right arm covered separately.");
        }
        private static void BlendedLeftWrist(Action<string> log)
        {
            Vector3 Elbow(MyCharacterBone[] bones,Matrix target,float rigidity)
            {
                foreach(var bone in bones) { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
                bones[0].ComputeAbsoluteTransform(true,true);
                var arm=ArmSkeleton.Find(name=>bones.SingleOrDefault(b=>b.Name==name),"SE_RigLUpperarm","SE_RigLForearm1","SE_RigLPalm",-1);
                if(!ArmSkeleton.Apply(arm,target,true,rigidity,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Blended left wrist solve failed");
                var palm=arm.Palm.Bone.AbsoluteTransform; var desired=arm.PalmOffset*target.GetOrientation();
                Near(Vector3.Distance(palm.Translation,target.Translation),0,"Wrist blend loses controller");
                Near(Vector3.Distance(palm.Up,desired.Up)+Vector3.Distance(palm.Right,desired.Right),0,"Wrist blend turns the palm",.001f);
                return arm.Lower.Bone.AbsoluteTransform.Translation;
            }
            var installed=InstalledBones();
            for(int i=0;i<60;i++)
            {
                var target=Matrix.CreateFromYawPitchRoll(i*.04f-1.2f,i*.02f-.6f,i*.05f);
                target.Translation=new Vector3(-.2f+i*.006f,1.25f,-.4f);
                Vector3 rigid=Elbow(installed,target,1),free=Elbow(installed,target,0),mixed=Elbow(installed,target,.5f);
                if(Vector3.Distance(mixed,rigid)>Vector3.Distance(free,rigid)+.005f || Vector3.Distance(mixed,free)>Vector3.Distance(free,rigid)+.005f)
                    throw new Exception("Wrist blend leaves the range between rigid and articulated elbows");
            }
            var head=MatrixD.CreateWorld(new Vector3D(0,1.6,0),Vector3D.Forward,Vector3D.Up);
            var facing=MatrixD.CreateWorld(new Vector3D(0,1.45,-.5),Vector3D.Normalize(new Vector3D(0,-.15,-.5)),Vector3D.Up);
            Near(TrackedArms.ScreenAttention(facing,head),1,"Screen facing the eye in view does not lock the wrist");
            Near(TrackedArms.ScreenAttention(MatrixD.CreateWorld(facing.Translation,-facing.Forward,Vector3D.Up),head),0,"Screen turned away does not release the wrist");
            Near(TrackedArms.ScreenAttention(facing,MatrixD.CreateWorld(head.Translation,Vector3D.Right,Vector3D.Up)),0,"Screen outside the view does not release the wrist");
            log("PASS blended left wrist: 60 installed-bone poses keep the tracked palm with the elbow between rigid and articulated solves; screen attention locks only when facing and in view");
        }
        private static string Content => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyCharacterBone).Assembly.Location),"..","Content"));
        internal static MyCharacterBone[] InstalledBones(string model="Models/Characters/Astronaut/SE_astronaut.mwm")
        {
            var importer=new MyModelImporter();
            using(var reader=new BinaryReader(File.OpenRead(Path.Combine(Content,model))))
                AccessTools.Method(typeof(MyModelImporter),"LoadTagData").Invoke(importer,new object[] {reader,new[] {"Bones"}});
            var native=(Array)importer.GetTagData()["Bones"];
            var relative=new Matrix[native.Length]; var absolute=new Matrix[native.Length]; var bones=new MyCharacterBone[native.Length];
            for(int i=0;i<bones.Length;i++)
            {
                var n=native.GetValue(i); int parent=(int)CockpitRender.Member(n,"Parent");
                bones[i]=new MyCharacterBone((string)CockpitRender.Member(n,"Name"),parent<0 ? null : bones[parent],
                    (Matrix)CockpitRender.Member(n,"Transform"),i,relative,absolute);
            }
            return bones;
        }
        // Remote head tracking must turn a bone that the installed astronaut meshes are weighted to.
        private static void SkinnedHeads(Action<string> log)
        {
            var astronauts=XDocument.Load(Path.Combine(Content,"Data","Characters.sbc")).Descendants("Character")
                .Where(c=>((string)c.Element("Model"))?.StartsWith(@"Models\Characters\Astronaut\")==true).ToArray();
            if(astronauts.Length<2) throw new Exception("Installed astronaut definitions not found");
            foreach(var astronaut in astronauts)
            {
                string model=((string)astronaut.Element("Model")).Replace('\\','/');
                var bones=InstalledBones(model);
                var head=ArmSkeleton.Head(name=>bones.FirstOrDefault(b=>b.Name==name),(string)astronaut.Element("HeadBone"));
                if(head==null) throw new Exception("No remote head bone in "+model);
                var tags=ModelInspection.ReadModel(Content,Path.Combine(Content,model),out _);
                var indices=ModelInspection.ReadVectors(tags["BlendIndices"] as IEnumerable);
                var weights=ModelInspection.ReadVectors(tags["BlendWeights"] as IEnumerable);
                int skinned=0;
                for(int v=0;v<indices.Length;v++)
                    for(int i=0;i<4;i++)
                        if((int)indices[v][i]==head.Index && weights[v][i]>0) { skinned++; break; }
                if(skinned<1000) throw new Exception("Remote head bone "+head.Name+" moves "+skinned+" vertices in "+model);
                // GetHeadMatrix reads the definition's camera dummy; turning the head must not move it.
                foreach(var bone in bones) bone.ComputeAbsoluteTransform();
                var eye=ArmSkeleton.Below(head,bones.FirstOrDefault(b=>b.Name==(string)astronaut.Element("HeadBone")));
                if(eye==null) throw new Exception("Camera dummy is not below "+head.Name+" in "+model);
                var before=eye.Bone.AbsoluteTransform; var headBefore=head.AbsoluteTransform;
                var saved=new ArmSkeleton.SavedBone {Bone=head};
                if(!ArmSkeleton.Look(saved,eye,Matrix.CreateRotationY(.6f)*Matrix.CreateRotationX(.3f),ArmSkeleton.HeadLimit)) throw new Exception("Head look failed in "+model);
                if(Vector3.Dot(head.AbsoluteTransform.Forward,headBefore.Forward)>.95f) throw new Exception("Head did not turn in "+model);
                Near(Vector3.Distance(eye.Bone.AbsoluteTransform.Translation,before.Translation),0,"Turning the head moves the camera dummy in "+model,.0005f);
                if(!eye.Restore(false) || !saved.Restore(false)) throw new Exception("Head look does not restore in "+model);
                head.ComputeAbsoluteTransform(true,true);
                Near(Vector3.Distance(eye.Bone.AbsoluteTransform.Translation,before.Translation),0,"Restored camera dummy moved in "+model,.0005f);
            }
            log("PASS head bones: "+astronauts.Length+" installed astronaut definitions resolve to a bone weighted to the head mesh; a turned head keeps the camera dummy in place and restores.");
        }
        // The seated lean bends the installed spine toward a headset offset, pins the camera dummy and restores.
        private static void SeatedLean(Action<string> log)
        {
            var bones=InstalledBones();
            foreach(var bone in bones) bone.ComputeAbsoluteTransform();
            Func<string,MyCharacterBone> find=name=>bones.FirstOrDefault(b=>b.Name==name);
            var head=ArmSkeleton.Head(find,"HeadDummy");
            var eye=ArmSkeleton.Below(head,find("HeadDummy"));
            var chain=SpineLean.Find(find,head,eye);
            if(chain==null || eye==null) throw new Exception("Installed astronaut has no spine chain for the seated lean");
            var rest=bones.Select(b=>b.AbsoluteTransform).ToArray();
            var collar=find("SE_RigRCollarbone");
            Vector3 headRest=head.AbsoluteTransform.Translation,eyeRest=eye.Bone.AbsoluteTransform.Translation,collarRest=collar.AbsoluteTransform.Translation;
            var first=Vector3.Normalize(new Vector3(.3f,.8f,-.2f)); var second=Vector3.Normalize(new Vector3(-.5f,.4f,.6f));
            Near(Vector3.Distance(Vector3.Transform(first,Matrix.CreateFromQuaternion(SpineLean.Between(first,second))),second),0,"Between does not rotate the first vector onto the second",.0005f);
            foreach(var delta in new[] {new Vector3(0,0,-.25f),new Vector3(.15f,0,0),new Vector3(-.15f,-.05f,-.1f),new Vector3(0,0,-2f)})
            {
                if(!SpineLean.Apply(chain,delta,0)) throw new Exception("Seated lean failed for "+delta);
                var limited=SpineLean.Limit(delta);
                var moved=head.AbsoluteTransform.Translation-headRest;
                float along=Vector3.Dot(moved,Vector3.Normalize(limited));
                if(along<limited.Length()*.6f || along>limited.Length()*1.05f) throw new Exception("Head moved "+along+" m along a "+limited.Length()+" m lean");
                if(Vector3.Distance(collar.AbsoluteTransform.Translation,collarRest)<along*.3f) throw new Exception("Shoulders did not follow the lean");
                Near(Vector3.Distance(eye.Bone.AbsoluteTransform.Translation,eyeRest),0,"Lean moved the camera dummy",.0005f);
                if(head.AbsoluteTransform.Translation.Y>headRest.Y+.001f && delta.Y<=0) throw new Exception("Lean raised the head");
                SpineLean.Restore(chain);
                for(int i=0;i<bones.Length;i++) Near(Vector3.Distance(bones[i].AbsoluteTransform.Translation,rest[i].Translation),0,"Lean did not restore "+bones[i].Name,.0005f);
            }
            float twist=SpineLean.Twist(new Vector3(.7f,0,-.7f));
            if(twist<=0 || twist>SpineLean.TwistLimit) throw new Exception("Looking right gives twist "+twist);
            Near(SpineLean.Twist(new Vector3(0,.2f,-1)),0,"Looking ahead twists the torso");
            Near(SpineLean.Twist(new Vector3(.05f,0,1)),0,"Looking behind twists the torso",.05f);
            if(!SpineLean.Apply(chain,Vector3.Zero,twist)) throw new Exception("Twist failed");
            var turned=Vector3.TransformNormal(Vector3.Forward,Matrix.Invert(rest[head.Index].GetOrientation())*head.AbsoluteTransform.GetOrientation());
            if(turned.X<.1f || Math.Abs(turned.Y)>.05f) throw new Exception("Torso twist does not turn the head toward the right: "+turned);
            if(collar.AbsoluteTransform.Translation.Z<collarRest.Z+.005f) throw new Exception("Right shoulder did not swing back with a right twist");
            Near(Vector3.Distance(eye.Bone.AbsoluteTransform.Translation,eyeRest),0,"Twist moved the camera dummy",.0005f);
            // The dummy sits 20 cm ahead of the head joint, so the bend passes offset the twist's eye travel; the head moves a few centimetres.
            if(Vector3.Distance(head.AbsoluteTransform.Translation,headRest)>.08f) throw new Exception("Twist displaced the head by "+Vector3.Distance(head.AbsoluteTransform.Translation,headRest));
            SpineLean.Restore(chain);
            for(int i=0;i<bones.Length;i++) Near(Vector3.Distance(bones[i].AbsoluteTransform.Translation,rest[i].Translation),0,"Twist did not restore "+bones[i].Name,.0005f);
            // A head turn after the lean must not adopt the leaned pose as its restore point.
            SpineLean.Apply(chain,new Vector3(0,0,-.2f),0);
            var saved=new ArmSkeleton.SavedBone {Bone=head};
            if(!ArmSkeleton.Look(saved,eye,Matrix.CreateRotationY(.5f),ArmSkeleton.HeadLimit)) throw new Exception("Look after lean failed");
            Near(Vector3.Distance(eye.Bone.AbsoluteTransform.Translation,eyeRest),0,"Look after lean moved the camera dummy",.0005f);
            eye.Restore(false); saved.Restore(false); SpineLean.Restore(chain); head.ComputeAbsoluteTransform(true,true);
            for(int i=0;i<bones.Length;i++) Near(Vector3.Distance(bones[i].AbsoluteTransform.Translation,rest[i].Translation),0,"Lean and look did not restore "+bones[i].Name,.0005f);
            if(SpineLean.Apply(chain,new Vector3(float.NaN,0,0),0)) throw new Exception("Invalid lean accepted");
            log("PASS seated lean: installed spine bends toward forward, side and clamped offsets with the camera dummy pinned, right twist turns the shoulders, lean and look restore bone for bone.");
        }
        private static void RecordedWatchPose(Action<string> log)
        {
            var bones=InstalledBones();
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
#endif
