using System;
using System.IO;
using System.Xml.Serialization;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class RollSensitivityTests
    {
        private static void Near(float actual,float expected,string name)
        {
            if (float.IsNaN(actual) || Math.Abs(actual-expected)>0.00001f) throw new Exception(name+": "+actual+" != "+expected);
        }
        public static void Run(Action<string> log)
        {
            var serializer=new XmlSerializer(typeof(PluginConfig));
            PluginConfig config;
            using (var xml=new StringReader("<PluginConfig><PlayerHeight>1.81</PlayerHeight><TrackedArms>false</TrackedArms></PluginConfig>"))
                config=(PluginConfig)serializer.Deserialize(xml);
            Near(config.JetpackRollSensitivity,0.25f,"Old config jetpack default");
            Near(config.ShipRollSensitivity,0.6f,"Old config ship default");
            Near(config.PlayerHeight,1.81f,"Existing calibration retained");
            if (config.TrackedArms) throw new Exception("Existing config option changed");
            foreach (bool ship in new[] { false,true })
            foreach (float horizontal in new[] { -1f,-0.7f,-0.2f,0,0.2f,0.7f,1f })
            foreach (bool modifier in new[] { false,true })
            {
                float sensitivity=ship ? config.ShipRollSensitivity : config.JetpackRollSensitivity;
                var stick=new Vector2(horizontal,0.6f);
                FlightAxes.Rotation(stick,modifier,ship,10,1,out var oldRotation,out var oldRoll);
                FlightAxes.Rotation(stick,modifier,ship,10,sensitivity,out var rotation,out var roll);
                Near(rotation.X,oldRotation.X,"Roll setting changed pitch");
                Near(rotation.Y,oldRotation.Y,"Roll setting changed yaw ownership");
                Near(roll,oldRoll*(ship ? 0.6f : 0.25f),"Requested roll reduction");
                if (ship)
                {
                    float previousTorque=modifier ? MathHelper.Clamp(horizontal*10*0.2f,-1,1) : 0;
                    Near(MathHelper.Clamp(roll*0.2f,-1,1),previousTorque*0.6f,"Ship reduction lost at engine saturation");
                }
            }
            string changed=null;
            config.PropertyChanged+=(sender,args)=>changed=args.PropertyName;
            config.JetpackRollSensitivity=0.4f;
            if (changed!=nameof(config.JetpackRollSensitivity)) throw new Exception("Jetpack setting cannot trigger automatic save");
            config.ShipRollSensitivity=0.85f;
            if (changed!=nameof(config.ShipRollSensitivity)) throw new Exception("Ship setting cannot trigger automatic save");
            using (var xml=new StringWriter())
            {
                serializer.Serialize(xml,config);
                using (var saved=new StringReader(xml.ToString())) config=(PluginConfig)serializer.Deserialize(saved);
            }
            Near(config.JetpackRollSensitivity,0.4f,"Saved jetpack value");
            Near(config.ShipRollSensitivity,0.85f,"Saved ship value");
            FlightAxes.Rotation(new Vector2(1,0),true,false,10,config.JetpackRollSensitivity,out _,out var tunedRoll);
            Near(tunedRoll,4,"Live custom sensitivity");
            foreach (float invalid in new[] { float.NaN,float.PositiveInfinity,float.NegativeInfinity })
            {
                config.JetpackRollSensitivity=invalid; config.ShipRollSensitivity=invalid;
                Near(config.JetpackRollSensitivity,0.25f,"Invalid jetpack fallback");
                Near(config.ShipRollSensitivity,0.6f,"Invalid ship fallback");
            }
            config.JetpackRollSensitivity=-1; config.ShipRollSensitivity=10;
            Near(config.JetpackRollSensitivity,0.05f,"Lower roll bound");
            Near(config.ShipRollSensitivity,2,"Upper roll bound");
            log("PASS roll tuning: 25% jetpack / 60% ship including native torque saturation, both directions and partial input, unchanged pitch/yaw, old-config defaults, save notifications/round-trip and bounded values");
        }
    }
}
