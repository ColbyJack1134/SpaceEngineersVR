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
        public override string GetFriendlyName() => "SEVR flight options";
        public FlightOptions() : base(new Vector2(0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR, new Vector2(0.80f,0.82f),
            false, null, MySandboxGame.Config.UIBkOpacity, MySandboxGame.Config.UIOpacity)
        {
            EnabledBackgroundFade=true; m_closeOnEsc=true; m_drawEvenWithoutFocus=true;
            CanHideOthers=true; CanBeHidden=true; CloseButtonEnabled=true;
        }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            AddCaption("VR Flight Options");
            var config=Common.Config;
            AddSensitivity(-0.23f,"Jetpack roll",config.JetpackRollSensitivity,PluginConfig.DefaultJetpackRollSensitivity,v=>config.JetpackRollSensitivity=v);
            AddSensitivity(-0.09f,"Ship roll",config.ShipRollSensitivity,PluginConfig.DefaultShipRollSensitivity,v=>config.ShipRollSensitivity=v);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.3f,.055f),text:"Invert ship thumbstick pitch",originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var invert=new MyGuiControlCheckbox(new Vector2(.29f,.055f)) { IsChecked=config.InvertShipPitch };
            invert.IsCheckedChanged+=v=>config.InvertShipPitch=v.IsChecked; Controls.Add(invert);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.3f,.13f),text:"Physical ship sticks only (first person)",textScale:.75f,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var physicalOnly=new MyGuiControlCheckbox(new Vector2(.29f,.13f)) { IsChecked=config.PhysicalShipControlsOnly };
            physicalOnly.IsCheckedChanged+=v=>config.PhysicalShipControlsOnly=v.IsChecked; Controls.Add(physicalOnly);
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.3f,0.21f),text:"Third-person controller flight stays available.",textScale:0.65f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(-0.26f,0.32f),size:new Vector2(0.22f,0.055f),text:new StringBuilder("Reset roll"),onButtonClick:b=> {
                config.JetpackRollSensitivity=PluginConfig.DefaultJetpackRollSensitivity;
                config.ShipRollSensitivity=PluginConfig.DefaultShipRollSensitivity;
                RecreateControls(false);
            }));
            Controls.Add(new MyGuiControlButton(new Vector2(0,0.32f),size:new Vector2(0.23f,0.055f),text:new StringBuilder("Physical sticks"),onButtonClick:b=>MyGuiSandbox.AddScreen(new PhysicalFlightOptions())));
            Controls.Add(new MyGuiControlButton(new Vector2(0.26f,0.32f),size:new Vector2(0.22f,0.055f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void AddSensitivity(float y,string title,float value,float defaultValue,Action<float> store)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.3f,y),text:title,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var slider=new MyGuiControlSlider(new Vector2(-0.035f,y+0.05f),minValue:PluginConfig.MinRollSensitivity*100,
                maxValue:PluginConfig.MaxRollSensitivity*100,width:0.53f,defaultValue:defaultValue*100,
                labelText:"{0}%",labelDecimalPlaces:0,labelSpaceWidth:0.08f,intValue:true,showLabel:true,
                toolTip:"Hold the right trigger and drag to adjust roll sensitivity.") { Value=value*100 };
            slider.ValueChanged+=s=>store(s.Value/100);
            Controls.Add(slider);
        }
    }
}
