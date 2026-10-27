#if SEVR_DIAGNOSTICS
using System;
using System.IO;
using System.Xml.Serialization;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.GUI;
using VRageMath;

namespace SpaceEngineersVR.Diagnostics
{
    internal static class SettingsUndoTests
    {
        // Created without its constructor, which needs the GUI renderer for the close button.
        private sealed class Screen : MyGuiScreenBase
        {
            internal int Rebuilt;
            private Screen() : base(new Vector2(.5f)) { }
            public override string GetFriendlyName() => "Undo test";
            public override void RecreateControls(bool constructor) => Rebuilt++;
        }

        public static void Run(Action<string> log)
        {
            PluginConfig config;
            using(var xml=new StringReader("<PluginConfig />")) config=(PluginConfig)new XmlSerializer(typeof(PluginConfig)).Deserialize(xml);
            config.InitializeHudProfiles();
            var screen=(Screen)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Screen));
            var undo=new SettingsUndo(screen,config);
            if(undo.Pending) throw new Exception("Undo enabled before any change");

            float haptics=config.HapticStrength;
            config.HapticStrength=.5f;
            if(!undo.Pending) throw new Exception("Undo disabled after a slider change");
            undo.Restore();
            if(config.HapticStrength!=haptics || undo.Pending) throw new Exception("Undo did not restore a slider value");
            if(screen.Rebuilt!=1) throw new Exception("Undo did not rebuild the screen");

            int states=config.HudProfiles.Length,index=config.HudProfileIndex;
            bool vitals=config.ShowVitals;
            config.AddHudProfile(); config.SelectHudProfile(4); config.ShowVitals=!config.ShowVitals;
            undo.Restore();
            if(config.HudProfiles.Length!=states || config.HudProfileIndex!=index || config.ShowVitals!=vitals)
                throw new Exception("Undo after adding a HUD state left "+config.HudProfiles.Length+" states, index "+config.HudProfileIndex);
            config.ShowVitals=!vitals;
            if(config.HudProfiles[index].Vitals==vitals) throw new Exception("Active HUD state stopped saving edits after undo");
            undo.Restore();

            bool seated=config.SeatedPlay;
            config.SeatedPlay=!seated; config.BodyCalibrated=!config.BodyCalibrated;
            undo.Restore();
            if(config.SeatedPlay!=seated) throw new Exception("Undo did not restore seated play");
            log("PASS settings undo: slider value, HUD state added then undone, active state edits after undo, seated play.");
        }
    }
}
#endif
