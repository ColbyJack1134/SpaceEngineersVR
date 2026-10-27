using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;

namespace SpaceEngineersVR.GUI
{
    // Restores the settings changed since a settings screen opened. Values come from a serialized copy, so only saved settings are restored.
    internal sealed class SettingsUndo
    {
        private static readonly string[] bodySettings={nameof(PluginConfig.PlayerHeight),nameof(PluginConfig.SeatedPlay),nameof(PluginConfig.SeatedReference),nameof(PluginConfig.FitBodyOnFoot),
            nameof(PluginConfig.BodyCalibrated),nameof(PluginConfig.MeasuredEyeHeight)};
        private readonly MyGuiScreenBase screen;
        private readonly PluginConfig config;
        private readonly string snapshot;
        private readonly PluginConfig opened;
        private readonly List<string> changed=new List<string>();
        private MyGuiControlButton button;
        private bool restoring;

        internal SettingsUndo(MyGuiScreenBase screen,PluginConfig config=null)
        {
            this.screen=screen;
            this.config=config ?? Common.Config;
            snapshot=Serialize(this.config);
            opened=Deserialize(snapshot);
            this.config.PropertyChanged+=Changed;
            screen.Closed+=(_,__)=>this.config.PropertyChanged-=Changed;
        }
        internal bool Pending => changed.Any(name => Restorable(name,out var property) && !Equals(property.GetValue(config),property.GetValue(opened)));

        internal void Attach(MyGuiControlButton undo)
        {
            button=undo;
            button.Enabled=Pending;
        }

        private void Changed(object sender,PropertyChangedEventArgs e)
        {
            if(restoring) return;
            if(!changed.Contains(e.PropertyName)) changed.Add(e.PropertyName);
            if(button!=null) button.Enabled=Pending;
        }

        internal void Restore()
        {
            restoring=true;
            try
            {
                // A fresh copy, so restored lists are not shared with the comparison copy. Latest change first.
                var values=Deserialize(snapshot);
                foreach(string name in Enumerable.Reverse(changed).Where(n=>n!=nameof(PluginConfig.HudProfiles)))
                    if(Restorable(name,out var property)) property.SetValue(config,property.GetValue(values));
                // HUD fields copy into the active state when set, and selecting a state writes the index and fields
                // without change events, so the states go back last and the selection reloads from them.
                if(changed.Contains(nameof(PluginConfig.HudProfiles)))
                {
                    config.HudProfiles=values.HudProfiles;
                    config.SelectHudProfile(values.HudProfileIndex);
                }
            }
            finally { restoring=false; }
            bool body=changed.Intersect(bodySettings).Any();
            changed.Clear();
            if(body) BodyFit.ApplyOrigin();
            screen.RecreateControls(false);
        }

        private static bool Restorable(string name,out PropertyInfo property)
        {
            property=typeof(PluginConfig).GetProperty(name,BindingFlags.Public|BindingFlags.Instance);
            return property!=null && property.CanRead && property.CanWrite && property.GetIndexParameters().Length==0 &&
                !property.IsDefined(typeof(XmlIgnoreAttribute),true);
        }

        private static readonly XmlSerializer serializer=new XmlSerializer(typeof(PluginConfig));
        private static string Serialize(PluginConfig config)
        {
            using(var text=new StringWriter()) { serializer.Serialize(text,config); return text.ToString(); }
        }
        private static PluginConfig Deserialize(string xml)
        {
            using(var reader=new StringReader(xml)) return (PluginConfig)serializer.Deserialize(reader);
        }
    }
}
