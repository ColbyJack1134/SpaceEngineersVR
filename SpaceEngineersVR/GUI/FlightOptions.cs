using System;
using System.Text;
using Sandbox;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Config;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class FlightOptions : MyGuiScreenBase
    {
        private readonly SettingsUndo undo;
        public override string GetFriendlyName() => "SEVR flight options";
        public FlightOptions() : base(new Vector2(0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.80f,0.82f),
            false, null, MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
        {
            EnabledBackgroundFade=true; m_closeOnEsc=true; m_drawEvenWithoutFocus=true;
            CanHideOthers=true; CanBeHidden=true; CloseButtonEnabled=true;
            undo=new SettingsUndo(this);
        }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            AddCaption("VR Flight Options");
            var config=Common.Config;
            AddSensitivity(-0.23f,"Jetpack roll",config.JetpackRoll,PluginConfig.DefaultJetpackRoll,v=>config.JetpackRoll=v,PluginConfig.JetpackRollPercent,PluginConfig.MinJetpackRoll,PluginConfig.MaxJetpackRoll);
            AddSensitivity(-0.09f,"Ship roll",config.ShipRollSensitivity,PluginConfig.DefaultShipRollSensitivity,v=>config.ShipRollSensitivity=v,100,PluginConfig.MinRollSensitivity,PluginConfig.MaxRollSensitivity);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.3f,.055f),text:"Invert ship thumbstick pitch",originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var invert=new MyGuiControlCheckbox(new Vector2(.29f,.055f)) { IsChecked=config.InvertShipPitch };
            invert.IsCheckedChanged+=v=>config.InvertShipPitch=v.IsChecked; Controls.Add(invert);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.3f,.13f),text:"Physical ship sticks only (first person)",textScale:.75f,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var physicalOnly=new MyGuiControlCheckbox(new Vector2(.29f,.13f)) { IsChecked=config.PhysicalShipControlsOnly };
            physicalOnly.IsCheckedChanged+=v=>config.PhysicalShipControlsOnly=v.IsChecked; Controls.Add(physicalOnly);
            SettingsTooltips.Set("Physical ship sticks only (first person)",physicalOnly);
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.3f,0.21f),text:"Third-person controller flight stays available.",textScale:0.65f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(-0.285f,0.32f),size:new Vector2(0.18f,0.055f),text:new StringBuilder("Reset roll"),onButtonClick:b=> {
                config.JetpackRoll=PluginConfig.DefaultJetpackRoll;
                config.ShipRollSensitivity=PluginConfig.DefaultShipRollSensitivity;
                RecreateControls(false);
            }));
            var restore=new MyGuiControlButton(new Vector2(-0.095f,0.32f),size:new Vector2(0.18f,0.055f),text:new StringBuilder("Undo"),onButtonClick:b=>undo.Restore());
            Controls.Add(restore); undo.Attach(restore);
            Controls.Add(new MyGuiControlButton(new Vector2(0.095f,0.32f),size:new Vector2(0.18f,0.055f),text:new StringBuilder("Physical sticks"),onButtonClick:b=>MyGuiSandbox.AddScreen(new PhysicalFlightOptions())));
            Controls.Add(new MyGuiControlButton(new Vector2(0.285f,0.32f),size:new Vector2(0.18f,0.055f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void AddSensitivity(float y,string title,float value,float defaultValue,Action<float> store,float percent,float min,float max)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.3f,y),text:title,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var slider=new MyGuiControlSlider(new Vector2(-0.035f,y+0.05f),minValue:min*percent,
                maxValue:max*percent,width:0.53f,defaultValue:defaultValue*percent,
                labelText:"{0}%",labelDecimalPlaces:0,labelSpaceWidth:0.08f,intValue:true,showLabel:true) { Value=value*percent };
            SettingsTooltips.Set(title,slider);
            slider.ValueChanged+=s=>store(s.Value/percent);
            Controls.Add(slider);
        }
    }
}
