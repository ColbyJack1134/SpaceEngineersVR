using System;
using System.IO;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Multiplayer
{
    internal sealed class PlayerPose
    {
        // Version 1 packets (85 bytes) carry hands only; the head pose is appended.
        internal const int LegacySize=85,HeadSize=113,Size=141,HeadTracked=4,ToolRayTracked=8,ItemSupported=16;
        internal long Character,Seat;
        internal uint Sequence;
        internal byte Tracked;
        internal Matrix Left,Right,Head=Matrix.Identity,ToolRay=Matrix.Identity;
        internal ArmSkeleton.Fingers LeftFingers,RightFingers;
        internal float LeftTrigger,RightTrigger;

        internal byte[] Encode()
        {
            using(var stream=new MemoryStream(Size))
            using(var writer=new BinaryWriter(stream))
            {
                writer.Write(0x31525653); writer.Write(Character); writer.Write(Seat); writer.Write(Sequence); writer.Write(Tracked);
                Write(writer,Left); Write(writer,Right);
                writer.Write((byte)LeftFingers); writer.Write((byte)RightFingers);
                writer.Write((byte)(MathHelper.Clamp(LeftTrigger,0,1)*255)); writer.Write((byte)(MathHelper.Clamp(RightTrigger,0,1)*255));
                Write(writer,Head);
                if((Tracked&ToolRayTracked)!=0) Write(writer,ToolRay);
                return stream.ToArray();
            }
        }
        internal static PlayerPose Decode(byte[] data)
        {
            if(data==null || data.Length!=Size && data.Length!=HeadSize && data.Length!=LegacySize) return null;
            using(var reader=new BinaryReader(new MemoryStream(data,false)))
            {
                if(reader.ReadInt32()!=0x31525653) return null;
                var value=new PlayerPose {Character=reader.ReadInt64(),Seat=reader.ReadInt64(),Sequence=reader.ReadUInt32(),Tracked=reader.ReadByte()};
                if(value.Character==0 || value.Tracked>31 || !Read(reader,out value.Left) || !Read(reader,out value.Right)) return null;
                value.LeftFingers=(ArmSkeleton.Fingers)reader.ReadByte(); value.RightFingers=(ArmSkeleton.Fingers)reader.ReadByte();
                value.LeftTrigger=reader.ReadByte()/255f; value.RightTrigger=reader.ReadByte()/255f;
                if(data.Length==LegacySize) value.Tracked&=3;
                else if(!Read(reader,out value.Head)) return null;
                if(data.Length<Size) value.Tracked&=23;
                else if(!Read(reader,out value.ToolRay)) return null;
                return value.LeftFingers>ArmSkeleton.Fingers.Stick || value.RightFingers>ArmSkeleton.Fingers.Stick ? null:value;
            }
        }
        private static void Write(BinaryWriter writer,Matrix matrix)
        {
            var q=Quaternion.CreateFromRotationMatrix(matrix);
            writer.Write(matrix.M41); writer.Write(matrix.M42); writer.Write(matrix.M43);
            writer.Write(q.X); writer.Write(q.Y); writer.Write(q.Z); writer.Write(q.W);
        }
        private static bool Read(BinaryReader reader,out Matrix matrix)
        {
            var p=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            var q=new Quaternion(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
            float length=q.LengthSquared(); matrix=Matrix.Identity;
            if(!p.IsValid() || p.LengthSquared()>25 || float.IsNaN(length) || length<.9f || length>1.1f) return false;
            q.Normalize(); matrix=Matrix.CreateFromQuaternion(q); matrix.Translation=p; return true;
        }
        internal static Matrix Blend(Matrix a,Matrix b,float amount)
        {
            var result=Matrix.CreateFromQuaternion(Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(a),Quaternion.CreateFromRotationMatrix(b),amount));
            result.Translation=Vector3.Lerp(a.Translation,b.Translation,amount); return result;
        }
    }

    internal sealed class PoseStream
    {
        private PlayerPose previous,current;
        private double received,interval;
        internal bool Push(PlayerPose pose,double now)
        {
            if(pose==null || current!=null && current.Character==pose.Character && now-received<1 && unchecked((int)(pose.Sequence-current.Sequence))<=0) return false;
            bool continuous=current!=null && current.Character==pose.Character && current.Seat==pose.Seat && now-received<.25;
            previous=continuous ? current:pose; current=pose;
            interval=continuous ? Math.Max(.025,Math.Min(.1,now-received)):.05; received=now; return true;
        }
        internal bool Sample(double now,out PlayerPose pose)
        {
            pose=current;
            if(pose==null || now-received>.5 || now<received) return false;
            return true;
        }
        internal Matrix Head(double now)
        {
            if((previous.Tracked&PlayerPose.HeadTracked)==0) return current.Head;
            return PlayerPose.Blend(previous.Head,current.Head,(float)MathHelper.Clamp((now-received)/interval,0,1));
        }
        internal Matrix ToolRay(double now)
        {
            if((previous.Tracked&PlayerPose.ToolRayTracked)==0) return current.ToolRay;
            return PlayerPose.Blend(previous.ToolRay,current.ToolRay,(float)MathHelper.Clamp((now-received)/interval,0,1));
        }
        internal Matrix Hand(bool left,double now)
        {
            float amount=(float)MathHelper.Clamp((now-received)/interval,0,1);
            return PlayerPose.Blend(left ? previous.Left:previous.Right,left ? current.Left:current.Right,amount);
        }
    }
}
