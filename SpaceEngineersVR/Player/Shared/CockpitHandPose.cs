using System.Collections.Generic;
using VRageMath;
using VRageRender.Animations;

namespace SpaceEngineersVR.Player
{
    internal static class CockpitHandPose
    {
        internal const float Tip=-.033f;
        internal static Matrix GripWrist(Matrix grip) => Matrix.CreateTranslation(0,.02f,.09f)*grip;
        internal static Quaternion Rotation(string name,bool pinch,bool stick=false)
        {
            bool thumb=name.Contains("Thumb"),index=name.Contains("Index");
            float curl=thumb ? pinch ? .25f : .35f : index && !stick ? pinch ? .7f : 0 : .85f;
            return Quaternion.CreateFromAxisAngle(Vector3.Backward,curl);
        }
        internal static Quaternion FreeRotation(string name,float[] curls)
        {
            int finger=name.Contains("Thumb") ? 0:name.Contains("Index") ? 1:name.Contains("Middle") ? 2:name.Contains("Ring") ? 3:4;
            float closed=finger==0 ? .45f:name.EndsWith("_2") ? 1.1f:name.EndsWith("_3") ? .85f:.8f;
            return Quaternion.CreateFromAxisAngle(Vector3.Backward,MathHelper.Lerp(.04f,closed,MathHelper.Clamp(curls[finger],0,1)));
        }
        internal static Quaternion StickRotation(string name,float trigger)
        {
            if(!name.Contains("Index")) return Quaternion.CreateFromAxisAngle(Vector3.Backward,name.Contains("Thumb") ? .35f:.95f);
            float rest,pressed;
            if(name.EndsWith("_1")) { rest=.35f; pressed=.65f; }
            else if(name.EndsWith("_2")) { rest=.55f; pressed=1.1f; }
            else { rest=.45f; pressed=.85f; }
            return Quaternion.CreateFromAxisAngle(Vector3.Backward,MathHelper.Lerp(rest,pressed,MathHelper.Clamp(trigger,0,1)));
        }
        internal static Matrix FingerPose(MyCharacterBone palm,MyCharacterBone tip,bool pinch)
        {
            var chain=new Stack<MyCharacterBone>();
            for(var bone=tip;bone!=null && bone!=palm;bone=bone.Parent) chain.Push(bone);
            Matrix pose=Matrix.Identity;
            foreach(var bone in chain)
            {
                Matrix local=bone.GetAbsoluteRigTransform()*Matrix.Invert(bone.Parent.GetAbsoluteRigTransform());
                pose=Matrix.CreateFromQuaternion(Rotation(bone.Name,pinch))*local*pose;
            }
            return pose;
        }
        internal static Vector3 Finger(MyCharacterBone palm,MyCharacterBone tip,bool pinch,float tipOffset=Tip) =>
            Vector3.Transform(new Vector3(tipOffset,0,0),FingerPose(palm,tip,pinch));
        internal static Vector3D PointContact(MatrixD wrist,Matrix palmOffset,Matrix finger) =>
            Vector3D.Transform(new Vector3D(Tip,0,0),(MatrixD)finger*palmOffset*wrist);
        internal static MatrixD PointPose(MatrixD wrist,Matrix palmOffset,Matrix finger)
        {
            MatrixD posed=(MatrixD)finger*palmOffset*wrist;
            return MatrixD.CreateWorld(Vector3D.Transform(new Vector3D(Tip,0,0),posed),-posed.Right,posed.Up);
        }
        internal static Vector3 Contact(MyCharacterBone palm,MyCharacterBone index,MyCharacterBone thumb,bool pinch,float tipOffset=Tip)
        {
            Vector3 point=Finger(palm,index,pinch,pinch ? -.025f:tipOffset);
            return pinch && thumb!=null ? (point+Finger(palm,thumb,true,-.025f))*.5f : point;
        }
        internal static MatrixD Blend(MatrixD free,MatrixD attached,float amount)
        {
            var rotation=Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(free),Quaternion.CreateFromRotationMatrix(attached),amount);
            var result=MatrixD.CreateFromQuaternion(rotation);
            result.Translation=Vector3D.Lerp(free.Translation,attached.Translation,amount);
            return result;
        }
        internal static MatrixD Attach(MatrixD wrist,Matrix palmOffset,Vector3 localContact,Vector3D target)
        {
            wrist.Translation+=target-Vector3D.Transform(localContact,(MatrixD)palmOffset*wrist);
            return wrist;
        }
    }
}
