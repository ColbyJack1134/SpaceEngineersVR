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
        [DataContract]
        public sealed class PoseExport
        {
            [DataMember] public Dictionary<string,float[]> absolute=new Dictionary<string,float[]>();
            [DataMember(EmitDefaultValue=false)] public float[] label,seat,pointer,grip;
        }
        private static float[] Elements(Matrix m) => new[] {m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44};
        private static Vector3 Tip(MyCharacterBone bone) => Vector3.Transform(new Vector3(-.025f,0,0),bone.AbsoluteTransform);
        private static void Curl(MyCharacterBone[] bones,string side,bool pinch)
        {
            foreach(var bone in bones.Where(b=>b.Name.StartsWith("SE_Rig"+side+"_"))) bone.Rotation=CockpitHandPose.Rotation(bone.Name,pinch);
            bones.Single(b=>b.Name=="SE_Rig"+side+"Palm").ComputeAbsoluteTransform(true,true);
        }
        public static void Run(Action<string> log)
        {
            var bones=ArmTests.InstalledBones();
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
                    var actual=pinch ? (Tip(index)+Tip(thumb))*.5f : Tip(index);
                    if(Vector3D.Distance(Vector3D.Transform(actual,world),anchor)>.0001) throw new Exception("Captured "+side+" fingertip/pinch misses its control");
                    if(pinch && Vector3.Distance(Tip(index),Tip(thumb))>.03f) throw new Exception("Pinch cannot surround a small lever");
                }
            }
            log("PASS cockpit hand attachment: installed astronaut fingers, both hands, point/pinch, 200 rotated large-world anchors.");
            foreach(string side in new[] {"L","R"})
            {
                var palmBone=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var lower=bones.Single(b=>b.Name=="SE_Rig"+side+"Forearm1");
                var index=bones.Single(b=>b.Name=="SE_Rig"+side+"_Index_3");
                var correction=ArmMath.PalmCorrection(palmBone.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side=="L" ? -1 : 1);
                var fingerPose=CockpitHandPose.FingerPose(palmBone,index,false);
                for(int i=0;i<50;i++)
                {
                    var aim=MatrixD.CreateFromYawPitchRoll(i*.11,i*.04,-i*.07)*MatrixD.CreateTranslation(1e6,-2e6,3e6);
                    var wrist=(MatrixD)CockpitHandPose.GripWrist(Matrix.Identity)*aim;
                    var contact=Vector3.Transform(new Vector3(-.025f,0,0),fingerPose);
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
                    var cockpitWrist=CockpitHandPose.CockpitWrist(wrist,correction,fingerPose);
                    localPalm=(Matrix)((MatrixD)correction*cockpitWrist*MatrixD.Invert(aim));
                    palmBone.SetCompleteTransformFromAbsoluteMatrix(ref localPalm,false); Curl(bones,side,false);
                    var outer=Vector3.Transform(new Vector3(CockpitHandPose.CockpitTip,0,0),index.AbsoluteTransform);
                    if(Vector3D.Distance(Vector3D.Transform(outer,aim),pointer.Translation)>.0001 || cockpitWrist.Forward!=wrist.Forward)
                        throw new Exception("Cockpit glove moved the fixed selector or disagrees with its outer fingertip");
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
            foreach(string side in new[] {"L","R"}) foreach(bool corrected in new[] {false,true})
            {
                var palm=bones.Single(b=>b.Name=="SE_Rig"+side+"Palm");
                var lower=bones.Single(b=>b.Name=="SE_Rig"+side+"Forearm1");
                var index=bones.Single(b=>b.Name=="SE_Rig"+side+"_Index_3");
                var correction=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side=="L" ? -1 : 1);
                var finger=CockpitHandPose.FingerPose(palm,index,false);
                var wrist=(MatrixD)CockpitHandPose.GripWrist(Matrix.Identity);
                var pointer=CockpitHandPose.PointPose(wrist,correction,finger);
                var export=new PoseExport {pointer=Elements((Matrix)pointer),grip=Elements(Matrix.Identity)};
                if(corrected) wrist=CockpitHandPose.CockpitWrist(wrist,correction,finger);
                var pose=(Matrix)((MatrixD)correction*wrist);
                palm.SetCompleteTransformFromAbsoluteMatrix(ref pose,false); Curl(bones,side,false);
                foreach(var bone in bones) export.absolute[bone.Name]=Elements(bone.AbsoluteTransform);
                using(var file=File.Create(Path.Combine(output,"cockpit-finger-"+side+(corrected ? "-after" : "-before")+".json")))
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
                    SeatPanel.TryMount(FighterProfile.Subtype,out surface,out float w,out float h);
                    export.seat=Elements((Matrix)surface);
                    var b=SeatPanel.Keys(true)[5].Bounds;
                    surface.Translation=Vector3D.Transform(new Vector3D((b.Center.X-.5)*w,(.5-b.Center.Y)*h,0),surface);
                }
                else surface=control=="cover" ? CockpitCoverGeometry.TouchPose(9,0) : CockpitLayout.Control(FighterProfile.Subtype,0,out _);
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
                var contact=CockpitHandPose.Contact(palm,index,thumb,pinch,pinch ? -.025f : CockpitHandPose.CockpitTip);
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
