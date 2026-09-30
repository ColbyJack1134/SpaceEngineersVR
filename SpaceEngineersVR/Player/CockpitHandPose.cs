using System.Collections.Generic;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitHandPose
    {
        internal static Quaternion Rotation(string name,bool pinch,bool stick=false)
        {
            bool thumb=name.Contains("Thumb"),index=name.Contains("Index");
            float curl=thumb ? pinch ? .25f : .35f : index && !stick ? pinch ? .7f : 0 : .85f;
            return Quaternion.CreateFromAxisAngle(Vector3.Backward,curl);
        }
        internal static Vector3 Finger(MyCharacterBone palm,MyCharacterBone tip,bool pinch)
        {
            var chain=new Stack<MyCharacterBone>();
            for(var bone=tip;bone!=null && bone!=palm;bone=bone.Parent) chain.Push(bone);
            Matrix pose=Matrix.Identity;
            foreach(var bone in chain)
            {
                Matrix local=bone.GetAbsoluteRigTransform()*Matrix.Invert(bone.Parent.GetAbsoluteRigTransform());
                pose=Matrix.CreateFromQuaternion(Rotation(bone.Name,pinch))*local*pose;
            }
            return Vector3.Transform(new Vector3(-.025f,0,0),pose);
        }
        internal static Vector3 Contact(MyCharacterBone palm,MyCharacterBone index,MyCharacterBone thumb,bool pinch)
        {
            Vector3 point=Finger(palm,index,pinch);
            return pinch && thumb!=null ? (point+Finger(palm,thumb,true))*.5f : point;
        }
        internal static MatrixD Attach(MatrixD wrist,Matrix palmOffset,Vector3 localContact,Vector3D target)
        {
            wrist.Translation+=target-Vector3D.Transform(localContact,(MatrixD)palmOffset*wrist);
            return wrist;
        }
    }
}
