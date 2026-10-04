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
            Toggle(-0.19f,"Twist sticks for yaw and vertical",config.StickTwist,v=>config.StickTwist=v);
            Slider(-0.13f,"Sensitivity",config.PhysicalStickSensitivity,.25f,2f,v=>config.PhysicalStickSensitivity=v,100,"{0}%",0);
            Slider(-0.035f,"Response curve",config.PhysicalStickExponent,1f,3f,v=>config.PhysicalStickExponent=v,1,"{0}",1);
            Slider(0.06f,"Center deadzone",config.PhysicalStickDeadzone,.02f,.35f,v=>config.PhysicalStickDeadzone=v,100,"{0}%",0);
            Slider(0.155f,"Smoothing",config.PhysicalStickSmoothing,0,.06f,v=>config.PhysicalStickSmoothing=v,1000,"{0} ms",0);
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,0.255f),text:"Each grab sets neutral. Lower sensitivity needs more tilt.",textScale:0.65f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(0,0.30f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void Toggle(float y,string label,bool value,Action<bool> save)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,y),text:label,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var box=new MyGuiControlCheckbox(new Vector2(0.29f,y)) { IsChecked=value };
            box.IsCheckedChanged+=c=>save(c.IsChecked); Controls.Add(box);
        }
        private void Slider(float y,string label,float value,float min,float max,Action<float> save,float scale,string format,int decimals)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,y),text:label,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var slider=new MyGuiControlSlider(new Vector2(-0.03f,y+0.035f),minValue:min*scale,maxValue:max*scale,width:0.53f,
                labelText:format,labelDecimalPlaces:decimals,labelSpaceWidth:0.08f,intValue:decimals==0,showLabel:true) { Value=value*scale };
            slider.ValueChanged+=s=>save(s.Value/scale); Controls.Add(slider);
        }
    }
}
