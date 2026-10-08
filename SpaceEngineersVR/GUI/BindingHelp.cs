using System;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class BindingHelp : MyGuiScreenBase
    {
        private int page;
        private static readonly string[] topics={"Basics","Menus","Building","Flight","Cockpit","Tools","HUD","Windows","View","Driving"};
        private sealed class Card
        {
            internal readonly string Title,Icon;
            internal readonly string[] Steps;
            internal Card(string title,string icon,params string[] steps){Title=title;Icon=icon;Steps=steps;}
        }
        private static readonly Card[][] cards={
            new[] {new Card("Move","MoveCloser","Left stick: move","Right stick: turn","Left grip: sprint"),new Card("Use","OpenInventory","Point at the object","A: use / enter / exit"),
                new Card("Jump & crouch","PlayerHelmetOn","Left-stick click: jump / climb up","Right stick down / up: crouch / stand"),new Card("Recenter","CameraSpectator","Click the right stick","Standing / seated height and view")},
            new[] {new Card("Toolbar","RadialMenu","Hold B","LEFT stick: select","Release B: use"),new Card("Quick actions","RadialMenu","Hold Y","RIGHT stick: select","Release Y: use"),
                new Card("Pages & cancel","ValueIncrease","Triggers: previous / next page","Center stick: cancel","B: cancel quick actions"),new Card("Block actions","ToggleConnectedGrid","Point at a block with the right hand","Hold Y","Release Y: use selected action")},
            new[] {new Card("Place & remove","MultiBlockBuilding","Right trigger: place","Right grip + trigger: remove"),new Card("Rotate","BlockRotate","Tap right grip","Right stick: yaw / pitch","Left stick: roll / distance"),
                new Card("Size & variants","GridSize","Right grip + X: size","Hold Y: variants","Triggers: change page"),new Card("Blueprints","BlueprintsScreen","Quick > Blueprints","Right trigger: paste preview","Tap B: cancel preview")},
            new[] {new Card("Controller flight","Jetpack","Left stick: translate","Right stick: pitch / yaw","Right grip + stick sideways: roll"),new Card("Dampeners","Dampeners","Click left stick: dampeners","Double click: auto dampeners","Hold on foot: jetpack"),
                new Card("Physical sticks","BlockRotate","Right tilt: pitch / roll","Right twist or thumbstick: yaw","Left tilt / twist: thrust / lift"),new Card("Camera & turret","CameraSpectator","Hold right cockpit stick","Right thumbstick up / down: zoom","A: exit the active view")},
            new[] {new Card("Buttons & switches","GridPowerOn","Bring fingertip near the control","Squeeze trigger to activate","Release before the next press"),new Card("Levers & sticks","BlockRotate","Grip or trigger: grab","Move while held","Release to let go"),
                new Card("Assign a control","RadialMenu","Hover the control","Tap B","Choose its action in the G menu"),new Card("Seat & tuning","AdminMenu","Quick > Seat panel","Hold arrows to adjust the seat","Cog: joystick settings")},
            new[] {new Card("Primary & secondary","DrillIcon","Right trigger: primary","Right grip + trigger: secondary"),new Card("Two hands","ReloadGame","Left grip near the support handle","Pistol: support the firing hand","Release grip to let go"),
                new Card("Reload","ReloadGame","Release the support grip","Left grip below the magazine","Or Quick > Reload"),new Card("Select equipment","RadialMenu","Hold B to open the toolbar","LEFT stick: choose a tool","Release B to equip")},
            new[] {new Card("Light & view","Light","Left temple + trigger: light","Left temple + grip: ship third person"),new Card("Visor & HUD","PlayerHelmetOn","Right temple + grip: visor","Right temple + trigger: cycle HUD","Near helmet: reveal marker names"),
                new Card("Navigation","SignalMode","Open the Navigation tab","Edge arrows: off-view waypoints","GPS / Contacts: show or hide"),new Card("Inspect a block","MultiBlockBuilding","Point with the right hand","Hold free right grip","Read components and integrity")},
            new[] {new Card("Menus & LCDs","ToggleConnectedGrid","Point and pull trigger: click","Grip: secondary click","Right stick: menu scroll"),new Card("Typing","Chat","X: open keyboard","Y: bring keyboard forward","B / Done: close keyboard"),
                new Card("Move & resize","MoveCloser","Grab the bar below the window","Held stick sideways: resize","Corner handle: resize"),new Card("Menu modifiers","OpenInventory","Left grip: Shift","Left trigger: Ctrl","Combine with right-trigger click")},
            new[] {new Card("Enter & leave","CameraSpectator","Left temple + grip: third person","Tap B: return","Right-stick click: fit ship"),new Card("Move the view","MoveCloser","Hold both grips: pan / zoom / rotate","Release one: pan with one hand"),
                new Card("Glide & stop","BlockRotate","Release while moving: glide","Grab again: stop glide","Flight input brakes the view"),new Card("Resume flight","Jetpack","Release both grips","Center flight controls","Y quick actions: camera modes")},
            new[] {new Card("Wheels","GridPowerOn","Empty hand + right trigger: drive","Left stick: steer / reverse","Rise control: brake"),new Card("Rise & descend","Jetpack","Left trigger: rise","Left grip: descend","X: jump in a rover"),
                new Card("Modeled wheels","Handbrake","Use controller driving inputs","Quick > Seat panel: adjust seat"),new Card("Remote view","CameraSpectator","Y: camera and remote actions","A: exit the active feed","Quick > Camera: cycle ship view")}
        };
        public BindingHelp(int topic=0) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(1.03f,.91f))
        {page=Math.Max(0,Math.Min(topics.Length-1,topic));m_closeOnEsc=true;CloseButtonEnabled=true;}
        public override string GetFriendlyName()=>"SEVR controls guide";
        internal void SelectTopic(int topic){page=topic;RecreateControls(false);}
        public override void LoadContent(){base.LoadContent();RecreateControls(true);}
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);AddCaption("Controls guide");
            for(int i=0;i<topics.Length;i++)
            {
                int index=i;
                var button=MenuLayout.Button(this,-.4f+i%5*.2f,-.30f+i/5*.067f,.19f,topics[i],()=>SelectTopic(index),.05f);
                button.Checked=i==page; if(i==page)FocusedControl=button;
            }
            for(int i=0;i<4;i++)
            {
                var card=cards[page][i];float x=i%2==0 ? -.25f:.25f,y=i<2 ? -.075f:.155f;
                Controls.Add(new MyGuiControlPanel(new Vector2(x,y),new Vector2(.475f,.205f),new Vector4(.10f,.16f,.20f,1),MyGuiConstants.BLANK_TEXTURE));
                MenuLayout.Icon(this,x-.184f,y-.06f,NativeSprites.Hud(card.Icon),.062f);
                MenuLayout.Label(this,x-.135f,y-.063f,card.Title,.85f);
                for(int step=0;step<card.Steps.Length;step++)
                    MenuLayout.Label(this,x-.208f,y-.005f+step*.042f,card.Steps[step],.62f);
            }
            MenuLayout.Label(this,-.46f,.292f,"Quest / Touch defaults",.60f);
            MenuLayout.Button(this,-.30f,.355f,.27f,"Active bindings",()=>MyGuiSandbox.AddScreen(new BindingOptions()));
            MenuLayout.Button(this,0,.355f,.27f,"Rebind in SteamVR",BindingOptions.OpenEditor).Enabled=OpenVR.Input!=null;
            MenuLayout.Button(this,.31f,.355f,.25f,"Close",()=>CloseScreen());
        }
    }
}
