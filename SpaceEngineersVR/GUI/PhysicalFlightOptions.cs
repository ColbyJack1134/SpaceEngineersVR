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
            base.RecreateControls(constructor); AddCaption("Physical Cockpit Controls");
            var config=Common.Config;
            Toggle(-0.25f,"Enable physical cockpit sticks",config.FighterCockpitSticks,v=>config.FighterCockpitSticks=v);
            Toggle(-0.19f,"Twist sticks for yaw and vertical",config.StickTwist,v=>config.StickTwist=v);
            Toggle(-0.10f,"Tap to hold joysticks",config.TapHoldSticks,v=>config.TapHoldSticks=v);
            Toggle(-0.04f,"Tap to hold analog levers",config.TapHoldLevers,v=>config.TapHoldLevers=v);
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,0.10f),text:"Flight tuning: settings on the seat panel or wrist.",textScale:0.7f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(0,0.30f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void Toggle(float y,string label,bool value,Action<bool> save)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-0.30f,y),text:label,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var box=new MyGuiControlCheckbox(new Vector2(0.29f,y)) { IsChecked=value };
            box.IsCheckedChanged+=c=>save(c.IsChecked); Controls.Add(box);
        }
    }
}
