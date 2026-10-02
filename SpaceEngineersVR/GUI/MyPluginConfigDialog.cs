using Sandbox;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;
using System;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    public class MyPluginConfigDialog : MyGuiScreenBase
    {
        public override string GetFriendlyName() => "SEVR options";
        public MyPluginConfigDialog() : base(new Vector2(0.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(0.80f,0.88f),false,null,MySandboxGame.Config.UIBkOpacity,MySandboxGame.Config.UIOpacity)
        {
            EnabledBackgroundFade=true; m_closeOnEsc=true; m_drawEvenWithoutFocus=true;
            CanHideOthers=true; CanBeHidden=true; CloseButtonEnabled=true;
        }
        internal static MyGuiScreenBase CreatePage(string page) =>
            page=="Flight" ? (MyGuiScreenBase)new FlightOptions() : page=="Rendering" ? new RenderingOptions() :
            page=="Controls" ? (MyGuiScreenBase)new BindingHelp() : new SettingsPage(page);
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            AddCaption("Space Engineers VR Options");
            string[] pages={ "Character","Flight","Third person","HUD & Interface","Rendering","Controls" };
            for(int i=0;i<pages.Length;i++)
            {
                string page=pages[i];
                Controls.Add(new MyGuiControlButton(new Vector2(0,-.24f+i*.092f),size:new Vector2(.50f,.065f),text:new System.Text.StringBuilder(page),onButtonClick:b=> {
                    MyGuiSandbox.AddScreen(CreatePage(page));
                }));
            }
            Controls.Add(new MyGuiControlButton(new Vector2(0,.35f),text:new System.Text.StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
    }
}
