using Sandbox;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    public class MyPluginConfigDialog : MyGuiScreenBase
    {
        private int page;
        private readonly SettingsUndo undo;
        private static readonly string[] pages={"Comfort & body","Flight & cockpit","View & cameras","HUD & navigation","Graphics"};
        private const int ResultRows=8;
        private List<SettingsSearch.Entry> searchIndex;
        private MyGuiControlSearchBox search;
        private readonly List<MyGuiControlButton> results=new List<MyGuiControlButton>();
        private List<MyGuiControlBase> pageControls,pager;
        private MyGuiControlLabel resultPage;
        private string query="";
        private int resultOffset;
        public override string GetFriendlyName() => "SEVR options";
        public MyPluginConfigDialog(int page=0) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.98f,.90f),false,null,MySandboxGame.Config.UIBkOpacity,MySandboxGame.Config.UIOpacity)
        {
            this.page=Math.Max(0,Math.Min(pages.Length-1,page));
            undo=new SettingsUndo(this);
            EnabledBackgroundFade=true; m_closeOnEsc=true; m_drawEvenWithoutFocus=true;
            CanHideOthers=true; CanBeHidden=true; CloseButtonEnabled=true;
        }
        internal static MyGuiScreenBase CreatePage(string page)
        {
            switch(page)
            {
                case "Flight":return new MyPluginConfigDialog(1);
                case "Rendering":return new MyPluginConfigDialog(4);
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
            // A rebuilt box is a new control; the keyboard would keep typing into the detached one.
            if(search!=null && MenuKeyboard.Targets(search.TextBox)) MenuKeyboard.Close();
            search=new MyGuiControlSearchBox { Position=new Vector2(-.325f,-.265f),Size=new Vector2(.24f,.045f) };
            search.TextBox.Text=query;
            search.OnTextChanged+=text=> { query=text; resultOffset=0; ShowResults(); };
            Controls.Add(search);
            for(int i=0;i<pages.Length;i++)
            {
                int index=i;
                var button=MenuLayout.Button(this,-.325f,-.195f+i*.078f,.24f,pages[i],()=>SelectPage(index),.066f);
                button.Checked=page==i; if(page==i)FocusedControl=button;
            }
            MenuLayout.Button(this,-.325f,.192f,.24f,"Rebind on desktop",OpenBindingEditor,.05f).Enabled=OpenVR.Input!=null;
            MenuLayout.Button(this,-.325f,.26f,.24f,"Controls guide",()=>MyGuiSandbox.AddScreen(new BindingHelp()));
            MenuLayout.Button(this,-.325f,.33f,.24f,"First-time setup",()=>MyGuiSandbox.AddScreen(new FirstRunSetup()));
            MenuLayout.Label(this,-.445f,.395f,"v"+Common.Version.ToString(3),.6f);
            int first=Controls.Count;
            MenuLayout.Label(this,-.14f,-.27f,pages[page],.9f);
            var c=Common.Config;
            switch(page)
            {
                case 0:
                    Link(-.19f,"Height & posture",()=>new BodyOptions());
                    Toggle(-.115f,"Seated play",c.SeatedPlay,BodyFit.SetSeated);
                    Toggle(-.04f,"Controller-relative movement",c.ControllerRelativeMovement,v=>c.ControllerRelativeMovement=v);
                    Link(.035f,"Movement & input",()=>new SettingsPage("Character"));
                    Link(.11f,"Character turning",()=>new SettingsPage("Character turning"));
                    MenuLayout.Slider(this,.165f,"Haptics strength",c.HapticStrength*100,0,100,10,"%",v=>c.HapticStrength=v/100);
                    var recenter=MenuLayout.Button(this,-.0075f,.27f,.265f,"Recenter",()=>Player.Player.Headset.RequestRecenter());
                    recenter.Enabled=Main.VrActive; SettingsTooltips.Set("Recenter",recenter);
                    MenuLayout.Button(this,.2775f,.27f,.265f,"Hand alignment",()=>MyGuiSandbox.AddScreen(new HandAlignment()));
                    break;
                case 1:
                    Toggle(-.21f,"Physical cockpit sticks",c.FighterCockpitSticks,v=>c.FighterCockpitSticks=v);
                    Toggle(-.13f,"Show cockpit seat panel",c.ShowCockpitSeatPanel,v=>c.ShowCockpitSeatPanel=v);
                    Toggle(-.05f,"Tap to hold joysticks",c.TapHoldSticks,v=>c.TapHoldSticks=v);
                    Toggle(.03f,"Tap to hold levers",c.TapHoldLevers,v=>c.TapHoldLevers=v);
                    Link(.11f,"Controller flight",()=>new FlightOptions());
                    MenuLayout.Button(this,.135f,.20f,.55f,"Sensitivity & deadzones",()=> {
                        // Only CloseScreen pops the single-player pause; CloseScreenNow alone leaves the game paused.
                        foreach(var pause in MyScreenManager.Screens.OfType<SpaceEngineers.Game.GUI.MyGuiScreenMainMenu>().ToArray()) { pause.CloseScreen(); pause.CloseScreenNow(); }
                        GameActions.Schedule(new ActionChoice("Cockpit tuning",()=>FlightSettings.Open(false)));
                    }).Enabled=SeatFit.Eligible(SeatFit.Seat) && !ThirdPersonView.Active;
                    break;
                case 2:
                    Link(-.17f,"Third-person sensitivity",()=>new SettingsPage("Third person"));
                    Link(-.075f,"Release glide",()=>new SettingsPage("Release glide"));
                    Link(.02f,"Turrets & cameras",()=>new SettingsPage("Turrets and cameras"));
                    Toggle(.115f,"Keep floating screens where left",c.KeepScreenPlacement,v=>c.KeepScreenPlacement=v);
                    if(Player.WeaponCoreBridge.ApiReady || Player.ShieldsBridge.Ready) Link(.21f,"Weapon & shield warnings",()=>new SettingsPage("Weapon & shield warnings"));
                    break;
                case 3:
                    c.InitializeHudProfiles();
                    for(int i=0;i<c.HudProfiles.Length;i++)
                    {
                        int index=i;
                        var state=c.HudProfiles[i];
                        var button=MenuLayout.Button(this,-.085f+i*.112f,-.155f,.103f,(i+1)+" "+state.Name,()=> {c.SelectHudProfile(index); RecreateControls(false);},.065f);
                        button.Checked=i==c.HudProfileIndex; SettingsTooltips.Set("HUD state",button);
                    }
                    Toggle(-.055f,"Vitals",c.ShowVitals,v=>c.ShowVitals=v);
                    SettingsTooltips.Set("Markers",MenuLayout.Button(this,.135f,.025f,.55f,"Markers: "+new[] {"Off","No names","Detailed"}[c.WaypointMode],()=> {c.WaypointMode=(c.WaypointMode+1)%3; RecreateControls(false);}));
                    Link(.105f,"Selected state",()=>new HudStateOptions());
                    Link(.185f,"GPS, contacts & arrows",()=>new SettingsPage("Signals"));
                    Link(.265f,"HUD visibility & crosshair",()=>new SettingsPage("HUD visibility"));
                    break;
                case 4:
                    MenuLayout.Slider(this,-.20f,"Headset resolution",c.EyeRenderScale*100,50,150,5,"%",v=>c.EyeRenderScale=v/100,enabled:!c.DynamicResolution);
                    MenuLayout.Slider(this,-.10f,"Camera feed resolution",c.RemoteFeedScale*100,50,150,5,"%",v=>c.RemoteFeedScale=v/100);
                    MenuLayout.Slider(this,0f,"Particle density",c.ParticleDensity*100,15,100,5,"%",v=>c.ParticleDensity=v/100);
                    MenuLayout.Label(this,-.14f,.08f,"100% restores original continuous particles",.60f);
                    Toggle(.135f,"Desktop mirror",c.MirrorDesktop,v=>c.MirrorDesktop=v);
                    Toggle(.20f,"Lens mask",c.HiddenAreaMask,v=>c.HiddenAreaMask=v);
                    Link(-.0075f,.265f,.265f,"Performance",()=>new PerformanceOptions());
                    MenuLayout.Button(this,.2775f,.265f,.265f,"Reset resolution",()=> {c.EyeRenderScale=1;c.RemoteFeedScale=5f/6;RecreateControls(false);});
                    break;
            }
            pageControls=Controls.Skip(first).ToList();
            first=Controls.Count;
            resultPage=MenuLayout.Label(this,.105f,.27f,"",.65f);
            MenuLayout.Button(this,-.035f,.27f,.12f,"Previous",()=> { resultOffset=Math.Max(0,resultOffset-ResultRows); ShowResults(); },.045f);
            MenuLayout.Button(this,.305f,.27f,.12f,"Next",()=> { resultOffset+=ResultRows; ShowResults(); },.045f);
            pager=Controls.Skip(first).ToList();
            ShowResults();
            undo.Attach(MenuLayout.Button(this,.095f,.355f,.20f,"Undo",undo.Restore));
            MenuLayout.Button(this,.315f,.355f,.20f,"Close",()=>CloseScreen());
        }
        // Results replace the page content while the box has text; hiding instead of rebuilding keeps the VR keyboard on the box.
        private void ShowResults()
        {
            foreach(var row in results) Controls.Remove(row);
            results.Clear();
            bool searching=!string.IsNullOrWhiteSpace(query);
            foreach(var control in pageControls) control.Visible=!searching;
            foreach(var control in pager) control.Visible=searching;
            if(!searching) return;
            if(searchIndex==null) searchIndex=SettingsSearch.Build(pages);
            var found=SettingsSearch.Find(searchIndex,query).ToArray();
            int count=Math.Max(1,(found.Length+ResultRows-1)/ResultRows);
            resultOffset=Math.Min(resultOffset,(count-1)*ResultRows);
            resultPage.Text=found.Length==0 ? "" : (resultOffset/ResultRows+1)+" / "+count;
            for(int i=0;i<ResultRows && resultOffset+i<found.Length;i++)
            {
                var entry=found[resultOffset+i];
                var row=MenuLayout.Button(this,.135f,-.25f+i*.062f,.55f,entry.Location+" > "+entry.Label,()=> {
                    if(entry.Page>=0) { search.TextBox.Text=""; SelectPage(entry.Page); } else MyGuiSandbox.AddScreen(entry.Create());
                },.055f);
                SettingsTooltips.Set(entry.Label,row); results.Add(row);
            }
        }
        // The Steam Frame dashboard ignores the in-headset binding request; the desktop editor works on every headset.
        private static void OpenBindingEditor()
        {
            var error=OpenVR.Input?.OpenBindingUI(null,0,0,true);
            if(error!=EVRInputError.None)
                MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(messageText:new StringBuilder("Open SteamVR > Controllers > Manage bindings."),messageCaption:new StringBuilder("Controller bindings")));
        }
        private void Link(float y,string title,Func<MyGuiScreenBase> create) => Link(.135f,y,.55f,title,create);
        // UserData lets the settings search follow links into sub-screens.
        private void Link(float x,float y,float width,string title,Func<MyGuiScreenBase> create) => MenuLayout.Button(this,x,y,width,title,()=>MyGuiSandbox.AddScreen(create())).UserData=create;
        private void Toggle(float y,string title,bool value,Action<bool> save) => MenuLayout.Toggle(this,y,title,value,save);
    }
}
