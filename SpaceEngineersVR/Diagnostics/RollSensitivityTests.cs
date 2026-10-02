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
            if(config.PhysicalShipControlsOnly) throw new Exception("Old config disables controller flight by default");
            if (config.TrackedArms) throw new Exception("Existing config option changed");
            foreach (bool ship in new[] { false,true })
            foreach (float horizontal in new[] { -1f,-0.7f,-0.2f,0,0.2f,0.7f,1f })
            foreach (bool modifier in new[] { false,true })
            {
                float sensitivity=ship ? config.ShipRollSensitivity : config.JetpackRollSensitivity;
                var stick=new Vector2(horizontal,0.6f);
                FlightAxes.Rotation(stick,modifier,ship,10,1,out var oldRotation,out var oldRoll);
                FlightAxes.Rotation(stick,modifier,ship,10,sensitivity,out var rotation,out var roll);
                FlightAxes.Rotation(stick,modifier,ship,10,sensitivity,out var inverted,out var invertedRoll,true);
                Near(inverted.X,-rotation.X,"Pitch inversion failed");
                Near(inverted.Y,rotation.Y,"Pitch inversion changed yaw");
                Near(invertedRoll,roll,"Pitch inversion changed roll");
                Near(rotation.X,oldRotation.X,"Roll setting changed pitch");
                Near(rotation.Y,oldRotation.Y,"Roll setting changed yaw ownership");
                Near(roll,oldRoll*(ship ? 0.6f : 0.25f),"Requested roll reduction");
                if (ship)
                {
                    float previousTorque=modifier ? MathHelper.Clamp(horizontal*10*0.2f,-1,1) : 0;
                    Near(MathHelper.Clamp(roll*0.2f,-1,1),previousTorque*0.6f,"Ship reduction lost at engine saturation");
                }
            }
            for(int legacy=0;legacy<4;legacy++)
            {
                PluginConfig migrated;
                using(var xml=new StringReader("<PluginConfig><HelmetHudMode>"+legacy+"</HelmetHudMode></PluginConfig>")) migrated=(PluginConfig)serializer.Deserialize(xml);
                if(migrated.ShowVitals!=(legacy>0) || migrated.WaypointMode!=Math.Max(0,legacy-1)) throw new Exception("Legacy HUD visibility changed");
                for(int press=1;press<=4;press++)
                {
                    migrated.CycleHud();
                    int expected=(legacy+press)%4;
                    if(migrated.ShowVitals!=(expected>0) || migrated.WaypointMode!=Math.Max(0,expected-1))
                        throw new Exception("Head gesture HUD cycle differs after legacy migration");
                    using(var xml=new StringWriter())
                    {
                        serializer.Serialize(xml,migrated);
                        using(var saved=new StringReader(xml.ToString())) migrated=(PluginConfig)serializer.Deserialize(saved);
                    }
                }
                migrated.ShowVitals=false; migrated.WaypointMode=2; migrated.HudWithVisorOpen=true;
                migrated.PhysicalShipControlsOnly=true;
                migrated.InvertShipPitch=true; migrated.InvertJetpackPitch=false; migrated.ThirdPersonRotationGlide=0;
                using(var xml=new StringWriter())
                {
                    serializer.Serialize(xml,migrated);
                    using(var saved=new StringReader(xml.ToString())) migrated=(PluginConfig)serializer.Deserialize(saved);
                }
                if(!migrated.PhysicalShipControlsOnly || migrated.ShowVitals || migrated.WaypointMode!=2 || !migrated.HudWithVisorOpen || !migrated.InvertShipPitch || migrated.InvertJetpackPitch || migrated.ThirdPersonRotationGlide!=0)
                    throw new Exception("Independent HUD/flight/glide settings lost after save");
                migrated.ThirdPersonZoomSensitivity=float.NaN; migrated.ThirdPersonPanGlide=float.PositiveInfinity;
                Near(migrated.ThirdPersonZoomSensitivity,1,"Invalid zoom sensitivity"); Near(migrated.ThirdPersonPanGlide,1,"Invalid pan glide");
            }
            log("PASS settings migration: all legacy HUD modes, independent vitals/waypoints/visor and ship/jetpack inversion, zero-glide persistence and invalid sensitivity fallback.");
            foreach(bool enabled in new[] {false,true})
            {
                if(!FlightAxes.ControllerInputAllowed(false,false,enabled) || !FlightAxes.ControllerInputAllowed(true,true,enabled))
                    throw new Exception("Physical-only setting suppresses jetpack or third-person flight");
                if(FlightAxes.ControllerInputAllowed(true,false,enabled)==enabled)
                    throw new Exception("First-person controller flight ignores physical-only setting");
            }
            log("PASS physical-only flight preference: legacy default off, enabled persistence, first-person ship gate and third-person/jetpack exceptions.");
            string changed=null;
            config.PropertyChanged+=(sender,args)=>changed=args.PropertyName;
            config.PhysicalShipControlsOnly=true;
            if(changed!=nameof(config.PhysicalShipControlsOnly)) throw new Exception("Physical-only setting cannot trigger automatic save");
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
