using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Character;
using VRageMath;
using VRageRender.Animations;
using Arm = SpaceEngineersVR.Player.ArmSkeleton.Arm;

namespace SpaceEngineersVR.Player
{
    // Head joint EyeAhead behind the headset along the head axis: a lower headset stoops the spine, the collarbones take the horizontal part.
    internal static class BodyPlacement
    {
        internal const float EyeAhead=.09f,MaxDrop=.15f,DropDeadband=.03f;
        private static readonly List<ArmSkeleton.SavedBone> roots=new List<ArmSkeleton.SavedBone>();
        private static MyCharacterBone[] owner;
        // X and Z applied to the collarbones, Y the head drop taken up by the spine.
        internal static Vector3 LastShift { get; private set; }
        internal static Vector3 Shift(Matrix headset,Vector3 head)
        {
            Vector3 shift=headset.Translation+Vector3.Normalize(headset.Backward)*EyeAhead-head;
            shift.Y=MathHelper.Clamp(Math.Min(0,shift.Y+DropDeadband),-MaxDrop,0);
            return shift;
        }
        internal static bool Apply(MyCharacter character,SpineLean.Chain spine,MyCharacterBone head,MyCharacterBone dummy,Arm left,Arm right,Matrix headset)
        {
            LastShift=Vector3.Zero;
            var bones=character.AnimationController.CharacterBones;
            if(bones==null || head==null || !headset.IsValid()) return false;
            if(owner!=bones)
            {
                roots.Clear();
                foreach(var bone in bones) if(bone.Parent==null) roots.Add(new ArmSkeleton.SavedBone { Bone=bone });
                owner=bones;
            }
            // Vanilla centres the camera dummy over the capsule only while the character is the camera controller.
            if(dummy!=null) Center(bones,roots,dummy);
            Vector3 shift=Shift(headset,head.AbsoluteTransform.Translation);
            if(!shift.IsValid() || shift.LengthSquared()>1) return false;
            if(shift.Y<-.005f && spine!=null) Stoop(bones,roots,spine,head,shift.Y);
            Shoulders(left,right,new Vector3(shift.X,0,shift.Z));
            LastShift=shift;
            return true;
        }
        internal static void Center(MyCharacterBone[] bones,List<ArmSkeleton.SavedBone> roots,MyCharacterBone dummy)
        {
            Vector3 offset=dummy.AbsoluteTransform.Translation; offset.Y=0;
            if(offset.LengthSquared()<1e-6f || offset.LengthSquared()>1) return;
            foreach(var root in roots) root.SaveOnce();
            MyCharacterBone.TranslateAllBones(bones,-offset);
            foreach(var root in roots) ArmSkeleton.Remember(root);
        }
        // Hinge the two lowest spine joints by the drop, then slide the skeleton back so the head stays under the headset.
        internal static void Stoop(MyCharacterBone[] bones,List<ArmSkeleton.SavedBone> roots,SpineLean.Chain spine,MyCharacterBone head,float drop)
        {
            Vector3 before=head.AbsoluteTransform.Translation;
            Vector3 pivot=spine.Joints[0].Bone.AbsoluteTransform.Translation;
            Vector3 from=before-pivot;
            float length=from.Length();
            if(length<.3f) return;
            Vector3 to=new Vector3(from.X,from.Y+drop,0);
            float forward=length*length-to.X*to.X-to.Y*to.Y;
            if(forward<=0) return;
            to.Z=-(float)Math.Sqrt(forward);
            var half=Quaternion.Slerp(Quaternion.Identity,SpineLean.Between(from/length,Vector3.Normalize(to)),.5f);
            for(int i=0;i<2 && i<spine.Joints.Length;i++)
            {
                var joint=spine.Joints[i]; joint.SaveOnce();
                var turned=joint.Bone.AbsoluteTransform.GetOrientation()*Matrix.CreateFromQuaternion(half);
                joint.Bone.SetCompleteTransformFromAbsoluteMatrix(ref turned,true); joint.Bone.ComputeAbsoluteTransform(true,true);
                ArmSkeleton.Remember(joint);
            }
            spine.Applied=true;
            Vector3 travel=head.AbsoluteTransform.Translation-before; travel.Y=0;
            foreach(var root in roots) root.SaveOnce();
            MyCharacterBone.TranslateAllBones(bones,-travel);
            foreach(var root in roots) ArmSkeleton.Remember(root);
        }
        internal static bool Shoulders(Arm left,Arm right,Vector3 shift)
        {
            if(!shift.IsValid() || shift.LengthSquared()>1) return false;
            Move(left,shift); Move(right,shift);
            return true;
        }
        // The collar is restored with the arm; a mirrored hold has already saved it.
        private static void Move(Arm arm,Vector3 shift)
        {
            var collar=arm?.Collar;
            if(collar==null || shift.LengthSquared()<1e-8f) return;
            collar.SaveOnce();
            var absolute=collar.Bone.AbsoluteTransform; absolute.Translation+=shift;
            collar.Bone.SetCompleteTransformFromAbsoluteMatrix(ref absolute,false); collar.Bone.ComputeAbsoluteTransform(true,true);
            ArmSkeleton.Remember(collar);
        }
        internal static void Restore(MyCharacter character)
        {
            if(character?.AnimationController?.CharacterBones!=owner) return;
            bool restored=false;
            foreach(var root in roots) restored|=root.Restore(false);
            if(restored) foreach(var root in roots) root.Bone.ComputeAbsoluteTransform(true,true);
        }
        internal static void Reset() { roots.Clear(); owner=null; LastShift=Vector3.Zero; }
    }
}
