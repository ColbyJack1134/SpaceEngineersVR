using System.Text;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class RenderingOptions : MyGuiScreenBase
    {
        public override string GetFriendlyName() => "SEVR rendering options";
        public RenderingOptions() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.8f,.78f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("VR Rendering");
            var config=Common.Config;
            Label(-.17f,"Headset resolution (width and height)");
            var target=Label(-.03f,"");
            void Refresh(float scale)
            {
                var recommended=EyeResolution.Recommended;
                var size=recommended.X>0 ? EyeResolution.Size(recommended,scale) : Vector2I.Zero;
                target.Text=size.X>0 ? $"{size.X} × {size.Y} per eye · {scale*scale*100:F0}% pixels" : "100% uses SteamVR's recommended eye resolution";
            }
            var slider=new MyGuiControlSlider(new Vector2(-.025f,-.11f),minValue:50,maxValue:150,width:.55f,defaultValue:100,
                labelText:"{0}%",labelDecimalPlaces:0,labelSpaceWidth:.08f,intValue:true,showLabel:true) { Value=config.EyeRenderScale*100 };
            slider.ValueChanged+=s=> { config.EyeRenderScale=s.Value/100; Refresh(config.EyeRenderScale); };
            Controls.Add(slider); Refresh(config.EyeRenderScale);
            Label(.03f,"Mirror headset on desktop");
            var mirror=new MyGuiControlCheckbox(new Vector2(.28f,.03f)) { IsChecked=config.MirrorDesktop };
            mirror.IsCheckedChanged+=c=>config.MirrorDesktop=c.IsChecked; Controls.Add(mirror);
            Label(.15f,RenderPerformance.Summary,.65f);
            Controls.Add(new MyGuiControlButton(new Vector2(-.16f,.30f),text:new StringBuilder("Reset to 100%"),onButtonClick:b=> { config.EyeRenderScale=1; RecreateControls(false); }));
            Controls.Add(new MyGuiControlButton(new Vector2(.16f,.30f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private MyGuiControlLabel Label(float y,string text,float size=.75f)
        {
            var label=new MyGuiControlLabel(new Vector2(-.3f,y),text:text,textScale:size,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(label); return label;
        }
    }
}
