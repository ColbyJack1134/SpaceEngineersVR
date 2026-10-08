using System;
using HarmonyLib;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class StandingMeasurementTests
    {
        private static void Require(bool value,string reason) { if(!value) throw new Exception(reason); }
        internal static void Run(Action<string> log)
        {
            var measurement=new StandingMeasurement();
            measurement.Sample(float.NaN); measurement.Sample(float.PositiveInfinity); measurement.Sample(0);
            for(int i=0;i<119;i++) measurement.Sample(1.837f);
            Require(!measurement.TryGet(out _),"Sparse tracking accepted for standing measurement");
            for(int i=0;i<180;i++) measurement.Sample(1.837f+(i%3-1)*.002f);
            measurement.Sample(2.2f);
            Require(measurement.TryGet(out float height) && Math.Abs(height-1.837f)<.001f,"Single tracking spike inflated standing reference");
            measurement.Clear();
            for(int i=0;i<300;i++) measurement.Sample(i%2==0 ? 1.7f:1.84f);
            Require(!measurement.TryGet(out _),"Moving/crouching window accepted as standing measurement");

            var configField=AccessTools.Field(typeof(Common),"<Config>k__BackingField");
            var original=Common.Config;
            var config=new PluginConfig {PlayerHeight=1.905f,MeasuredEyeHeight=1.837f};
            configField.SetValue(null,config);
            try
            {
                BodyFit.SetHeight(1.91f);
                Require(config.MeasuredEyeHeight==1.837f,"Actual height edit erased measured headset reference");
                BodyFit.SetHeight(1.905f);
                var samples=new StandingMeasurement();
                for(int i=0;i<299;i++) samples.Sample(1.837f);
                samples.Sample(2.2f);
                Require(samples.TryApply(config,true,out _),"Stable headset measurement failed to apply");
                Require(config.PlayerHeight==1.905f && Math.Abs(config.MeasuredEyeHeight-1.837f)<.001f,"Headset measurement overwrote actual stature or retained spike");
                Require(!samples.TryApply(config,false,out _),"Lost tracking applied headset calibration");
                Require(samples.TryApply(config,true,out _,true) && Math.Abs(config.PlayerHeight-1.837f*1.8f/1.69f)<.001f,"Onboarding failed to estimate body height from standing eye height");
                config.PlayerHeight=1.905f;
                samples.Clear();
                Require(!samples.TryApply(config,true,out _),"Empty measurement applied headset calibration");
                Require(config.PlayerHeight==1.905f && Math.Abs(config.MeasuredEyeHeight-1.837f)<.001f,"Failed measurement changed saved calibration");
            }
            finally
            {
                configField.SetValue(null,original);
            }
            log("PASS standing measurement: sparse/invalid tracking, stable median, outlier and motion rejection, actual-height preservation and failed-measurement retention.");
        }
    }
}
