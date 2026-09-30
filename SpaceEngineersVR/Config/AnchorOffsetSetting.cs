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
        public AnchorOffsetSetting Copy() => (AnchorOffsetSetting)MemberwiseClone();
        public bool Valid => !string.IsNullOrEmpty(Key) && new Vector3(X,Y,Z).IsValid() &&
            new Vector3(Pitch,Yaw,Roll).IsValid() && Math.Abs(X)<=1 && Math.Abs(Y)<=1 && Math.Abs(Z)<=1 &&
            Math.Abs(Pitch)<=180 && Math.Abs(Yaw)<=180 && Math.Abs(Roll)<=180 && !float.IsNaN(Scale) && Scale>=.5f && Scale<=2;
    }
}
