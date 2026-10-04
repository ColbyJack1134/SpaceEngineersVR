using System;
using System.Collections.Generic;
using Sandbox.Game.Entities.Character;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    internal static class ArmSkeleton
    {
        internal enum Fingers : byte { Native, Point, Pinch, Stick }
        internal sealed class SavedBone
        {
            public MyCharacterBone Bone;
            public Quaternion Original,Applied;
            public Vector3 Translation,AppliedTranslation;
            public void Save() { Original=Bone.Rotation; Translation=Bone.Translation; }
            public void Restore(bool force)
            {
                // Do not overwrite a new pose that vanilla animation has already written.
                if(force || (Bone.Rotation==Applied && Bone.Translation==AppliedTranslation))
                { Bone.Rotation=Original; Bone.Translation=Translation; }
            }
        }
        internal sealed class Arm
        {
            public SavedBone Upper,Lower,Palm;
            public SavedBone[] Fingers,Twists;
            public MyCharacterBone IndexTip,ThumbTip;
            public Matrix PalmOffset,PointFinger;
            public Vector3 Hint;
            public bool Applied;
            public void Restore(bool force=false)
            {
                if(!Applied) return;
                Upper.Restore(force); Lower.Restore(force); Palm.Restore(force);
                foreach(var twist in Twists) twist.Restore(force);
                if(Fingers!=null) foreach(var finger in Fingers) finger.Restore(force);
                Upper.Bone.ComputeAbsoluteTransform(true,true);
                Applied=false;
            }
        }
        internal static Arm Find(MyCharacter character,string upperName,string lowerName,string palmName,float side)
            => Find(name=>character.AnimationController.FindBone(name,out _),upperName,lowerName,palmName,side);
        internal static Arm Find(Func<string,MyCharacterBone> find,string upperName,string lowerName,string palmName,float side)
        {
            if(string.IsNullOrEmpty(upperName) || string.IsNullOrEmpty(lowerName) || string.IsNullOrEmpty(palmName)) return null;
            var upper=find(upperName);
            var lower=find(lowerName);
            var palm=find(palmName);
            if(upper==null || lower==null || palm==null || !Descends(lower,upper) || !Descends(palm,lower)) return null;
            var fingers=new List<SavedBone>();
            var twists=new List<SavedBone>();
            for(var bone=palm.Parent;bone!=lower;bone=bone.Parent) twists.Add(new SavedBone { Bone=bone });
            string prefix=side<0 ? "SE_RigL_" : "SE_RigR_";
            foreach(string digit in new[] { "Thumb","Index","Middle","Ring","Little" })
                for(int i=1;i<=3;i++)
                {
                    var bone=find(prefix+digit+"_"+i);
                    if(bone!=null) fingers.Add(new SavedBone { Bone=bone });
                }
            var index=find(prefix+"Index_3");
            return new Arm {
                PointFinger=index==null ? Matrix.Identity : CockpitHandPose.FingerPose(palm,index,false),
                Fingers=fingers.ToArray(),Twists=twists.ToArray(),
                IndexTip=find(prefix+"Index_3"),ThumbTip=find(prefix+"Thumb_3"),
                Upper=new SavedBone { Bone=upper },Lower=new SavedBone { Bone=lower },Palm=new SavedBone { Bone=palm },
                PalmOffset=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side),
                Hint=new Vector3(side*0.55f,-1,0.3f) };
        }
        private static bool Descends(MyCharacterBone child,MyCharacterBone parent)
        {
            for(var node=child.Parent;node!=null;node=node.Parent) if(node==parent) return true;
            return false;
        }
        internal static bool Apply(Arm arm, Matrix target, bool adaptive, bool rigid, float scale, Fingers fingers, float trigger)
        {
            if(arm==null || !target.IsValid()) return false;
            arm.Upper.Save(); arm.Lower.Save(); arm.Palm.Save();
            foreach(var twist in arm.Twists) twist.Save();
            foreach(var finger in arm.Fingers) finger.Save();
            arm.Applied=true;
            if(!ArmMath.ApplyPose(arm.Upper.Bone,arm.Lower.Bone,arm.Palm.Bone,target,arm.PalmOffset,arm.Hint,adaptive,rigid,scale))
            { arm.Restore(true); return false; }
            if(fingers!=Fingers.Native)
                foreach(var finger in arm.Fingers)
                {
                    finger.Bone.Rotation=fingers==Fingers.Stick ? CockpitHandPose.StickRotation(finger.Bone.Name,trigger)
                        : CockpitHandPose.Rotation(finger.Bone.Name,fingers==Fingers.Pinch);
                    finger.Bone.ComputeAbsoluteTransform(true,true);
                }
            Remember(arm.Upper); Remember(arm.Lower); Remember(arm.Palm);
            foreach(var finger in arm.Fingers) Remember(finger);
            foreach(var twist in arm.Twists) Remember(twist);
            return true;
        }
        // Turns the head to a character-space orientation, limited to a natural range from the animated pose.
        internal static bool Look(SavedBone head,Matrix target,float limit)
        {
            if(head?.Bone.Parent==null || !target.IsValid() || Math.Abs(target.Forward.LengthSquared()-1)>.1f || Math.Abs(target.Up.LengthSquared()-1)>.1f) return false;
            head.Save();
            var rest=head.Bone.GetAbsoluteRigTransform().GetOrientation();
            var animated=Quaternion.CreateFromRotationMatrix(head.Bone.AbsoluteTransform);
            var desired=Quaternion.CreateFromRotationMatrix(rest*target.GetOrientation());
            float angle=2*(float)Math.Acos(MathHelper.Clamp(Math.Abs(Quaternion.Dot(animated,desired)),0,1));
            if(angle>limit) desired=Quaternion.Slerp(animated,desired,limit/angle);
            var absolute=Matrix.CreateFromQuaternion(desired); absolute.Translation=head.Bone.AbsoluteTransform.Translation;
            head.Bone.SetCompleteTransformFromAbsoluteMatrix(ref absolute,true);
            head.Bone.ComputeAbsoluteTransform(true,true);
            Remember(head);
            return true;
        }
        private static void Remember(SavedBone saved)
        { saved.Applied=saved.Bone.Rotation; saved.AppliedTranslation=saved.Bone.Translation; }
    }
}
