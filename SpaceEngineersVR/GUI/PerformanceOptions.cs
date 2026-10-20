using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class PerformanceOptions : MyGuiScreenBase
    {
        private static readonly string[] Costly={"Game settings that cost the most in VR:","•  Grass density and draw distance","•  Shadow quality","•  Tree view distance"};
        private MyGuiControlLabel detail,live;
        public override string GetFriendlyName() => "SEVR performance";
        public PerformanceOptions() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.8f,.88f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; CanHideOthers=true; CanBeHidden=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if(detail!=null) detail.Text=Detail();
            if(live!=null) live.Text=Status();
            return result;
        }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("Performance");
            detail=Add(this,-.26f);
            live=MenuLayout.Label(this,-.275f,.07f,Status(),.62f);
            MenuLayout.Toggle(this,.135f,"Performance panel",PerformanceHud.Enabled,v=>PerformanceHud.Enabled=v,0);
            Tips(this,.2f,.027f);
            MenuLayout.Button(this,0,.36f,.26f,"Done",()=>CloseScreen());
        }

        internal static MyGuiControlLabel Add(MyGuiScreenBase screen,float top)
        {
            var c=Common.Config;
            MenuLayout.Toggle(screen,top,"Dynamic resolution",c.DynamicResolution,v=>c.DynamicResolution=v,0);
            MenuLayout.Slider(screen,top+.075f,"Target frame rate",c.TargetFrameRate,30,144,5," FPS",v=>c.TargetFrameRate=v,0,c.DynamicResolution);
            MenuLayout.Slider(screen,top+.18f,"Headset resolution",c.EyeRenderScale*100,50,150,5,"%",v=>c.EyeRenderScale=v/100,0,!c.DynamicResolution);
            return MenuLayout.Label(screen,-.275f,top+.275f,Detail(),.6f);
        }
        internal static void Tips(MyGuiScreenBase screen,float top,float spacing)
        {
            for(int i=0;i<Costly.Length;i++) MenuLayout.Label(screen,-.275f,top+i*spacing,Costly[i],.58f);
            if(RenderPerformance.DesktopVSync) MenuLayout.Label(screen,-.275f,top+Costly.Length*spacing,"Game VSync is on: VR runs no faster than the monitor's refresh rate.",.58f);
        }
        internal static string Detail()
        {
            var c=Common.Config;
            var recommended=EyeResolution.Recommended;
            if(recommended.X<=0) return "";
            var top=EyeResolution.Size(recommended,EyeResolution.BaseScale);
            if(!c.DynamicResolution) return $"{top.X}x{top.Y} per eye";
            var low=EyeResolution.Size(recommended,DynamicResolution.Floor);
            return $"{top.X}x{top.Y} per eye, down to {low.X}x{low.Y}";
        }
        private static string Status()
        {
            var last=RenderPerformance.Last;
            return last==null ? "Frame rate shows here after playing in VR." : $"Last in game: {last.Fps:F0} FPS, limited by {last.Limit}";
        }
    }
}
