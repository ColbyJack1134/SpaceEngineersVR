using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using SpaceEngineersVR.Player;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class CockpitHandTests
    {
        internal static void ExportGrip(string output,string name,bool left,Matrix pose,float trigger,string suffix)
        {
            var arm=ArmTests.InstalledBones(); string side=left ? "L":"R";
            arm.Single(b=>b.Name=="SE_Rig"+side+"Palm").SetCompleteTransformFromAbsoluteMatrix(ref pose,false);
            Curl(arm,side,false,true,trigger);
            var export=new PoseExport(); foreach(var bone in arm) export.absolute[bone.Name]=Elements(bone.AbsoluteTransform);
            using(var file=File.Create(Path.Combine(output,"stick-grip-"+name+"-"+side+suffix+".json")))
                new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
        }

        [DataContract]
        public sealed class PoseExport
        {
            [DataMember] public Dictionary<string,float[]> absolute=new Dictionary<string,float[]>();
            [DataMember(EmitDefaultValue=false)] public float[] label,seat,pointer,grip;
        }
        private static float[] Elements(Matrix m) => new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
        private static Vector3 Tip(MyCharacterBone bone,bool pinch=false) => Vector3.Transform(new Vector3(pinch ? -.025f:CockpitHandPose.Tip,0,0),bone.AbsoluteTransform);
        private static void Curl(MyCharacterBone[] bones,string side,bool pinch,bool stick=false,float trigger=0)
        {
            foreach(var bone in bones.Where(b=>b.Name.StartsWith("SE_Rig"+side+"_"))) bone.Rotation=stick ? CockpitHandPose.StickRotation(bone.Name,trigger):CockpitHandPose.Rotation(bone.Name,pinch);
            bones.Single(b=>b.Name=="SE_Rig"+side+"Palm").ComputeAbsoluteTransform(true,true);
        }
        public static void Run(Action<string> log)
        {
            foreach(bool ship in new[] {false,true}) foreach(bool owned in new[] {false,true}) foreach(bool near in new[] {false,true})
            {
                if(FlightAxes.SecondaryGrip(true,ship,owned,near,Vector2.Zero)!=(ship && !owned && !near) ||
                    FlightAxes.SecondaryGrip(true,ship,owned,near,new Vector2(.5f,0)))
                    throw new Exception("Secondary grip conflicts with flight roll or joystick capture");
            }
            foreach(bool owned in new[] {false,true}) foreach(bool near in new[] {false,true})
                if(FlightAxes.SecondaryGrip(true,false,owned,near,new Vector2(.5f,.5f),turret:true)!=(!owned && !near))
                    throw new Exception("Turret locking conflicts with aiming or physical joystick ownership");
            foreach(bool left in new[] {true,false})
            {
                var arm=ArmTests.InstalledBones(); var side=left ? "L":"R";
                var palm=arm.Single(b=>b.Name=="SE_Rig"+side+"Palm"); var lower=arm.Single(b=>b.Name=="SE_Rig"+side+"Forearm1");
                var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),left ? -1:1);
                var grips=CockpitRig.All.Select(r=>left ? r.Left:r.Right).Where(x=>x!=null).Select(x=>x.Palm(left));
                foreach(var grip in grips) foreach(float angle in new[] {0f,.3f,-.3f})
                {
                    var attached=Matrix.Invert(correction)*grip*Matrix.CreateRotationX(angle)*Matrix.CreateTranslation(.04f,.12f,-.05f);
                    var controller=Matrix.Invert(CockpitHandPose.GripWrist(Matrix.Identity))*attached;
                    if(CockpitControls.GripDistance(controller,attached)>.00001f)
                        throw new Exception("Joystick capture does not match the displayed grip");
                    controller.Translation+=Vector3.Right*.16f;
                    if(CockpitControls.GripDistance(controller,attached)<CockpitControls.CaptureRadius)
                        throw new Exception("Joystick capture accepts a controller outside its reach");
                }
            }
            string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyCharacterBone).Assembly.Location),"..","Content"));
            var bones=ArmTests.InstalledBones();
            foreach(string side in new[] {"L","R"})
            {
                var palm=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var index=bones.Single(b=>b.Name=="SE_Rig"+side+"_Index_3");
                var middle=bones.Single(b=>b.Name=="SE_Rig"+side+"_Middle_3");
                var thumb=bones.Single(b=>b.Name=="SE_Rig"+side+"_Thumb_3");
                Curl(bones,side,false,true);
                var middleRest=Tip(middle); var thumbRest=Tip(thumb); var palmRest=palm.AbsoluteTransform;
                float previous=Vector3.Distance(Tip(index),palmRest.Translation);
                foreach(float pressure in new[] {.25f,.5f,.75f,1f})
                {
                    Curl(bones,side,false,true,pressure);
                    float distance=Vector3.Distance(Tip(index),palmRest.Translation);
                    if(distance>=previous || Vector3.Distance(Tip(middle),middleRest)>.00001f ||
                        Vector3.Distance(Tip(thumb),thumbRest)>.00001f || palm.AbsoluteTransform!=palmRest)
                        throw new Exception("Joystick trigger curl moves the held palm/other fingers or reverses: "+side);
                    previous=distance;
                }
            }
            log("PASS installed joystick fingers: both hands curl continuously toward the palm with trigger pressure; palm, thumb and lower fingers remain fixed.");
            foreach(string side in new[] {"L","R"}) foreach(bool pinch in new[] {false,true})
            {
                var palm=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var lower=bones.Single(b=>b.Name=="SE_Rig"+side+"Forearm1");
                var index=bones.Single(b=>b.Name=="SE_Rig"+side+"_Index_3");
                var thumb=bones.Single(b=>b.Name=="SE_Rig"+side+"_Thumb_3");
                var offset=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side=="L" ? -1 : 1);
                var contact=CockpitHandPose.Contact(palm,index,thumb,pinch);
                for(int i=0;i<50;i++)
                {
                    var world=MatrixD.CreateFromYawPitchRoll(i*.13,i*.09,-i*.04)*MatrixD.CreateTranslation(2e6,-3e6,4e6);
                    var anchor=Vector3D.Transform(new Vector3D(.1,-.2,-.3),world);
                    var wrist=CockpitHandPose.Attach(world,offset,contact,anchor);
                    var pose=(Matrix)((MatrixD)offset*wrist*MatrixD.Invert(world));
                    palm.SetCompleteTransformFromAbsoluteMatrix(ref pose,false);
                    Curl(bones,side,pinch);
                    var actual=pinch ? (Tip(index,true)+Tip(thumb,true))*.5f : Tip(index);
                    if(Vector3D.Distance(Vector3D.Transform(actual,world),anchor)>.0001) throw new Exception("Captured "+side+" fingertip/pinch misses its control");
                    if(pinch && Vector3.Distance(Tip(index,true),Tip(thumb,true))>.03f) throw new Exception("Pinch cannot surround a small lever");
                }
            }
            log("PASS cockpit hand attachment: installed astronaut fingers, both hands, point/pinch, 200 rotated large-world anchors.");
            foreach(string side in new[] {"L","R"})
            {
                var glove=GloveGeometry.Load(content,GloveGeometry.DefaultModel,side=="L");
                var palmBone=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var lower=bones.Single(b=>b.Name=="SE_Rig"+side+"Forearm1");
                var index=bones.Single(b=>b.Name=="SE_Rig"+side+"_Index_3");
                var correction=ArmMath.PalmCorrection(palmBone.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side=="L" ? -1 : 1);
                var fingerPose=CockpitHandPose.FingerPose(palmBone,index,false);
                for(int i=0;i<50;i++)
                {
                    var aim=MatrixD.CreateFromYawPitchRoll(i*.11,i*.04,-i*.07)*MatrixD.CreateTranslation(1e6,-2e6,3e6);
                    var wrist=(MatrixD)CockpitHandPose.GripWrist(Matrix.Identity)*aim;
                    var contact=Vector3.Transform(new Vector3(CockpitHandPose.Tip,0,0),fingerPose);
                    var palm=(MatrixD)correction*wrist;
                    var localPalm=(Matrix)(palm*MatrixD.Invert(aim));
                    palmBone.SetCompleteTransformFromAbsoluteMatrix(ref localPalm,false); Curl(bones,side,false);
                    var finger=CockpitHandPose.PointContact(wrist,correction,fingerPose);
                    if(Vector3D.Distance(Vector3D.Transform(Tip(index),aim),finger)>.0001)
                        throw new Exception("Grip-relative contact disagrees with the posed fingertip");
                    var pointer=CockpitHandPose.PointPose(wrist,correction,fingerPose);
                    var ray=HandInteraction.RayForPose(pointer);
                    var direction=Vector3D.Normalize(Vector3D.TransformNormal(-index.AbsoluteTransform.Right,aim));
                    if(Vector3D.Distance(ray.From,finger)>.0001 || Vector3D.Dot(ray.Direction,direction)<.9999)
                        throw new Exception("Finger ray disagrees with the posed index direction");
                    var menuPointer=(MatrixD)glove.PointFrame*wrist;
                    if(Vector3D.Distance(menuPointer.Translation,pointer.Translation)>.0001 || Vector3D.Dot(menuPointer.Forward,pointer.Forward)<.99999)
                        throw new Exception("Menu glove and native hand disagree on the shared fingertip ray");
                    var probe=new CockpitProbe(pointer,side=="L");
                    var front=probe.Start+pointer.Forward*CockpitProbe.Radius;
                    var center=finger+pointer.Right*(side=="L" ? .0025:-.0025)-pointer.Up*.0005;
                    if(Vector3D.Distance(front,center+pointer.Forward*CockpitProbe.TipExtension)>.0001)
                        throw new Exception("Contact capsule moved away from the rendered fingertip");
                    var button=MatrixD.CreateScale(.1,.15,.04)*aim;
                    var from=Vector3D.Transform(new Vector3D(.04,.02,.08),aim);
                    var surface=HandInteraction.ClosestControlPoint(button,from);
                    var expected=Vector3D.Transform(new Vector3D(.04,.02,.02),aim);
                    if(Vector3D.Distance(surface,expected)>.0001) throw new Exception("Nearby control query lost its oriented face at large coordinates");
                    var held=CockpitHandPose.Attach(wrist,correction,contact,surface);
                    if(Vector3D.Distance(Vector3D.Transform(contact,(MatrixD)correction*held),surface)>.0001)
                        throw new Exception("Held world control contact slipped");
                }
            }
            if(!HandInteraction.SmallControl(MatrixD.CreateScale(.15,.22,.37)) || HandInteraction.SmallControl(MatrixD.CreateScale(1.7,2.1,2.2)))
                throw new Exception("Physical interaction confused a keypad with a whole door");
            log("PASS character pointing: ray follows the posed index tip and direction, both hands, 100 rotated large-world poses and held face contacts; whole-door touch rejected.");
        }
        internal static void NativeContacts(Action<string> log)
        {
            var entity=new VRage.Game.Entity.MyEntity();
            entity.RefreshModels(@"Models\Cubes\Large\ButtonPanel.mwm",null);
            var dummies=entity.Model.Dummies;
            for(int pose=0;pose<4;pose++)
            {
                var world=MatrixD.CreateFromYawPitchRoll(.3*pose,-.2*pose,.1*pose)*MatrixD.CreateTranslation(pose*1e6,-pose*2e6,pose*3e6);
                entity.PositionComp.SetWorldMatrix(ref world);
                foreach(var dummy in dummies.Where(d=>d.Key.StartsWith("detector_panel_button_")))
                {
                    MatrixD activation=(MatrixD)dummy.Value.Matrix*world;
                    var normal=Vector3D.Normalize(activation.Backward);
                    var from=activation.Translation+normal*.12;
                    if(!HandInteraction.SurfaceContact(entity,activation,from,out var contact))
                        throw new Exception("Physical button face not acquired: "+dummy.Key);
                    if(Vector3D.Distance(contact,activation.Translation)<.001)
                        throw new Exception("Button press still uses the detector center instead of its mesh face");
                    if(HandInteraction.SurfaceContact(entity,activation,activation.Translation+normal*.5,out _))
                        throw new Exception("Physical press exceeds the reach limit");
                }
            }
            log("PASS native button contacts: installed ButtonPanel mesh, four buttons, rotated large-world poses, physical face hit and reach rejection.");
        }
        public static void Preview(string output)
        {
            string content=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(MyCharacterBone).Assembly.Location),"..","Content"));
            var glove=GloveGeometry.Load(content,GloveGeometry.DefaultModel,false);
            var reference=new PoseExport { pointer=Elements(glove.PointFrame),grip=Elements(CockpitHandPose.GripWrist(Matrix.Identity)) };
            using(var file=File.Create(Path.Combine(output,"menu-glove-reference.json")))
                new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,reference);
            foreach(bool extended in new[] {false,true}) foreach(bool rigid in new[] {false,true})
            {
                var arm=ArmTests.InstalledBones();
                var palm=arm.Single(b=>b.Name=="SE_RigLPalm");
                var lower=arm.Single(b=>b.Name=="SE_RigLForearm1"); var upper=lower.Parent;
                Matrix desired=Matrix.Identity;
                desired.Right=new Vector3(-.9470054f,-.2854625f,-.1475649f);
                desired.Up=new Vector3(-.3211800f,.8273234f,.4607469f);
                desired.Backward=new Vector3(-.0094374f,.4837239f,-.8751677f);
                desired.Translation=new Vector3(-.0685918f,1.2402833f,-.4206055f);
                var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),-1);
                if(extended)
                {
                    desired=correction*CockpitHandPose.GripWrist(Matrix.Identity);
                    desired.Translation=new Vector3(-.25f,1.35f,-.55f);
                }
                if(!ArmMath.ApplyPose(upper,lower,palm,Matrix.Invert(correction)*desired,correction,new Vector3(-.55f,-1,.3f),rigidWrist:rigid))
                    throw new Exception("Wrist inspection solve failed");
                Curl(arm,"L",false);
                var export=new PoseExport(); foreach(var bone in arm) export.absolute[bone.Name]=Elements(bone.AbsoluteTransform);
                using(var file=File.Create(Path.Combine(output,(extended ? "left-extended-":"left-wrist-")+(rigid ? "rigid.json":"articulated.json"))))
                    new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
            foreach(bool rigid in new[] {true,false}) foreach(int reach in new[] {0,1})
            {
                var arm=ArmTests.InstalledBones();
                var palm=arm.Single(b=>b.Name=="SE_RigLPalm");
                var lower=arm.Single(b=>b.Name=="SE_RigLForearm1"); var upper=lower.Parent;
                var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),-1);
                var desired=CockpitRig.Find(CockpitLayout.Fighter).Left.Palm(true)*Matrix.CreateRotationX(reach*.3f);
                desired.Translation=new Vector3(-.28f,1.05f+reach*.1f,-.3f-reach*.08f);
                if(!ArmMath.ApplyPose(upper,lower,palm,Matrix.Invert(correction)*desired,correction,new Vector3(-.55f,-1,.3f),rigidWrist:rigid))
                    throw new Exception("Joystick arm inspection solve failed");
                if(Vector3.Distance(palm.AbsoluteTransform.Translation,desired.Translation)>.0001f ||
                    Vector3.Distance(palm.AbsoluteTransform.Up,desired.Up)>.0001f)
                    throw new Exception("Joystick wrist articulation moved its palm target");
                Curl(arm,"L",false,true);
                var export=new PoseExport(); foreach(var bone in arm) export.absolute[bone.Name]=Elements(bone.AbsoluteTransform);
                using(var file=File.Create(Path.Combine(output,"joystick-arm-"+reach+"-"+(rigid ? "rigid":"flexible")+".json")))
                    new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
            foreach(bool left in new[] {true,false}) foreach(string state in new[] {"open","touch","trigger","grip","closed"})
            {
                var arm=ArmTests.InstalledBones(); string side=left ? "L":"R";
                var palm=arm.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var pose=CockpitRig.Find(CockpitLayout.ControlSeat).Handles[left ? 0:1].Palm(left,.5f);
                palm.SetCompleteTransformFromAbsoluteMatrix(ref pose,false);
                var curls=new float[5];
                ControllerFingers.Fallback(curls,state=="trigger" || state=="closed" ? 1:0,state=="grip" || state=="closed" ? 1:0);
                if(state=="touch") { curls[0]=.3f; curls[1]=.35f; }
                foreach(var bone in arm.Where(b=>b.Name.StartsWith("SE_Rig"+side+"_")))
                    bone.Rotation=CockpitHandPose.FreeRotation(bone.Name,curls);
                palm.ComputeAbsoluteTransform(true,true);
                var export=new PoseExport(); foreach(var bone in arm) export.absolute[bone.Name]=Elements(bone.AbsoluteTransform);
                using(var file=File.Create(Path.Combine(output,"free-hand-"+side+"-"+state+".json")))
                    new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
            foreach(bool left in new[] {true,false}) foreach(float trigger in new[] {0f,.5f,1f})
            {
                string suffix=trigger==0 ? "":trigger==1 ? "-pressed":"-half";
                foreach(var rig in CockpitRig.All)
                {
                    var stick=left ? rig.Left:rig.Right;
                    if(stick!=null) ExportGrip(output,rig.Subtype,left,stick.Palm(left)*stick.Visual(Vector3.Zero),trigger,suffix);
                }
            }
            foreach(bool left in new[] {true,false}) foreach(float position in new[] {0f,.5f,1f})
                ExportGrip(output,"Bar",left,CockpitRig.Find(CockpitLayout.ControlSeat).Handles[left ? 0:1].Palm(left,position),1,"-"+(int)(position*100));
            foreach(var rig in CockpitRig.All) for(int i=0;i<rig.Handles.Length;i++) foreach(float position in new[] {0f,.25f,.5f,.75f,1f})
            {
                var handle=rig.Handles[i]; string name="Handle-"+rig.Subtype+"-"+i;
                foreach(bool left in new[] {true,false}) ExportGrip(output,name,left,handle.Palm(left,position),1,"-"+(int)(position*100));
                var export=new PoseExport(); export.absolute["visual"]=Elements(handle.Visual(position));
                export.absolute["touch"]=Elements((Matrix)handle.TouchPose*handle.Visual(position));
                using(var file=File.Create(Path.Combine(output,name+"-"+(int)(position*100)+".json")))
                    new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
            var bones=ArmTests.InstalledBones();
            foreach(bool pressed in new[] {false,true})
            {
                var palm=bones.Single(b=>b.Name=="SE_RigRPalm");
                var lower=bones.Single(b=>b.Name=="SE_RigRForearm1");
                var index=bones.Single(b=>b.Name=="SE_RigR_Index_3");
                var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),1);
                // Inspected ButtonPanel button 1; use its sloped face as the press anchor.
                var normal=Vector3D.Normalize(new Vector3D(0,.473,.881));
                var anchor=new Vector3D(-.55715,-.40537,-1.04303)+normal*.02;
                var aim=MatrixD.CreateWorld(anchor+normal*(pressed ? 0 : .12),-normal,Vector3D.Up);
                var finger=CockpitHandPose.FingerPose(palm,index,false);
                var wrist=(MatrixD)CockpitHandPose.GripWrist(Matrix.Identity)*aim;
                wrist.Translation+=anchor+normal*.12-CockpitHandPose.PointContact(wrist,correction,finger);
                if(pressed) wrist=CockpitHandPose.Attach(wrist,correction,CockpitHandPose.Finger(palm,index,false),anchor);
                var pose=(Matrix)((MatrixD)correction*wrist);
                palm.SetCompleteTransformFromAbsoluteMatrix(ref pose,false); Curl(bones,"R",false);
                var export=new PoseExport();
                foreach(var bone in bones) export.absolute[bone.Name]=Elements(bone.AbsoluteTransform);
                using(var file=File.Create(Path.Combine(output,pressed ? "hand-world-press.json" : "hand-world-ray.json")))
                    new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
            foreach(string side in new[] {"L","R"})
            {
                var palm=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var lower=bones.Single(b=>b.Name=="SE_Rig"+side+"Forearm1");
                var index=bones.Single(b=>b.Name=="SE_Rig"+side+"_Index_3");
                var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side=="L" ? -1 : 1);
                var finger=CockpitHandPose.FingerPose(palm,index,false);
                var wrist=(MatrixD)CockpitHandPose.GripWrist(Matrix.Identity);
                var pointer=CockpitHandPose.PointPose(wrist,correction,finger);
                var export=new PoseExport {pointer=Elements((Matrix)pointer),grip=Elements(Matrix.Identity)};
                var pose=(Matrix)((MatrixD)correction*wrist);
                palm.SetCompleteTransformFromAbsoluteMatrix(ref pose,false); Curl(bones,side,false);
                foreach(var bone in bones) export.absolute[bone.Name]=Elements(bone.AbsoluteTransform);
                using(var file=File.Create(Path.Combine(output,"hand-finger-"+side+".json")))
                    new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
            foreach(string side in new[] {"L","R"}) foreach(string control in new[] {"button","lever","cover","hover"})
            {
                bool pinch=control=="lever" || control=="cover";
                var palm=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var index=bones.Single(b=>b.Name=="SE_Rig"+side+"_Index_3");
                var thumb=bones.Single(b=>b.Name=="SE_Rig"+side+"_Thumb_3");
                MatrixD surface;
                var export=new PoseExport();
                if(control=="button")
                {
                    SeatPanel.TryMount(CockpitLayout.Fighter,out surface,out float w,out float h);
                    export.seat=Elements((Matrix)surface);
                    var b=SeatPanel.Keys(true)[5].Bounds;
                    surface.Translation=Vector3D.Transform(new Vector3D((b.Center.X-.5)*w,(.5-b.Center.Y)*h,0),surface);
                }
                else surface=control=="cover" ? CockpitRig.Find(CockpitLayout.Fighter).Levers[9].CoverPose(0) : CockpitLayout.Control(CockpitLayout.Fighter,0,out _);
                if(control=="hover")
                {
                    surface.Translation+=surface.Backward*.025;
                    var eye=new Vector3D(0,.3,.7);
                    var head=MatrixD.CreateWorld(eye,Vector3D.Normalize(surface.Translation-eye),Vector3D.Up);
                    export.label=Elements((Matrix)CockpitTouch.LabelPose(head,surface.Translation,.14f));
                }
                var pose=Matrix.Identity;
                pose.Right=(Vector3)surface.Backward; pose.Up=(Vector3)surface.Up;
                pose.Backward=Vector3.Normalize(Vector3.Cross(pose.Right,pose.Up));
                var contact=CockpitHandPose.Contact(palm,index,thumb,pinch,pinch ? -.025f : CockpitHandPose.Tip);
                pose=(Matrix)CockpitHandPose.Attach(pose,Matrix.Identity,contact,surface.Translation);
                palm.SetCompleteTransformFromAbsoluteMatrix(ref pose,false); Curl(bones,side,pinch);
                foreach(var bone in bones)
                {
                    var m=bone.AbsoluteTransform;
                    export.absolute[bone.Name]=Elements(m);
                }
                using(var file=File.Create(Path.Combine(output,"hand-"+side+"-"+control+".json")))
                    new DataContractJsonSerializer(typeof(PoseExport),new DataContractJsonSerializerSettings {UseSimpleDictionaryFormat=true}).WriteObject(file,export);
            }
        }
    }
}
