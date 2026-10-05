using System;
using System.IO;

namespace SpaceEngineersVR.Multiplayer
{
    internal sealed class FlightTuning
    {
        internal float Rotation=1,Translation=1,RotationCurve=2,TranslationCurve=2,TiltDeadzone=.08f,TwistDeadzone=.08f,Smoothing=.05f;
        internal FlightTuning Copy() => (FlightTuning)MemberwiseClone();
        private static bool Within(float value,float min,float max) => !float.IsNaN(value) && value>=min && value<=max;
        internal bool Valid => Within(Rotation,.25f,2) && Within(Translation,.25f,2) &&
            (RotationCurve==1 || RotationCurve==2) && (TranslationCurve==1 || TranslationCurve==2) &&
            Within(TiltDeadzone,0,.35f) && Within(TwistDeadzone,0,.35f) && Within(Smoothing,0,.1f);
        internal void Write(BinaryWriter writer)
        {
            if(!Valid) throw new InvalidDataException("Invalid flight settings.");
            foreach(float v in new[] {Rotation,Translation,RotationCurve,TranslationCurve,TiltDeadzone,TwistDeadzone,Smoothing}) writer.Write(v);
        }
        internal static FlightTuning Read(BinaryReader reader)
        {
            var value=new FlightTuning {Rotation=reader.ReadSingle(),Translation=reader.ReadSingle(),RotationCurve=reader.ReadSingle(),
                TranslationCurve=reader.ReadSingle(),TiltDeadzone=reader.ReadSingle(),TwistDeadzone=reader.ReadSingle(),Smoothing=reader.ReadSingle()};
            if(!value.Valid) throw new InvalidDataException("Invalid flight settings.");
            return value;
        }
        internal string Encode()
        {
            using(var stream=new MemoryStream()) using(var writer=new BinaryWriter(stream)) { Write(writer); return Convert.ToBase64String(stream.ToArray()); }
        }
        internal static FlightTuning Decode(string text)
        {
            var bytes=Convert.FromBase64String(text);
            if(bytes.Length!=28) throw new InvalidDataException("Invalid flight settings length.");
            using(var reader=new BinaryReader(new MemoryStream(bytes))) return Read(reader);
        }
    }
}
