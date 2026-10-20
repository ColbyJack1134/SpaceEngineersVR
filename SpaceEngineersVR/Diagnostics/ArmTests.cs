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
            Solver(log);
            ReachModes(log);
            Limits(log);
            GloveRoll(log);
            Placement(log);
            RecordedWatchPose(log);
            SkinnedHeads(log);
            SeatedLean(log);
            CockpitHandTests.Run(log);
        }
        // step: the solver's smoothing step per solve; 1 settles the elbow correction within one solve.
        private static ArmSkeleton.Arm Installed(MyCharacterBone[] bones,float side,float step=1)
        {
            string s=side<0 ? "L":"R";
            foreach(var bone in bones) { bone.Rotation=Quaternion.Identity; bone.Translation=Vector3.Zero; }
            bones[0].ComputeAbsoluteTransform(true,true);
            var arm=ArmSkeleton.Find(name=>bones.FirstOrDefault(b=>b.Name==name),"SE_Rig"+s+"Upperarm","SE_Rig"+s+"Forearm1","SE_Rig"+s+"Palm",side);
            arm.State.FixedStep=step;
            return arm;
        }
        internal static float Length(ArmSkeleton.Arm arm,bool upper) => upper
            ? Vector3.Distance(arm.Upper.Bone.AbsoluteTransform.Translation,arm.Lower.Bone.AbsoluteTransform.Translation)
            : Vector3.Distance(arm.Lower.Bone.AbsoluteTransform.Translation,arm.Palm.Bone.AbsoluteTransform.Translation);
        // The elbow hinge as a forearm bone carries it, so the palm's roll past that bone is the roll left at that joint.
        private static Vector3 Carried(ArmSkeleton.Arm arm,MyCharacterBone bone)
        {
            var relation=(bone.GetAbsoluteRigTransform()*Matrix.Invert(arm.Lower.Bone.GetAbsoluteRigTransform())).GetOrientation();
            return Vector3.TransformNormal(arm.Rig.LowerHinge,Matrix.Invert(relation)*bone.AbsoluteTransform.GetOrientation());
        }
        private static Matrix Target(ArmSkeleton.Arm arm,float side,int i)
        {
            var target=Matrix.CreateFromYawPitchRoll(side*(i*.021f-.9f),i*.017f-.6f,(float)Math.Sin(i*.05)*.7f);
            Vector3 shoulder=arm.Upper.Bone.AbsoluteTransform.Translation;
            target.Translation=shoulder+new Vector3(side*(.05f+(float)Math.Abs(Math.Sin(i*.031))*.3f)-side*.25f*(float)Math.Max(0,Math.Sin(i*.013)),
                -.35f+(float)Math.Cos(i*.027)*.35f,-.15f-(float)Math.Abs(Math.Cos(i*.019))*.3f);
            return target;
        }
        private static void Solver(Action<string> log)
        {
            var bones=InstalledBones();
            foreach(float side in new[] {-1f,1f})
            {
                var arm=Installed(bones,side,1/90f);
                float upper=Length(arm,true),lower=Length(arm,false);
                Vector3 lastElbow=Vector3.Zero; int flips=0;
                for(int i=0;i<300;i++)
                {
                    var target=Target(arm,side,i);
                    var options=ArmSolve.Options.Local;
                    if(!ArmSkeleton.Apply(arm,target,options,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Arm solve failed");
                    var palm=arm.Palm.Bone.AbsoluteTransform; var desired=arm.PalmOffset*target.GetOrientation();
                    Near(Vector3.Distance(palm.Translation,target.Translation),0,"Hand misses the controller");
                    Near(Vector3.Distance(palm.Up,desired.Up)+Vector3.Distance(palm.Right,desired.Right),0,"Palm orientation",.0005f);
                    float reach=Vector3.Distance(arm.Upper.Bone.AbsoluteTransform.Translation,target.Translation);
                    if(reach<upper+lower-.02f)
                    {
                        Near(Length(arm,true),upper,"Upper arm length within reach",.0005f);
                        Near(Length(arm,false),lower,"Forearm length within reach",.0005f);
                    }
                    Vector3 axis=Vector3.Normalize(palm.Translation-arm.Lower.Bone.AbsoluteTransform.Translation);
                    float past=ArmSolve.Roll(axis,Carried(arm,arm.Lower.Bone),desired,arm.Rig);
                    float total=past/(1-ArmSolve.TwistShares[0]);
                    float wrist=ArmSolve.Roll(axis,Carried(arm,arm.Chain[arm.Chain.Length-1]),desired,arm.Rig);
                    float expected=total*(1-ArmSolve.TwistShares[ArmSolve.TwistShares.Length-1]);
                    if(Math.Abs(wrist-expected)>1.5f) throw new Exception("Roll left at the wrist joint: "+wrist+" of "+total);
                    for(int c=0;c<arm.Chain.Length;c++)
                    {
                        float left=ArmSolve.Roll(axis,Carried(arm,arm.Chain[c]),desired,arm.Rig);
                        if(Math.Abs(left-total*(1-ArmSolve.TwistShares[c+1]))>1.5f) throw new Exception("Twist bone "+c+" carries the wrong roll share: "+left+" of "+total);
                    }
                    Vector3 elbow=arm.Lower.Bone.AbsoluteTransform.Translation;
                    if(i>0 && Vector3.Distance(elbow,lastElbow)>.08f) flips++;
                    lastElbow=elbow;
                    arm.Restore(true);
                }
                if(flips>0) throw new Exception("Elbow jumped between consecutive targets "+flips+" times");
                if(ArmSkeleton.Apply(arm,Matrix.CreateTranslation(float.NaN,1,0),ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Invalid target accepted");
            }
            // The side sign rules in MeasureRig and Hinge must give mirror-image elbows for mirror-image targets.
            var leftArm=Installed(bones,-1); var rightArm=Installed(bones,1);
            for(int i=0;i<60;i++)
            {
                var target=Target(leftArm,-1,i*5);
                if(!ArmSkeleton.Apply(leftArm,target,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Mirror solve failed");
                Vector3 elbow=leftArm.Lower.Bone.AbsoluteTransform.Translation; leftArm.Restore(true);
                var mirrored=Matrix.CreateScale(-1,1,1)*target*Matrix.CreateScale(-1,1,1); mirrored.Translation=target.Translation*new Vector3(-1,1,1);
                if(!ArmSkeleton.Apply(rightArm,mirrored,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Mirror solve failed");
                Vector3 other=rightArm.Lower.Bone.AbsoluteTransform.Translation; rightArm.Restore(true);
                if(Vector3.Distance(other,elbow*new Vector3(-1,1,1))>.01f) throw new Exception("Left and right elbows are not mirror images: "+Vector3.Distance(other,elbow*new Vector3(-1,1,1)));
            }
            log("PASS arm solver: 600 installed-bone targets on both arms keep the hand on the controller, bind lengths within reach, roll in the forearm and a continuous elbow");
        }
        private static void ReachModes(Action<string> log)
        {
            var bones=InstalledBones();
            foreach(float side in new[] {-1f,1f})
            {
                var arm=Installed(bones,side);
                float upper=Length(arm,true),lower=Length(arm,false);
                Vector3 shoulder=arm.Upper.Bone.AbsoluteTransform.Translation;
                for(int i=0;i<40;i++)
                {
                    var target=Matrix.CreateRotationY(side*.3f);
                    Vector3 direction=Vector3.Normalize(new Vector3(side*.4f,(float)Math.Sin(i*.2),-1));
                    target.Translation=shoulder+direction*(upper+lower)*(1.05f+i*.02f);
                    if(!ArmSkeleton.Apply(arm,target,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Stretch solve failed");
                    Near(Vector3.Distance(arm.Palm.Bone.AbsoluteTransform.Translation,target.Translation),0,"Stretched hand misses the controller",.0005f);
                    float stretch=Length(arm,true)/upper;
                    if(stretch>ArmSolve.MaxStretch+.001f) throw new Exception("Stretch over the limit: "+stretch);
                    if(stretch<1-.001f) throw new Exception("Arm shortened: "+stretch);
                    if(Vector3.Distance(arm.Upper.Bone.AbsoluteTransform.Translation,shoulder)>ArmSolve.ShoulderTravel+.0001f) throw new Exception("Shoulder travel over the limit");
                    arm.Restore(true);
                    if(!ArmSkeleton.Apply(arm,target,ArmSolve.Options.Reach,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Reach solve failed");
                    Near(Length(arm,true),upper,"Reach mode stretched the upper arm",.0005f);
                    Near(Length(arm,false),lower,"Reach mode stretched the forearm",.0005f);
                    Vector3 travelled=arm.Upper.Bone.AbsoluteTransform.Translation;
                    float gap=Vector3.Distance(arm.Palm.Bone.AbsoluteTransform.Translation,target.Translation);
                    float expected=Vector3.Distance(travelled,target.Translation)-(upper+lower-.005f);
                    if(expected<0) expected=0;
                    Near(gap,expected,"Reach mode hand does not stop at full reach",.002f);
                    Vector3 toward=Vector3.Normalize(target.Translation-travelled);
                    Near(Vector3.Dot(Vector3.Normalize(arm.Palm.Bone.AbsoluteTransform.Translation-travelled),toward),1,"Reach mode hand leaves the line to the controller",.001f);
                    var desired=arm.PalmOffset*target.GetOrientation();
                    Near(Vector3.Distance(arm.Palm.Bone.AbsoluteTransform.Up,desired.Up),0,"Reach mode changes the palm orientation",.0005f);
                    arm.Restore(true);
                    if(!ArmSkeleton.Apply(arm,target,ArmSolve.Options.Fixed,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Fixed solve failed");
                    Near(Vector3.Distance(arm.Upper.Bone.AbsoluteTransform.Translation,shoulder),0,"Fixed mode moved the shoulder");
                    Near(Length(arm,true),upper,"Fixed mode stretched the upper arm",.0005f);
                    arm.Restore(true);
                }
            }
            log("PASS reach modes: local view stretches to the controller within "+ArmSolve.MaxStretch+"x and "+ArmSolve.ShoulderTravel+" m shoulder travel; reach mode keeps bind lengths with the hand stopping on the line to the controller; fixed mode holds the shoulder");
        }
        private static void Limits(Action<string> log)
        {
            var bones=InstalledBones();
            int corrected=0;
            foreach(float side in new[] {-1f,1f})
            {
                var arm=Installed(bones,side);
                Vector3 shoulder=arm.Upper.Bone.AbsoluteTransform.Translation;
                for(int i=0;i<24;i++)
                {
                    // A hand in front, rolled far about the forearm, and a hand bent back at the wrist.
                    var target=Matrix.CreateFromYawPitchRoll(side*.2f,i<12 ? 0:1.3f,(i%12)*.5f-2.8f);
                    target.Translation=shoulder+new Vector3(side*.1f,-.1f,-.4f);
                    if(!ArmSkeleton.Apply(arm,target,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Limit solve failed");
                    Vector3 elbow=arm.Lower.Bone.AbsoluteTransform.Translation,wrist=arm.Palm.Bone.AbsoluteTransform.Translation;
                    Vector3 axis=Vector3.Normalize(wrist-shoulder);
                    Vector3 bend=Vector3.Normalize((elbow-shoulder)-axis*Vector3.Dot(elbow-shoulder,axis));
                    Vector3 hint=ArmSolve.Hint(shoulder,wrist,Length(arm,true)+Length(arm,false),side,axis);
                    Vector3 forearm=Vector3.Normalize(wrist-elbow);
                    var palmPose=arm.PalmOffset*target.GetOrientation();
                    if(Vector3.Dot(bend,hint)<.999f) corrected++;
                    ArmSolve.Wrist(forearm,ArmSolve.Hinge(bend,axis,side),palmPose,arm.Rig,out float pitch,out float sideways,out float roll);
                    if(float.IsNaN(pitch) || float.IsNaN(sideways) || float.IsNaN(roll) || Math.Abs(roll)>180) throw new Exception("Wrist angles invalid");
                    arm.Restore(true);
                }
            }
            if(corrected<8) throw new Exception("Elbow never swung to relieve the wrist ("+corrected+" of 48)");
            log("PASS wrist limits: "+corrected+" of 48 extreme hand rotations swing the elbow; wrist angles finite");
        }
        private static void GloveRoll(Action<string> log)
        {
            var bones=InstalledBones();
            var arm=Installed(bones,-1);
            var display=bones.Single(b=>b.Name=="SE_RigLForearm2"); var palm=arm.Palm.Bone;
            Matrix expected=display.GetAbsoluteRigTransform()*Matrix.Invert(palm.GetAbsoluteRigTransform());
            float upper=Length(arm,true),lower=Length(arm,false);
            Vector3 collar=arm.Collar.Bone.AbsoluteTransform.Translation;
            foreach(float screen in new[] {0f,.5f,1f})
                for(int i=0;i<40;i++)
                {
                    var target=Matrix.CreateFromYawPitchRoll(i*.04f-1.2f,i*.02f-.6f,i*.05f);
                    target.Translation=new Vector3(-.2f+i*.004f,1.25f,-.4f);
                    if(!ArmSkeleton.Apply(arm,target,new ArmSolve.Options(true,true,screen),1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Screen solve failed at "+screen);
                    var desired=arm.PalmOffset*target.GetOrientation();
                    Near(Vector3.Distance(palm.AbsoluteTransform.Translation,target.Translation),0,"Screen solve loses the controller");
                    Near(Vector3.Distance(palm.AbsoluteTransform.Up,desired.Up)+Vector3.Distance(palm.AbsoluteTransform.Right,desired.Right),0,"Screen solve turns the palm",.0005f);
                    if(Vector3.Distance(arm.Upper.Bone.AbsoluteTransform.Translation,target.Translation)<upper+lower-.02f)
                    {
                        if(screen<=0) Near(Length(arm,true),upper,"Screen solve stretched the upper arm",.0005f);
                        if(screen<=0) Near(Length(arm,false),lower,"Screen solve stretched the forearm",.0005f);
                        else if(Math.Abs(Length(arm,false)/lower-1)>.26f) throw new Exception("Screen solve changed the forearm by "+Length(arm,false)/lower);
                    }
                    // The glove rides the forearm axis with the hand's full roll. Screen in use with the forearm on the hand's line:
                    // the glove keeps the hand's orientation.
                    Vector3 axis=Vector3.Normalize(palm.AbsoluteTransform.Translation-arm.Lower.Bone.AbsoluteTransform.Translation);
                    float left=ArmSolve.Roll(axis,Carried(arm,display),desired,arm.Rig);
                    if(Math.Abs(left)>1.5f) throw new Exception("Roll left between the glove and the hand: "+left);
                    var relation=(display.GetAbsoluteRigTransform()*Matrix.Invert(arm.Lower.Bone.GetAbsoluteRigTransform())).GetOrientation();
                    Vector3 gloveAxis=Vector3.TransformNormal(arm.Rig.LowerAxis,Matrix.Invert(relation)*display.AbsoluteTransform.GetOrientation());
                    Near(Vector3.Dot(Vector3.Normalize(gloveAxis),axis),1,"Glove left the forearm axis",.001f);
                    Vector3 elbow=arm.Lower.Bone.AbsoluteTransform.Translation,onLine=display.AbsoluteTransform.Translation-elbow;
                    Near((onLine-axis*Vector3.Dot(onLine,axis)).Length(),0,"Glove left the line from elbow to hand",.001f);
                    if(screen>=1)
                    {
                        Matrix relative=display.AbsoluteTransform*Matrix.Invert(palm.AbsoluteTransform);
                        Vector3 glove=Vector3.Normalize(Vector3.TransformNormal(arm.Rig.PalmLong,palm.AbsoluteTransform));
                        if(Vector3.Dot(axis,glove)>.9998f)
                            Near(Vector3.Distance(relative.Up,expected.Up)+Vector3.Distance(relative.Right,expected.Right),0,"Tablet turns relative to the hand",.02f);
                        float needed=Vector3.Distance(palm.AbsoluteTransform.Translation-glove*lower,arm.Upper.Bone.AbsoluteTransform.Translation);
                        float fold=MathHelper.ToDegrees((float)Math.Acos(MathHelper.Clamp(Vector3.Dot(axis,glove),-1,1)));
                        if(needed>=upper && fold>1) throw new Exception("Forearm leaves the glove line: "+fold+" degrees");
                        if(needed>=upper) Near(Length(arm,true)/upper,Length(arm,false)/lower,"Screen solve stretched the upper arm alone",.002f);
                    }
                    arm.Restore(true);
                    Near(Vector3.Distance(arm.Collar.Bone.AbsoluteTransform.Translation,collar),0,"Screen shoulder shift left on the collarbone");
                }
            // Screen in use with the hand anywhere around the body at any rotation: the stretch stays within the free limit and the sleeve folds at most 40 degrees at the cuff.
            var random=new Random(7);
            for(int i=0;i<400;i++)
            {
                var target=Matrix.CreateFromYawPitchRoll((float)random.NextDouble()*6.3f,(float)random.NextDouble()*6.3f,(float)random.NextDouble()*6.3f);
                var direction=Vector3.Normalize(new Vector3((float)random.NextDouble()*2-1,(float)random.NextDouble()*2-1,(float)random.NextDouble()*-1.5f+.3f));
                target.Translation=arm.Upper.Bone.AbsoluteTransform.Translation+direction*(upper+lower)*(.25f+(float)random.NextDouble()*1.1f);
                if(!ArmSkeleton.Apply(arm,target,new ArmSolve.Options(true,true,1),1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Reaching screen solve failed");
                if(arm.Collar.Bone.AbsoluteTransform.Translation.X-collar.X>1e-4f) throw new Exception("Screen shoulder shift moved the left collarbone toward the chest");
                if(Length(arm,true)>upper*ArmSolve.MaxStretch+.001f || Length(arm,false)>lower*ArmSolve.MaxStretch+.001f)
                    throw new Exception("Reaching screen arm stretched past the free limit: "+Length(arm,true)/upper+"/"+Length(arm,false)/lower);
                Vector3 elbow=arm.Lower.Bone.AbsoluteTransform.Translation,cuff=display.AbsoluteTransform.Translation,hand=palm.AbsoluteTransform.Translation;
                float fold=MathHelper.ToDegrees((float)Math.Acos(MathHelper.Clamp(Vector3.Dot(Vector3.Normalize(cuff-elbow),Vector3.Normalize(hand-cuff)),-1,1)));
                if(fold>40) throw new Exception("Screen arm folds the sleeve: "+fold+" degrees");
                arm.Restore(true);
            }
            // Adaptive arms off: fixed lengths even with the screen in use.
            for(int i=0;i<20;i++)
            {
                var target=Matrix.CreateFromYawPitchRoll(i*.04f-.6f,.2f,i*.05f); target.Translation=new Vector3(-.2f,1.25f,-.35f);
                if(!ArmSkeleton.Apply(arm,target,new ArmSolve.Options(false,false,1),1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Fixed screen solve failed");
                Near(Length(arm,true),upper,"Fixed mode stretched the upper arm with the screen in use",.0005f);
                Near(Length(arm,false),lower,"Fixed mode stretched the forearm with the screen in use",.0005f);
                arm.Restore(true);
            }
            var head=MatrixD.CreateWorld(new Vector3D(0,1.6,0),Vector3D.Forward,Vector3D.Up);
            var facing=MatrixD.CreateWorld(new Vector3D(0,1.45,-.5),Vector3D.Normalize(new Vector3D(0,-.15,-.5)),Vector3D.Up);
            Near(TrackedArms.ScreenAttention(facing,head),1,"Screen facing the eye in view does not raise the weight");
            Near(TrackedArms.ScreenAttention(MatrixD.CreateWorld(facing.Translation,-facing.Forward,Vector3D.Up),head),0,"Screen turned away keeps the weight");
            Near(TrackedArms.ScreenAttention(facing,MatrixD.CreateWorld(head.Translation,Vector3D.Right,Vector3D.Up)),0,"Screen outside the view keeps the weight");
            log("PASS wrist screen: 120 installed-bone poses and 400 random reaches: the glove rides the forearm with the hand's full roll; screen in use, the forearm runs along the glove line at real arm lengths; continuous in between; the collarbone never moves toward the chest; attention weights only a facing screen in view");
        }
        private static void Placement(Action<string> log)
        {
            var level=Matrix.CreateWorld(new Vector3(.02f,1.6f,0),Vector3.Forward,Vector3.Up);
            var shift=BodyPlacement.Shift(level,new Vector3(0,1.7f,.2f));
            Near(shift.X,.02f,"Shift sideways"); Near(shift.Y,-.1f+BodyPlacement.DropDeadband,"Shift vertical"); Near(shift.Z,BodyPlacement.EyeAhead-.2f,"Shift forward");
            Near(BodyPlacement.Shift(level,new Vector3(0,1.62f,.2f)).Y,0,"Small drop is not ignored");
            // A head pitched down keeps its joint behind and above the eyes, so looking down does not lower the body.
            var pitched=Matrix.CreateRotationX(-.6f)*Matrix.CreateTranslation(0,1.6f,0);
            var down=BodyPlacement.Shift(pitched,new Vector3(0,1.65f,.08f));
            if(down.Y<-.005f) throw new Exception("Looking down lowers the body: "+down.Y);
            if(down.Z>0) throw new Exception("Pitched head does not keep the shoulders behind: "+down.Z);
            Near(BodyPlacement.Shift(level,new Vector3(0,2.5f,.2f)).Y,-BodyPlacement.MaxDrop,"Drop is not capped");
            var bones=InstalledBones();
            var left=Installed(bones,-1); var right=Installed(bones,1);
            var ribcage=bones.Single(b=>b.Name=="SE_RigRibcage"); var head=bones.Single(b=>b.Name=="SE_RigHead"); var dummy=bones.Single(b=>b.Name=="HeadDummy");
            float ahead=head.AbsoluteTransform.Translation.Z-dummy.AbsoluteTransform.Translation.Z;
            if(ahead<.15f || ahead>.25f) throw new Exception("Installed camera dummy is "+ahead+" m ahead of the head joint");
            Vector3 shoulder=left.Upper.Bone.AbsoluteTransform.Translation,chest=ribcage.AbsoluteTransform.Translation,hand=left.Palm.Bone.AbsoluteTransform.Translation;
            var pelvis=bones.Single(b=>b.Name=="SE_RigPelvis"); var foot=bones.Single(b=>b.Name=="SE_RigLFoot");
            Vector3 headRest=head.AbsoluteTransform.Translation,footRest=foot.AbsoluteTransform.Translation;
            var roots=bones.Where(b=>b.Parent==null).Select(b=>new ArmSkeleton.SavedBone {Bone=b}).ToList();
            var eye=ArmSkeleton.Below(head,dummy); var spine=SpineLean.Find(name=>bones.FirstOrDefault(b=>b.Name==name),head,eye);
            // Without vanilla's first-person centring the dummy sits 20 cm ahead of the capsule; centring slides the body back.
            BodyPlacement.Center(bones,roots,dummy);
            Near(Math.Abs(dummy.AbsoluteTransform.Translation.X)+Math.Abs(dummy.AbsoluteTransform.Translation.Z),0,"Centring left the camera dummy off the capsule axis",.001f);
            if(ribcage.AbsoluteTransform.Translation.Z<chest.Z+.15f) throw new Exception("Centring did not slide the torso back");
            foreach(var root in roots) root.Restore(false);
            bones[0].ComputeAbsoluteTransform(true,true);
            Near(Vector3.Distance(ribcage.AbsoluteTransform.Translation,chest),0,"Centring did not restore",.0005f);
            BodyPlacement.Stoop(bones,roots,spine,head,-.08f);
            Vector3 moved=head.AbsoluteTransform.Translation-headRest;
            if(moved.Y>-.05f || moved.Y<-.09f) throw new Exception("Stoop did not lower the head: "+moved.Y);
            Near(Math.Abs(moved.X)+Math.Abs(moved.Z),0,"Stoop moved the head sideways or forward",.001f);
            Near(foot.AbsoluteTransform.Translation.Y,footRest.Y,"Stoop lifted or sank the feet",.001f);
            if(foot.AbsoluteTransform.Translation.Z<footRest.Z+.1f) throw new Exception("Stoop did not move the hips and feet back");
            if(ribcage.AbsoluteTransform.Translation.Y>chest.Y-.02f) throw new Exception("Stoop did not lower the chest");
            Vector3 leaned=left.Upper.Bone.AbsoluteTransform.Translation,chestLeaned=ribcage.AbsoluteTransform.Translation;
            if(!BodyPlacement.Shoulders(left,right,new Vector3(.01f,0,-.11f))) throw new Exception("Shoulder placement failed");
            Near(Vector3.Distance(left.Upper.Bone.AbsoluteTransform.Translation,leaned+new Vector3(.01f,0,-.11f)),0,"Left shoulder did not follow the shift",.0005f);
            Near(Vector3.Distance(ribcage.AbsoluteTransform.Translation,chestLeaned),0,"Shoulder shift moved the torso");
            left.Restore(); right.Restore();
            foreach(var root in roots) root.Restore(false);
            SpineLean.Restore(spine);
            bones[0].ComputeAbsoluteTransform(true,true);
            Near(Vector3.Distance(head.AbsoluteTransform.Translation,headRest),0,"Stoop did not restore the head",.0005f);
            Near(Vector3.Distance(left.Upper.Bone.AbsoluteTransform.Translation,shoulder),0,"Placement did not restore",.0005f);
            if(BodyPlacement.Shoulders(left,right,new Vector3(float.NaN,0,0))) throw new Exception("Invalid shoulder shift accepted");
            log("PASS body placement: camera dummy centred when vanilla does not; head joint "+BodyPlacement.EyeAhead+" m behind the eyes along the head axis; a stoop bends the spine with the hips back and the feet on the floor (up to "+BodyPlacement.MaxDrop+" m), looking down does not; collarbones carry the arms forward and everything restores; camera dummy "+ahead.ToString("0.000")+" m ahead of the head joint");
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
            var arm=Installed(bones,-1);
            // Recorded watch pose.
            Matrix desired=Matrix.Identity;
            desired.Right=new Vector3(-.9470054f,-.2854625f,-.1475649f);
            desired.Up=new Vector3(-.3211800f,.8273234f,.4607469f);
            desired.Backward=new Vector3(-.0094374f,.4837239f,-.8751677f);
            desired.Translation=new Vector3(-.0685918f,1.2402833f,-.4206055f);
            var shoulder=new Vector3(-.2063112f,1.4152480f,.1555137f);
            var upper=arm.Upper.Bone;
            var upperPose=upper.AbsoluteTransform; upperPose.Translation=shoulder;
            upper.SetCompleteTransformFromAbsoluteMatrix(ref upperPose,false); upper.ComputeAbsoluteTransform(true,true);
            var target=Matrix.Invert(arm.PalmOffset)*desired;
            if(!ArmSkeleton.Apply(arm,target,ArmSolve.Options.Local,1,ArmSkeleton.Fingers.Native,0)) throw new Exception("Recorded watch pose failed");
            var palm=arm.Palm.Bone;
            Vector3 forearm=Vector3.Normalize(palm.AbsoluteTransform.Translation-arm.Lower.Bone.AbsoluteTransform.Translation);
            ArmSolve.Wrist(forearm,Vector3.Up,arm.PalmOffset*target.GetOrientation(),arm.Rig,out float pitch,out float side,out _);
            Near(Vector3.Distance(palm.AbsoluteTransform.Translation,desired.Translation),0,"Watch pose loses tracked palm");
            Near(Vector3.Distance(palm.AbsoluteTransform.Up,desired.Up),0,"Watch pose changes tool/palm orientation",.0002f);
            if(Vector3.Distance(upper.AbsoluteTransform.Translation,shoulder)>ArmSolve.ShoulderTravel+.0001f) throw new Exception("Watch pose shoulder exceeds travel limit");
            log("PASS recorded watch pose (captured under the earlier chest placement): palm target retained; wrist pitch "+pitch.ToString("0")+", side "+side.ToString("0")+" degrees, shoulder travel bounded.");
        }
    }
}
#endif
