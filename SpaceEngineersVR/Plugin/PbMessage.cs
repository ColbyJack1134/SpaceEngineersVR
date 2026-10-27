using System.Globalization;
using System.Text;
using VRageMath;

namespace SpaceEngineersVR.Plugin
{
    // Programmable block argument, one line of space-separated fields: tag, sequence, controller entity id, flags, head pose, left hand, right hand,
    // left joints, right joints.
    // Pose: position xyz, rotation quaternion xyzw. Hand: pose, trigger, grip, stick xy, five finger curls (thumb first).
    // Joints: estimated shoulder, elbow and wrist positions xyz.
    // Poses are relative to the recentered seated eye, in block axes: x right, y up, z backward.
    // Fields never move; new fields are appended, so parsers accept longer messages.
    internal sealed class PbMessage
    {
        internal const string Tag="SEVR1";
        internal const int Fields=61;
        internal const int Arms=1,Head=2,Left=4,Right=8,Stopped=16,LeftJoints=32,RightJoints=64,Lean=128;

        internal sealed class Hand
        {
            internal Matrix Pose=Matrix.Identity;
            internal float Trigger,Grip;
            internal Vector2 Stick;
            internal readonly float[] Curls=new float[5];
        }
        internal sealed class Joints { internal Vector3 Shoulder,Elbow,Wrist; }

        internal long Controller;
        internal int Flags;
        internal Matrix HeadPose=Matrix.Identity;
        internal readonly Hand LeftHand=new Hand(),RightHand=new Hand();
        internal readonly Joints LeftArm=new Joints(),RightArm=new Joints();

        // Everything after the sequence number, so unchanged frames can be skipped.
        internal string Body()
        {
            var text=new StringBuilder(480);
            text.Append(Controller.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Flags.ToString(CultureInfo.InvariantCulture));
            Write(text,HeadPose);
            Write(text,LeftHand); Write(text,RightHand);
            Write(text,LeftArm); Write(text,RightArm);
            return text.ToString();
        }
        internal static string Format(uint sequence,string body) => Tag+" "+sequence.ToString(CultureInfo.InvariantCulture)+" "+body;
        private static void Write(StringBuilder text,Hand hand)
        {
            Write(text,hand.Pose);
            Number(text,hand.Trigger,"0.###"); Number(text,hand.Grip,"0.###");
            Number(text,hand.Stick.X,"0.###"); Number(text,hand.Stick.Y,"0.###");
            foreach(float curl in hand.Curls) Number(text,curl,"0.###");
        }
        private static void Write(StringBuilder text,Joints arm)
        {
            Write(text,arm.Shoulder); Write(text,arm.Elbow); Write(text,arm.Wrist);
        }
        private static void Write(StringBuilder text,Vector3 point)
        {
            Number(text,point.X,"0.####"); Number(text,point.Y,"0.####"); Number(text,point.Z,"0.####");
        }
        private static void Write(StringBuilder text,Matrix pose)
        {
            var rotation=Quaternion.CreateFromRotationMatrix(pose);
            rotation.Normalize();
            Write(text,pose.Translation);
            Number(text,rotation.X,"0.####"); Number(text,rotation.Y,"0.####"); Number(text,rotation.Z,"0.####"); Number(text,rotation.W,"0.####");
        }
        private static void Number(StringBuilder text,float value,string format)
        {
            if(float.IsNaN(value) || float.IsInfinity(value)) value=0;
            string number=value.ToString(format,CultureInfo.InvariantCulture);
            text.Append(' ').Append(number=="-0" ? "0":number);
        }
    }
}
