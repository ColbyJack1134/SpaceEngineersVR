using System;
using System.IO;

namespace SpaceEngineersVR.Multiplayer
{
    internal sealed class FlightTuning
    {
        internal float Rotation=1,Translation=1,RotationCurve=2,TranslationCurve=2,TiltDeadzone=.08f,TwistDeadzone=.08f,Smoothing=.05f;
        internal bool FlightMode,BarTiltEnabled,WheelMotion;
        internal float ThrottleSensitivity=1,PitchSensitivity=1,RollSensitivity=1,BarPitchTravel=.05f,BarPitchDeadzone=.005f,
            BarRollTravel=(float)(Math.PI/18),BarRollDeadzone=(float)(Math.PI/90);
        internal float WheelPitchTravel=.04f,WheelPitchDeadzone=.005f,WheelRollTravel=(float)(Math.PI/6);
        internal FlightTuning Copy() => (FlightTuning)MemberwiseClone();
        internal bool Same(FlightTuning other) => other!=null && Rotation==other.Rotation && Translation==other.Translation &&
            RotationCurve==other.RotationCurve && TranslationCurve==other.TranslationCurve && TiltDeadzone==other.TiltDeadzone &&
            TwistDeadzone==other.TwistDeadzone && Smoothing==other.Smoothing && FlightMode==other.FlightMode && BarTiltEnabled==other.BarTiltEnabled &&
            ThrottleSensitivity==other.ThrottleSensitivity && PitchSensitivity==other.PitchSensitivity && RollSensitivity==other.RollSensitivity &&
            BarPitchTravel==other.BarPitchTravel && BarPitchDeadzone==other.BarPitchDeadzone &&
            BarRollTravel==other.BarRollTravel && BarRollDeadzone==other.BarRollDeadzone && WheelMotion==other.WheelMotion &&
            WheelPitchTravel==other.WheelPitchTravel && WheelPitchDeadzone==other.WheelPitchDeadzone && WheelRollTravel==other.WheelRollTravel;
        private static bool Within(float value,float min,float max) => !float.IsNaN(value) && value>=min && value<=max;
        internal bool Valid => Within(Rotation,.25f,2) && Within(Translation,.25f,2) &&
            (RotationCurve==1 || RotationCurve==2) && (TranslationCurve==1 || TranslationCurve==2) &&
            Within(TiltDeadzone,0,.35f) && Within(TwistDeadzone,0,.35f) && Within(Smoothing,0,.1f) &&
            Within(ThrottleSensitivity,.25f,2) && Within(PitchSensitivity,.25f,2) && Within(RollSensitivity,.25f,2) &&
            Within(BarPitchTravel,.02f,.05f) && Within(BarPitchDeadzone,0,.02f) && BarPitchDeadzone<BarPitchTravel &&
            Within(BarRollTravel,(float)(Math.PI/36),(float)(Math.PI/18)) &&
            Within(BarRollDeadzone,0,(float)(Math.PI/36)) && BarRollDeadzone<BarRollTravel &&
            Within(WheelPitchTravel,.02f,.06f) && Within(WheelPitchDeadzone,0,.015f) &&
            Within(WheelRollTravel,(float)(Math.PI/12),(float)(Math.PI/3));
        internal void Write(BinaryWriter writer)
        {
            if(!Valid) throw new InvalidDataException("Invalid flight settings.");
            foreach(float v in new[] {Rotation,Translation,RotationCurve,TranslationCurve,TiltDeadzone,TwistDeadzone,Smoothing}) writer.Write(v);
            writer.Write(FlightMode); writer.Write(BarTiltEnabled);
            foreach(float v in new[] {ThrottleSensitivity,PitchSensitivity,RollSensitivity,BarPitchTravel,BarPitchDeadzone,BarRollTravel,BarRollDeadzone}) writer.Write(v);
            writer.Write(WheelMotion); writer.Write(WheelPitchTravel); writer.Write(WheelPitchDeadzone); writer.Write(WheelRollTravel);
        }
        internal static FlightTuning Read(BinaryReader reader,int version=6)
        {
            var value=new FlightTuning {Rotation=reader.ReadSingle(),Translation=reader.ReadSingle(),RotationCurve=reader.ReadSingle(),
                TranslationCurve=reader.ReadSingle(),TiltDeadzone=reader.ReadSingle(),TwistDeadzone=reader.ReadSingle(),Smoothing=reader.ReadSingle()};
            if(version>=4)
            {
                value.FlightMode=reader.ReadBoolean(); bool enabled=reader.ReadBoolean();
                value.ThrottleSensitivity=reader.ReadSingle(); value.PitchSensitivity=reader.ReadSingle(); value.RollSensitivity=reader.ReadSingle();
                float travel=reader.ReadSingle(),deadzone=reader.ReadSingle();
                if(version==4)
                {
                    // The former body-lean opt-in must not activate a different hand gesture.
                    if(!Within(travel,.05f,.3f) || !Within(deadzone,0,.05f) || deadzone>=travel)
                        throw new InvalidDataException("Invalid legacy flight settings.");
                }
                else
                {
                    value.BarTiltEnabled=enabled; value.BarPitchTravel=travel; value.BarPitchDeadzone=deadzone;
                    value.BarRollTravel=reader.ReadSingle(); value.BarRollDeadzone=reader.ReadSingle();
                }
            }
            if(version>=6)
            {
                value.WheelMotion=reader.ReadBoolean(); value.WheelPitchTravel=reader.ReadSingle();
                value.WheelPitchDeadzone=reader.ReadSingle(); value.WheelRollTravel=reader.ReadSingle();
            }
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
            if(bytes.Length!=28 && bytes.Length!=50 && bytes.Length!=58 && bytes.Length!=71) throw new InvalidDataException("Invalid flight settings length.");
            using(var reader=new BinaryReader(new MemoryStream(bytes))) return Read(reader,bytes.Length==71 ? 6:bytes.Length==58 ? 5:bytes.Length==50 ? 4:3);
        }
    }
}
