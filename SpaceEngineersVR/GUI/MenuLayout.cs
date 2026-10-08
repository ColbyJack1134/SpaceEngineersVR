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
            Button(screen,center,y,.55f,label+"    "+(value ? "On":"Off"),()=> {save(!value); screen.RecreateControls(false);});
        }
        internal static void Slider(MyGuiScreenBase screen,float y,string label,float value,float min,float max,float step,string unit,Action<float> save,float center=.135f)
        {
            Label(screen,center-.275f,y,label);
            var caption=Label(screen,center+.145f,y,value.ToString("0.#")+unit,.68f);
            var slider=new MyGuiControlSlider(new Vector2(center,y+.044f),minValue:min,maxValue:max,width:.40f,defaultValue:value) {Value=value};
            slider.ValueChanged+=s=> {save(s.Value); caption.Text=s.Value.ToString("0.#")+unit;}; screen.Controls.Add(slider);
            Button(screen,center-.25f,y+.044f,.06f,"−",()=>slider.Value=Math.Max(min,slider.Value-step),.042f);
            Button(screen,center+.25f,y+.044f,.06f,"+",()=>slider.Value=Math.Min(max,slider.Value+step),.042f);
        }
    }
}
