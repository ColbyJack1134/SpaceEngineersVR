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
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            AddCaption("Space Engineers VR Options");
            var config=Common.Config;
            AddToggle(-0.23f,"Body follows head turns",config.UseHeadRotationForCharacter,v=>config.UseHeadRotationForCharacter=v,
                "Turn the engineer with physical head yaw. Pitch and roll remain head-only.");
            AddToggle(-0.15f,"Roomscale body movement",config.RoomscaleMovement,v=>config.RoomscaleMovement=v,
                "Move the body with horizontal physical steps, stopping at collisions. Crouching remains head movement.");
            AddToggle(-0.07f,"Use left-controller movement direction",config.ControllerRelativeMovement,v=>config.ControllerRelativeMovement=v,
                "Off: movement follows your head. On: movement follows your left controller. Looking up/down does not change walking speed.");
            AddToggle(0.01f,"Controller menu pointer",config.ControllerMenuPointer,v=>config.ControllerMenuPointer=v,
                "Point the right controller at the menu; trigger clicks or drags. Mouse still works when the pointer is off the panel.");
            AddToggle(0.09f,"Keyboard / mouse gameplay",config.EnableKeyboardAndMouseControls,v=>config.EnableKeyboardAndMouseControls=v,
                "Allow ordinary gameplay input. Menu mouse input remains available.");
            AddToggle(0.17f,"Tracked arms (experimental)",config.TrackedArms,v=>config.TrackedArms=v,
                "Track controller grips on foot and in supported seats. Uses vanilla arms during reload, ladder use, or tracking loss.");
            AddToggle(0.25f,"Legacy hand-tilt ship steering",config.LegacyShipTilt,v=>config.LegacyShipTilt=v,
                "Off: stick pitch/yaw, grip + stick horizontal roll. On: held grip lets hand tilt override the stick in ships. Jetpack controls are unchanged.");
            Controls.Add(new MyGuiControlButton(position:new Vector2(-.285f,.35f),size:new Vector2(.18f,.055f),text:new System.Text.StringBuilder("Controls"),onButtonClick:b=>MyGuiSandbox.AddScreen(new BindingHelp())));
            Controls.Add(new MyGuiControlButton(position:new Vector2(-.095f,.35f),size:new Vector2(.18f,.055f),text:new System.Text.StringBuilder("Flight"),onButtonClick:b=>MyGuiSandbox.AddScreen(new FlightOptions())));
            Controls.Add(new MyGuiControlButton(position:new Vector2(.095f,.35f),size:new Vector2(.18f,.055f),text:new System.Text.StringBuilder("Rendering"),onButtonClick:b=>MyGuiSandbox.AddScreen(new RenderingOptions())));
            Controls.Add(new MyGuiControlButton(position:new Vector2(.285f,.35f),size:new Vector2(.18f,.055f),text:new System.Text.StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void AddToggle(float y,string text,bool value,Action<bool> store,string tooltip)
        {
            Controls.Add(new MyGuiControlLabel(position:new Vector2(-0.3f,y),text:text,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
            var checkbox=new MyGuiControlCheckbox(position:new Vector2(0.29f,y),toolTip:tooltip) { IsChecked=value };
            checkbox.IsCheckedChanged+=c=>store(c.IsChecked);
            Controls.Add(checkbox);
        }
    }
}
