using Sandbox;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using System;
using System.Linq;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    public class MyPluginConfigDialog : MyGuiScreenBase
    {
        private int page;
        private static readonly string[] pages={"Comfort & body","Flight & cockpit","View & cameras","HUD & navigation","Graphics"};
        public override string GetFriendlyName() => "SEVR options";
        public MyPluginConfigDialog(int page=0) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.98f,.90f),false,null,MySandboxGame.Config.UIBkOpacity,MySandboxGame.Config.UIOpacity)
        {
            this.page=Math.Max(0,Math.Min(4,page));
            EnabledBackgroundFade=true; m_closeOnEsc=true; m_drawEvenWithoutFocus=true;
            CanHideOthers=true; CanBeHidden=true; CloseButtonEnabled=true;
        }
        internal static MyGuiScreenBase CreatePage(string page)
        {
            switch(page)
            {
                case "Flight":return new MyPluginConfigDialog(1);
                case "Rendering":return new MyPluginConfigDialog(4);
                case "Controls":return new BindingHelp();
                case "Character":return new MyPluginConfigDialog();
                case "HUD & Interface":return new MyPluginConfigDialog(3);
                case "Third person":return new MyPluginConfigDialog(2);
                default:return new SettingsPage(page);
            }
        }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        internal void SelectPage(int index) { page=index; RecreateControls(false); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("VR settings");
            for(int i=0;i<pages.Length;i++)
            {
                int index=i;
                var button=MenuLayout.Button(this,-.325f,-.255f+i*.093f,.24f,pages[i],()=>SelectPage(index),.075f);
                button.Checked=page==i; if(page==i)FocusedControl=button;
            }
            MenuLayout.Button(this,-.325f,.255f,.24f,"Controls guide",()=>MyGuiSandbox.AddScreen(new BindingHelp()));
            MenuLayout.Button(this,-.325f,.33f,.24f,"First-time setup",()=>MyGuiSandbox.AddScreen(new FirstRunSetup()));
            MenuLayout.Label(this,-.14f,-.27f,pages[page],.9f);
            var c=Common.Config;
            switch(page)
            {
                case 0:
                    Link(-.17f,"Height & posture",()=>new BodyOptions());
                    Toggle(-.085f,"Seated play",c.SeatedPlay,BodyFit.SetSeated);
                    Toggle(0,"Controller-relative movement",c.ControllerRelativeMovement,v=>c.ControllerRelativeMovement=v);
                    Link(.085f,"Body visibility",()=>new SettingsPage("Body visibility"));
                    Link(.17f,"Movement & input",()=>new SettingsPage("Character"));
                    MenuLayout.Button(this,.135f,.27f,.55f,"Recenter",()=>Player.Player.Headset.RequestRecenter()).Enabled=Main.VrActive;
                    break;
                case 1:
                    Toggle(-.17f,"Physical cockpit sticks",c.FighterCockpitSticks,v=>c.FighterCockpitSticks=v);
                    Toggle(-.085f,"Tap to hold joysticks",c.TapHoldSticks,v=>c.TapHoldSticks=v);
                    Toggle(0,"Tap to hold levers",c.TapHoldLevers,v=>c.TapHoldLevers=v);
                    Link(.085f,"Controller flight",()=>new FlightOptions());
                    MenuLayout.Button(this,.135f,.185f,.55f,"Sensitivity & deadzones",()=> {
                        foreach(var pause in MyScreenManager.Screens.OfType<SpaceEngineers.Game.GUI.MyGuiScreenMainMenu>().ToArray()) pause.CloseScreenNow();
                        GameActions.Schedule(new ActionChoice("Cockpit tuning",()=>FlightSettings.Open(false)));
                    }).Enabled=SeatFit.Eligible(SeatFit.Seat) && !ThirdPersonView.Active;
                    break;
                case 2:
                    Link(-.17f,"Third-person sensitivity",()=>new SettingsPage("Third person"));
                    Link(-.075f,"Release glide",()=>new SettingsPage("Release glide"));
                    Link(.02f,"Turret aim & camera zoom",()=>new SettingsPage("Turrets and cameras"));
                    break;
                case 3:
                    c.InitializeHudProfiles();
                    for(int i=0;i<c.HudProfiles.Length;i++)
                    {
                        int index=i;
                        var state=c.HudProfiles[i];
                        var button=MenuLayout.Button(this,-.085f+i*.112f,-.155f,.103f,(i+1)+" "+state.Name,()=> {c.SelectHudProfile(index); RecreateControls(false);},.065f);
                        button.Checked=i==c.HudProfileIndex;
                    }
                    Toggle(-.055f,"Vitals",c.ShowVitals,v=>c.ShowVitals=v);
                    MenuLayout.Button(this,.135f,.025f,.55f,"Markers: "+new[] {"Off","No names","Detailed"}[c.WaypointMode],()=> {c.WaypointMode=(c.WaypointMode+1)%3; RecreateControls(false);});
                    Link(.105f,"Selected state",()=>new HudStateOptions());
                    Link(.185f,"GPS, contacts & arrows",()=>new SettingsPage("Signals"));
                    Link(.265f,"HUD visibility & crosshair",()=>new SettingsPage("HUD visibility"));
                    break;
                case 4:
                    MenuLayout.Slider(this,-.17f,"Headset resolution",c.EyeRenderScale*100,50,150,5,"%",v=>c.EyeRenderScale=v/100);
                    MenuLayout.Slider(this,-.055f,"Camera feed resolution",c.RemoteFeedScale*100,50,150,5,"%",v=>c.RemoteFeedScale=v/100);
                    MenuLayout.Slider(this,.06f,"Particle density",c.ParticleDensity*100,15,100,5,"%",v=>c.ParticleDensity=v/100);
                    MenuLayout.Label(this,-.14f,.135f,"100% restores original continuous particles",.60f);
                    Toggle(.19f,"Desktop mirror",c.MirrorDesktop,v=>c.MirrorDesktop=v);
                    MenuLayout.Button(this,.135f,.27f,.55f,"Reset resolution",()=> {c.EyeRenderScale=1;c.RemoteFeedScale=5f/6;RecreateControls(false);});
                    break;
            }
            MenuLayout.Button(this,.315f,.355f,.20f,"Close",()=>CloseScreen());
        }
        private void Link(float y,string title,Func<MyGuiScreenBase> create) => MenuLayout.Button(this,.135f,y,.55f,title,()=>MyGuiSandbox.AddScreen(create()));
        private void Toggle(float y,string title,bool value,Action<bool> save) => MenuLayout.Toggle(this,y,title,value,save);
    }
}
