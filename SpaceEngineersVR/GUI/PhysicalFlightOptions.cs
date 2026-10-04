using System;
using System.Text;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class PhysicalFlightOptions : MyGuiScreenBase
    {
        public override string GetFriendlyName() => "SEVR physical stick options";
        public PhysicalFlightOptions() : base(new Vector2(0.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(0.80f,0.80f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("Physical Cockpit Sticks");
            var config=Common.Config;
            Toggle(-0.25f,"Enable physical cockpit sticks",config.FighterCockpitSticks,v=>config.FighterCockpitSticks=v);
            Toggle(-0.18f,"Twist sticks for yaw and vertical",config.StickTwist,v=>config.StickTwist=v);
            Slider(-0.10f,"Response",config.PhysicalStickSensitivity,0.25f,2f,v=>config.PhysicalStickSensitivity=v);
            Slider(0.04f,"Center deadzone",config.PhysicalStickDeadzone,0.02f,0.35f,v=>config.PhysicalStickDeadzone=v);
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,0.17f),text:"Squeeze near a handle. Each grab captures a new neutral.",textScale:0.65f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,0.21f),text:"Release and center thumbsticks for normal button flight.",textScale:0.65f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(0,0.30f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void Toggle(float y,string label,bool value,Action<bool> save)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,y),text:label,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var box=new MyGuiControlCheckbox(new Vector2(0.29f,y)) { IsChecked=value };
            box.IsCheckedChanged+=c=>save(c.IsChecked); Controls.Add(box);
        }
        private void Slider(float y,string label,float value,float min,float max,Action<float> save)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,y),text:label,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var slider=new MyGuiControlSlider(new Vector2(-0.03f,y+0.05f),minValue:min*100,maxValue:max*100,width:0.53f,
                labelText:"{0}%",labelDecimalPlaces:0,labelSpaceWidth:0.08f,intValue:true,showLabel:true) { Value=value*100 };
            slider.ValueChanged+=s=>save(s.Value/100); Controls.Add(slider);
        }
    }
}
