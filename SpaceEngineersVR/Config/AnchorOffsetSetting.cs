using System;
using System.Xml.Serialization;
using VRageMath;

namespace SpaceEngineersVR.Config
{
    public sealed class AnchorOffsetSetting
    {
        public string Key { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Pitch { get; set; }
        public float Yaw { get; set; }
        public float Roll { get; set; }
        public float Scale { get; set; }=1;

        [XmlIgnore]
        public Matrix Matrix
        {
            get
            {
                var result=Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(Yaw),MathHelper.ToRadians(Pitch),MathHelper.ToRadians(Roll));
                result.Translation=new Vector3(X,Y,Z);
                return result;
            }
        }
        public static AnchorOffsetSetting FromRotation(string key,Matrix rotation)
        {
            // Inverse of Quaternion.CreateFromYawPitchRoll, which Matrix uses above.
            var q=Quaternion.CreateFromRotationMatrix(rotation);
            double yaw=Math.Atan2(2*(q.W*q.Y+q.X*q.Z),1-2*(q.X*q.X+q.Y*q.Y));
            double pitch=Math.Asin(Math.Max(-1,Math.Min(1,2*(q.W*q.X-q.Y*q.Z))));
            double roll=Math.Atan2(2*(q.W*q.Z+q.X*q.Y),1-2*(q.X*q.X+q.Z*q.Z));
            return new AnchorOffsetSetting { Key=key,Yaw=(float)(yaw*180/Math.PI),Pitch=(float)(pitch*180/Math.PI),Roll=(float)(roll*180/Math.PI) };
        }
        public AnchorOffsetSetting Copy() => (AnchorOffsetSetting)MemberwiseClone();
        public bool Valid => !string.IsNullOrEmpty(Key) && new Vector3(X,Y,Z).IsValid() &&
            new Vector3(Pitch,Yaw,Roll).IsValid() && Math.Abs(X)<=1 && Math.Abs(Y)<=1 && Math.Abs(Z)<=1 &&
            Math.Abs(Pitch)<=180 && Math.Abs(Yaw)<=180 && Math.Abs(Roll)<=180 && !float.IsNaN(Scale) && Scale>=.5f && Scale<=2;
    }
}
