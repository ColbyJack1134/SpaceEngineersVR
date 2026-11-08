using System;
using System.Text;
using Sandbox.Graphics.GUI;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal static class MenuLayout
    {
        internal static MyGuiControlLabel Label(MyGuiScreenBase screen,float x,float y,string text,float scale=.72f)
        {
            var label=new MyGuiControlLabel(new Vector2(x,y),text:text,textScale:scale,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            screen.Controls.Add(label); return label;
        }
        internal static MyGuiControlButton Button(MyGuiScreenBase screen,float x,float y,float width,string text,Action action,float height=.055f)
        {
            var native=MyGuiControlButton.GetVisualStyle(VRage.Game.MyGuiControlButtonStyleEnum.Default);
            // Default button textures fix their bounds; a center slice permits native art at the requested size.
            var button=new MyGuiControlButton(new Vector2(x,y),text:new StringBuilder(text),textScale:.8f,onButtonClick:_=>action()) {
                CustomStyle=new MyGuiControlButton.StyleDefinition {
                    NormalTexture=new MyGuiCompositeTexture {Center=native.NormalTexture.LeftTop},
                    HighlightTexture=new MyGuiCompositeTexture {Center=native.HighlightTexture.LeftTop},
                    FocusTexture=new MyGuiCompositeTexture {Center=native.FocusTexture.LeftTop},
                    ActiveTexture=new MyGuiCompositeTexture {Center=native.ActiveTexture.LeftTop},
                    NormalFont=native.NormalFont,HighlightFont=native.HighlightFont,Padding=native.Padding,
                    TextColorFocus=native.TextColorFocus,SizeOverride=new Vector2(width,height)
                }
            };
            screen.Controls.Add(button); return button;
        }
        internal static void Icon(MyGuiScreenBase screen,float x,float y,string path,float size=.055f)
        {
            screen.Controls.Add(new MyGuiControlImage(new Vector2(x,y),new Vector2(size*.75f,size),textures:new[] {path}));
        }
        internal static void Toggle(MyGuiScreenBase screen,float y,string label,bool value,Action<bool> save,float center=.135f)
        {
            SettingsTooltips.Set(label,Button(screen,center,y,.55f,label+"    "+(value ? "On":"Off"),()=> {save(!value); screen.RecreateControls(false);}));
        }
        // Ctrl+click or gamepad A enters a value, as on terminal sliders.
        internal static MyGuiControlSlider Typed(MyGuiControlSlider slider,string caption)
        {
            slider.SliderSetValueManual=s=> {
                string format=s.IntValue ? "0":"0.##";
                var dialog=new MyGuiScreenDialogAmount(s.MinValue,s.MaxValue,MyStringId.GetOrCompute(caption),s.IntValue ? 0:2,s.IntValue,s.Value,
                    Sandbox.MySandboxGame.Config.UIBkOpacity,Sandbox.MySandboxGame.Config.UIOpacity,writer:(text,v)=>text.Clear().Append(v.ToString(format)));
                dialog.OnConfirmed+=v=>s.Value=v;
                MyGuiSandbox.AddScreen(dialog);
                return true;
            };
            return slider;
        }
        internal static void Slider(MyGuiScreenBase screen,float y,string label,float value,float min,float max,float step,string unit,Action<float> save,float center=.135f,bool enabled=true)
        {
            var title=Label(screen,center-.275f,y,label); title.Enabled=enabled;
            var slider=Typed(new MyGuiControlSlider(new Vector2(center,y+.044f),minValue:min,maxValue:max,width:.40f,defaultValue:value,
                intValue:step%1==0 && min%1==0) {Value=value,Enabled=enabled},label);
            var caption=Label(screen,center+.145f,y,slider.Value.ToString("0.#")+unit,.68f); caption.Enabled=enabled;
            slider.ValueChanged+=s=> {save(s.Value); caption.Text=s.Value.ToString("0.#")+unit;}; screen.Controls.Add(slider);
            SettingsTooltips.Set(label,title,slider);
            Button(screen,center-.25f,y+.044f,.06f,"−",()=>slider.Value=Math.Max(min,slider.Value-step),.042f).Enabled=enabled;
            Button(screen,center+.25f,y+.044f,.06f,"+",()=>slider.Value=Math.Min(max,slider.Value+step),.042f).Enabled=enabled;
        }
    }
}
