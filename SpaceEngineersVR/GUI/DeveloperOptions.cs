using System;
using System.Text;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class DeveloperOptions : MyGuiScreenBase
    {
        public override string GetFriendlyName() => "SEVR developer options";
        public DeveloperOptions() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.8f,.74f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; CanHideOthers=true; CanBeHidden=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("Developer options");
            Toggle(-.18f,"Show alignment markers and diagnostic logs",Common.Config.DeveloperTools,v=>Common.Config.DeveloperTools=v);
            Toggle(-.10f,"Adaptive arm reach",Common.Config.AdaptiveArms,v=>Common.Config.AdaptiveArms=v);
            Toggle(-.02f,"Stable stereo shadow cascades",Common.Config.StableShadows,v=>Common.Config.StableShadows=v);
            Toggle(.06f,"Sun / distant flares",Common.Config.DistantFlares,v=>Common.Config.DistantFlares=v);
            Toggle(.14f,"Mirror an eye to the desktop",Common.Config.MirrorDesktop,v=>Common.Config.MirrorDesktop=v);
            Toggle(.22f,"Skip pixels hidden by the headset lenses",Common.Config.HiddenAreaMask,v=>Common.Config.HiddenAreaMask=v);
            Controls.Add(new MyGuiControlLabel(new Vector2(-.33f,.28f),text:"Trace: 120 frames to GameData / SEVR-render-trace.csv (CPU timings).",textScale:.62f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(-.2f,.35f),text:new StringBuilder("Capture trace"),onButtonClick:b=>Player.StereoRenderState.RequestTrace()));
            Controls.Add(new MyGuiControlButton(new Vector2(.2f,.33f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void Toggle(float y,string label,bool value,Action<bool> store)
        {
            Controls.Add(new MyGuiControlLabel(new Vector2(-.33f,y),text:label,textScale:.75f,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var checkbox=new MyGuiControlCheckbox(new Vector2(.31f,y)) { IsChecked=value };
            checkbox.IsCheckedChanged+=c=>store(c.IsChecked); Controls.Add(checkbox);
        }
    }
}
