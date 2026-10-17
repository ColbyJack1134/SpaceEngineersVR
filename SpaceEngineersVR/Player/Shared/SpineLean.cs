using System;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    // Bends the seated upper body toward the headset. Vanilla forces its own lean to zero in cockpits and only bends for pitch.
    internal static class SpineLean
    {
        internal sealed class Chain
        {
            public ArmSkeleton.SavedBone[] Joints;
            public MyCharacterBone Head;
            public ArmSkeleton.SavedBone Eye;
            public bool Applied;
        }
        internal static readonly string[] JointNames={"SE_RigSpine1","SE_RigSpine2","SE_RigSpine3","SE_RigSpine4","SE_RigNeck"};
        // Character space metres: side, down and up, forward (-Z) and back.
        internal static readonly Vector3 Min=new Vector3(-.2f,-.15f,-.3f),Max=new Vector3(.2f,.1f,.08f);
        internal const float TwistShare=.35f,TwistLimit=.5f,TwistFade=1.75f;

        internal static Chain Find(Func<string,MyCharacterBone> find,MyCharacterBone head,ArmSkeleton.SavedBone eye)
        {
            if(head==null) return null;
            var joints=new ArmSkeleton.SavedBone[JointNames.Length];
            for(int i=0;i<joints.Length;i++)
            {
                var bone=find(JointNames[i]);
                if(bone==null || !Ancestor(bone,head)) return null;
                joints[i]=new ArmSkeleton.SavedBone {Bone=bone};
            }
            return new Chain {Joints=joints,Head=head,Eye=eye};
        }
        private static bool Ancestor(MyCharacterBone bone,MyCharacterBone descendant)
        {
            for(var node=descendant.Parent;node!=null;node=node.Parent) if(node==bone) return true;
            return false;
        }
        internal static Vector3 Limit(Vector3 delta) => delta.IsValid() ? Vector3.Clamp(delta,Min,Max) : Vector3.Zero;
        // Torso share of the head yaw; fades out past the limit so a look behind does not flip the torso.
        internal static float Twist(Vector3 forward)
        {
            if(!forward.IsValid() || forward.X*forward.X+forward.Z*forward.Z<1e-6f) return 0;
            float yaw=(float)Math.Atan2(forward.X,-forward.Z);
            float fade=MathHelper.Clamp((MathHelper.Pi-Math.Abs(yaw))/(MathHelper.Pi-TwistFade),0,1);
            return MathHelper.Clamp(yaw*TwistShare*fade,-TwistLimit,TwistLimit);
        }
        // delta moves the camera dummy in character space; the dummy itself is pinned afterwards so seat cameras anchored to it stay put.
        internal static bool Apply(Chain chain,Vector3 delta,float twist)
        {
            if(chain==null || !delta.IsValid() || float.IsNaN(twist) || float.IsInfinity(twist)) return false;
            var effector=chain.Eye?.Bone ?? chain.Head;
            Matrix eye=effector.AbsoluteTransform;
            foreach(var joint in chain.Joints) joint.SaveOnce();
            chain.Eye?.SaveOnce();
            chain.Applied=true;
            Vector3 pivot=chain.Joints[0].Bone.AbsoluteTransform.Translation;
            Vector3 target=eye.Translation+Limit(delta);
            var twistStep=Quaternion.CreateFromAxisAngle(Vector3.Up,-MathHelper.Clamp(twist,-TwistLimit,TwistLimit)/chain.Joints.Length);
            for(int pass=0;pass<3;pass++)
            {
                Vector3 from=effector.AbsoluteTransform.Translation-pivot,to=target-pivot;
                if(from.LengthSquared()<1e-6f || to.LengthSquared()<1e-6f) break;
                var share=Quaternion.Slerp(Quaternion.Identity,Between(Vector3.Normalize(from),Vector3.Normalize(to)),1f/chain.Joints.Length);
                foreach(var joint in chain.Joints) Rotate(joint.Bone,pass==0 ? twistStep*share:share);
            }
            if(chain.Eye!=null) { chain.Eye.Bone.SetCompleteTransformFromAbsoluteMatrix(ref eye,false); chain.Eye.Bone.ComputeAbsoluteTransform(true,true); }
            foreach(var joint in chain.Joints) ArmSkeleton.Remember(joint);
            if(chain.Eye!=null) ArmSkeleton.Remember(chain.Eye);
            return true;
        }
        // Rotation in character space about the bone's own position.
        private static void Rotate(MyCharacterBone bone,Quaternion rotation)
        {
            var absolute=bone.AbsoluteTransform;
            var turned=absolute.GetOrientation()*Matrix.CreateFromQuaternion(rotation);
            turned.Translation=absolute.Translation;
            bone.SetCompleteTransformFromAbsoluteMatrix(ref turned,true);
            bone.ComputeAbsoluteTransform(true,true);
        }
        internal static Quaternion Between(Vector3 from,Vector3 to)
        {
            float dot=MathHelper.Clamp(Vector3.Dot(from,to),-1,1);
            var axis=Vector3.Cross(from,to);
            if(axis.LengthSquared()<1e-10f) return Quaternion.Identity;
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis),(float)Math.Acos(dot));
        }
        internal static void Restore(Chain chain,bool force=false)
        {
            if(chain==null || !chain.Applied) return;
            bool restored=false;
            foreach(var joint in chain.Joints) restored|=joint.Restore(force);
            if(chain.Eye!=null) restored|=chain.Eye.Restore(force);
            if(restored) chain.Joints[0].Bone.ComputeAbsoluteTransform(true,true);
            chain.Applied=false;
        }
    }
}
