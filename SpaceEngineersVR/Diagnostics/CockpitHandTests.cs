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
            [DataMember(EmitDefaultValue=false)] public float[] label,seat;
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
        }
        public static void Preview(string output)
        {
            var bones=ArmTests.InstalledBones();
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
                var contact=CockpitHandPose.Contact(palm,index,thumb,pinch);
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
