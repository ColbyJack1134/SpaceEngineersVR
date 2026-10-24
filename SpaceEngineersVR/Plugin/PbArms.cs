using System;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Character;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Plugin
{
    // Shoulder and elbow estimates for programmable block arms: torso square to the seat under the head joint, elbow from the avatar arm solve.
    internal static class PbArms
    {
        internal sealed class Arm
        {
            public ArmSolve.Rig Rig;
            public Matrix PalmOffset;
            // Bind pose, unscaled: shoulder relative to the head joint, segment lengths.
            public Vector3 Shoulder;
            public float Upper,Lower;
            public readonly ArmSolve.State State=new ArmSolve.State();
        }

        private static MyCharacterDefinition measured;
        private static Arm left,right;

        internal static void Reset() { measured=null; left=right=null; }

        // side: -1 left, 1 right.
        internal static bool TrySolve(MyCharacter character,Matrix head,Matrix wrist,float side,out Vector3 shoulder,out Vector3 elbow)
        {
            shoulder=elbow=Vector3.Zero;
            var definition=character?.Definition;
            if(!head.IsValid() || !wrist.IsValid() || definition==null || character.AnimationController?.CharacterBones==null) return false;
            if(definition!=measured) { measured=definition; Measure(character); }
            var arm=side<0 ? left:right;
            if(arm==null) return false;
            // The feed estimates the player's own arm, so it scales with body calibration while the seated avatar (BodyFit.ScaleFor) does not.
            float scale=BodyFit.Enabled ? BodyFit.Scale(Common.Config.PlayerHeight,definition.CharacterCollisionHeight) : 1;
            Solve(arm,head,wrist,scale,out shoulder,out elbow);
            return true;
        }

        internal static void Solve(Arm arm,Matrix head,Matrix wrist,float scale,out Vector3 shoulder,out Vector3 elbow)
        {
            shoulder=BodyPlacement.HeadJoint(head)+arm.Shoulder*scale;
            Vector3 delta=wrist.Translation-shoulder;
            float distance=Math.Max(delta.Length(),1e-4f);
            float upper=arm.Upper*scale,lower=arm.Lower*scale;
            elbow=ArmSolve.Elbow(shoulder,wrist.Translation,delta/distance,distance,upper,lower,upper+lower,
                arm.PalmOffset*wrist.GetOrientation(),arm.Rig,0,arm.State).Elbow;
        }

        private static void Measure(MyCharacter character)
        {
            var definition=character.Definition;
            Func<string,VRageRender.Animations.MyCharacterBone> find=name => character.AnimationController.FindBone(name,out _);
            var pivot=ArmSkeleton.Head(find,definition.HeadBone);
            left=right=null;
            if(pivot==null) return;
            Vector3 head=pivot.GetAbsoluteRigTransform().Translation;
            left=Measure(ArmSkeleton.Find(find,definition.LeftHandIKStartBone,definition.LeftForearmBone,definition.LeftHandIKEndBone,-1),head);
            right=Measure(ArmSkeleton.Find(find,definition.RightHandIKStartBone,definition.RightForearmBone,definition.RightHandIKEndBone,1),head);
        }
        internal static Arm Measure(ArmSkeleton.Arm skeleton,Vector3 head)
        {
            if(skeleton==null) return null;
            Vector3 upper=skeleton.Upper.Bone.GetAbsoluteRigTransform().Translation,lower=skeleton.Lower.Bone.GetAbsoluteRigTransform().Translation;
            var arm=new Arm {
                Rig=skeleton.Rig,PalmOffset=skeleton.PalmOffset,Shoulder=upper-head,
                Upper=Vector3.Distance(upper,lower),Lower=Vector3.Distance(lower,skeleton.Palm.Bone.GetAbsoluteRigTransform().Translation) };
            return arm.Upper>.05f && arm.Lower>.05f ? arm : null;
        }
    }
}
