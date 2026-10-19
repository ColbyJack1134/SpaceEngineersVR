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
            public bool Saved;
            public void Save() { Original=Bone.Rotation; Translation=Bone.Translation; Saved=true; }
            // Two solves may touch one bone in a frame (lean then look); only the first sees the vanilla pose.
            public void SaveOnce() { if(!Saved) Save(); }
            public bool Restore(bool force)
            {
                Saved=false;
                // Do not overwrite a new pose that vanilla animation has already written.
                if(!force && (Bone.Rotation!=Applied || Bone.Translation!=AppliedTranslation)) return false;
                Bone.Rotation=Original; Bone.Translation=Translation; return true;
            }
        }
        internal sealed class Arm
        {
            public SavedBone Upper,Lower,Palm,Collar;
            public SavedBone[] Fingers,Twists;
            public MyCharacterBone[] Chain;
            public MyCharacterBone IndexTip,ThumbTip;
            public Matrix PalmOffset,PointFinger;
            public ArmSolve.Rig Rig;
            public readonly ArmSolve.State State=new ArmSolve.State();
            public bool Applied;
            public void Restore(bool force=false)
            {
                bool collar=Collar!=null && Collar.Applied!=default(Quaternion) && Collar.Restore(force);
                if(collar) Collar.Applied=default(Quaternion);
                if(!Applied) { if(collar) Collar.Bone.ComputeAbsoluteTransform(true,true); return; }
                Upper.Restore(force); Lower.Restore(force); Palm.Restore(force);
                foreach(var twist in Twists) twist.Restore(force);
                if(Fingers!=null) foreach(var finger in Fingers) finger.Restore(force);
                (collar ? Collar.Bone:Upper.Bone).ComputeAbsoluteTransform(true,true);
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
            var chain=new MyCharacterBone[twists.Count];
            for(int i=0;i<chain.Length;i++) chain[i]=twists[chain.Length-1-i].Bone;
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
                Fingers=fingers.ToArray(),Twists=twists.ToArray(),Chain=chain,
                IndexTip=find(prefix+"Index_3"),ThumbTip=find(prefix+"Thumb_3"),
                Upper=new SavedBone { Bone=upper },Lower=new SavedBone { Bone=lower },Palm=new SavedBone { Bone=palm },Collar=upper.Parent==null ? null:new SavedBone { Bone=upper.Parent },
                PalmOffset=ArmMath.PalmCorrection(palm.GetAbsoluteRigTransform(),lower.GetAbsoluteRigTransform(),side),
                Rig=ArmSolve.MeasureRig(upper,lower,palm,find(prefix+"Thumb_1"),find(prefix+"Little_1"),side) };
        }
        // Vanilla animates the collarbones for an item in the right hand; a left-handed hold swaps them as mirror images.
        internal static void MirrorCollars(Arm left,Arm right)
        {
            if(left.Collar==null || right.Collar==null) return;
            left.Collar.Save(); right.Collar.Save();
            var mirror=Mirror(right.Collar.Bone,left.Collar.Bone);
            Swap(left.Collar,right.Collar,mirror,Matrix.Invert(mirror)); Swap(right.Collar,left.Collar,Matrix.Invert(mirror),mirror);
            Remember(left.Collar); Remember(right.Collar);
            left.Collar.Bone.ComputeAbsoluteTransform(true,true); right.Collar.Bone.ComputeAbsoluteTransform(true,true);
        }
        private static void Swap(SavedBone target,SavedBone source,Matrix toTarget,Matrix fromTarget)
        {
            var rotation=fromTarget*Matrix.CreateFromQuaternion(source.Original)*toTarget;
            target.Bone.Rotation=Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(rotation));
            target.Bone.Translation=Vector3.TransformNormal(source.Translation,toTarget);
        }
        private static Matrix Mirror(MyCharacterBone right,MyCharacterBone left) =>
            right.GetAbsoluteRigTransform().GetOrientation()*Matrix.CreateScale(-1,1,1)*Matrix.Invert(left.GetAbsoluteRigTransform().GetOrientation());
        // Right palm to left palm coordinates, from the symmetric bind pose.
        internal static Matrix PalmMirror(Arm right,Arm left) => Mirror(right.Palm.Bone,left.Palm.Bone);
        // Character definitions name HeadDummy, an unskinned camera bone; the astronaut head mesh follows SE_RigHead.
        internal static MyCharacterBone Head(Func<string,MyCharacterBone> find,string definitionHead) => find("SE_RigHead") ?? find(definitionHead);
        private static bool Descends(MyCharacterBone child,MyCharacterBone parent)
        {
            for(var node=child.Parent;node!=null;node=node.Parent) if(node==parent) return true;
            return false;
        }
        internal static bool Apply(Arm arm,Matrix target,ArmSolve.Options options,float scale,Fingers fingers,float trigger,float[] curls=null,WeaponProfile item=null,bool support=false,float[] stickCurl=null)
        {
            if(arm==null || !target.IsValid()) return false;
            arm.Upper.Save(); arm.Lower.Save(); arm.Palm.Save();
            foreach(var twist in arm.Twists) twist.Save();
            foreach(var finger in arm.Fingers) finger.Save();
            arm.Applied=true;
            if(!ArmSolve.Apply(arm.Upper.Bone,arm.Lower.Bone,arm.Chain,arm.Palm.Bone,arm.Rig,target,arm.PalmOffset,options,scale,arm.State))
            { arm.Restore(true); return false; }
            if(fingers!=Fingers.Native)
                foreach(var finger in arm.Fingers)
                {
                    finger.Bone.Rotation=item!=null && fingers==Fingers.Stick ? item.FingerRotation(finger.Bone.Name,trigger,support,finger.Original):curls!=null ? CockpitHandPose.FreeRotation(finger.Bone.Name,curls):fingers==Fingers.Stick ? CockpitHandPose.StickRotation(finger.Bone.Name,trigger,stickCurl)
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
            head.SaveOnce();
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
        internal const float HeadLimit=1.4f;
        // Keeps the camera dummy below the head in place, so views anchored to it do not turn with the head.
        internal static bool Look(SavedBone head,SavedBone eye,Matrix target,float limit)
        {
            var anchored=eye?.Bone.AbsoluteTransform;
            if(!Look(head,target,limit)) return false;
            if(anchored.HasValue)
            {
                var absolute=anchored.Value; eye.SaveOnce();
                eye.Bone.SetCompleteTransformFromAbsoluteMatrix(ref absolute,false);
                eye.Bone.ComputeAbsoluteTransform(true,true);
                Remember(eye);
            }
            return true;
        }
        internal static SavedBone Below(MyCharacterBone head,MyCharacterBone bone) => head!=null && bone!=null && Descends(bone,head) ? new SavedBone {Bone=bone}:null;
        internal static void Remember(SavedBone saved)
        { saved.Applied=saved.Bone.Rotation; saved.AppliedTranslation=saved.Bone.Translation; }
    }
}
