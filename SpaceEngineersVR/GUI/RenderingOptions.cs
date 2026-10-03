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
        public RenderingOptions() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.8f,.9f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("VR Rendering");
            var config=Common.Config;
            Label(-.29f,"Headset resolution (width and height)");
            var target=Label(-.17f,"");
            void Refresh(float scale)
            {
                var recommended=EyeResolution.Recommended;
                var size=recommended.X>0 ? EyeResolution.Size(recommended,scale) : Vector2I.Zero;
                target.Text=size.X>0 ? $"{size.X} × {size.Y} per eye · {scale*scale*100:F0}% pixels" : "100% uses SteamVR's recommended eye resolution";
            }
            Slider(-.23f,100,config.EyeRenderScale,v=> { config.EyeRenderScale=v; Refresh(v); });
            Refresh(config.EyeRenderScale);
            Label(-.09f,"Turret and camera feed resolution");
            var feed=Label(.03f,"");
            void RefreshFeed(float scale) { var size=RemoteFeed.Size(scale); feed.Text=$"{size.X} × {size.Y} · 100% is 1920 × 1080"; }
            Slider(-.03f,83,config.RemoteFeedScale,v=> { config.RemoteFeedScale=v; RefreshFeed(v); });
            RefreshFeed(config.RemoteFeedScale);
            Label(.11f,"Mirror headset on desktop");
            var mirror=new MyGuiControlCheckbox(new Vector2(.28f,.11f)) { IsChecked=config.MirrorDesktop };
            mirror.IsCheckedChanged+=c=>config.MirrorDesktop=c.IsChecked; Controls.Add(mirror);
            Label(.2f,RenderPerformance.Summary,.65f);
            Controls.Add(new MyGuiControlButton(new Vector2(-.16f,.36f),text:new StringBuilder("Reset defaults"),onButtonClick:b=> {
                config.EyeRenderScale=1; config.RemoteFeedScale=5f/6; RecreateControls(false); }));
            Controls.Add(new MyGuiControlButton(new Vector2(.16f,.36f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void Slider(float y,float defaultPercent,float value,System.Action<float> changed)
        {
            var slider=new MyGuiControlSlider(new Vector2(-.025f,y),minValue:50,maxValue:150,width:.55f,defaultValue:defaultPercent,
                labelText:"{0}%",labelDecimalPlaces:0,labelSpaceWidth:.08f,intValue:true,showLabel:true) { Value=value*100 };
            slider.ValueChanged+=s=>changed(s.Value/100);
            Controls.Add(slider);
        }
        private MyGuiControlLabel Label(float y,string text,float size=.75f)
        {
            var label=new MyGuiControlLabel(new Vector2(-.3f,y),text:text,textScale:size,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(label); return label;
        }
    }
}
