using System.Linq;
using Sandbox.Graphics.GUI;
using SpaceEngineers.Game.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class FirstRunSetup : MyGuiScreenBase
    {
        private static bool offered;
        private int step;
        private bool measuring,manual;
        private float height;
        private string calibrationResult;
        private MyGuiControlLabel status;
        private MyGuiControlButton next;
        internal static void Offer()
        {
            if(offered || Common.Config.SetupCompleted || Common.Config.BodyCalibrated || !Main.VrActive || !Player.Player.Headset.pose.isTracked ||
                Sandbox.Game.World.MySession.Static!=null || !(MyScreenManager.Screens.LastOrDefault() is MyGuiScreenMainMenu))return;
            offered=true;MyGuiSandbox.AddScreen(new FirstRunSetup());
        }
        internal FirstRunSetup(int step=0) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.90f,.86f))
        {this.step=System.Math.Min(step,1);height=Common.Config.PlayerHeight*100;m_closeOnEsc=true;CloseButtonEnabled=true;}
        public override string GetFriendlyName()=>"SEVR first-time setup";
        public override void LoadContent(){base.LoadContent();RecreateControls(true);}
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if(status!=null && measuring)status.Text=Player.Player.CalibrationStatus;
            if(measuring && !Player.Player.IsCalibrating){calibrationResult=Player.Player.CalibrationStatus;measuring=false;RecreateControls(false);}
            if(next!=null)next.Enabled=!measuring;
            return result;
        }
        public override bool CloseScreen(bool isUnloading=false)
        {
            if(measuring){Player.Player.CancelCalibration();measuring=false;}
            return base.CloseScreen(isUnloading);
        }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);status=null;AddCaption("VR setup "+(step+1)+" / 2");
            var c=Common.Config;
            if(step==0)
            {
                bool tracked=Main.VrActive && Player.Player.Headset.pose.isTracked;
                MenuLayout.Icon(this,0,-.235f,NativeSprites.Hud("PlayerHelmetOn"),.08f);
                MenuLayout.Label(this,-.31f,-.16f,"Stand upright and look ahead",.85f);
                MenuLayout.Button(this,0,-.075f,.58f,measuring ? "Cancel measurement":"Measure standing height",()=> {
                    if(measuring)Player.Player.CancelCalibration();else Player.Player.StartCalibration(estimateBodyHeight:true);
                    measuring=Player.Player.IsCalibrating;manual=false;RecreateControls(false);
                }).Enabled=tracked;
                status=MenuLayout.Label(this,-.31f,-.015f,measuring ? Player.Player.CalibrationStatus :
                    calibrationResult ?? (c.MeasuredEyeHeight>0 ? "Height calibrated" : tracked ? "" : "Connect a headset to measure."),.62f);
                status.SetMaxWidth(.62f);
                MenuLayout.Button(this,0,.055f,.58f,manual ? "Use measurement":"Enter height manually",()=> {
                    manual=!manual;height=c.PlayerHeight*100;RecreateControls(false);
                }).Enabled=!measuring;
                if(manual)
                {
                    var slider=new MyGuiControlSlider(new Vector2(0,.13f),minValue:100,maxValue:240,width:.56f,
                        defaultValue:180,labelText:"{0} cm",labelDecimalPlaces:1,labelSpaceWidth:.08f,showLabel:true) {Value=height};
                    slider.ValueChanged+=v=>height=v.Value;Controls.Add(slider);
                }
                MenuLayout.Button(this,0,.225f,.58f,c.SeatedPlay ? "Recapture seated position":"Capture seated position (optional)",()=> {
                    if(manual)BodyFit.SetHeight(height/100);
                    BodyFit.SetSeated(true);BodyFit.CaptureSeat();RecreateControls(false);
                }).Enabled=tracked && !measuring;
            }
            else
            {
                MenuLayout.Icon(this,-.16f,-.19f,NativeSprites.Hud("RadialMenu"),.09f);
                MenuLayout.Icon(this,.16f,-.19f,NativeSprites.Hud("HelpScreen"),.09f);
                MenuLayout.Label(this,-.32f,-.075f,"Hold B: toolbar     Hold Y: quick actions",.78f);
                MenuLayout.Label(this,-.32f,.005f,"Wrist > Quick > Help",.78f);
                MenuLayout.Button(this,0,.12f,.55f,"Open controls guide",()=>MyGuiSandbox.AddScreen(new BindingHelp(1)));
            }
            MenuLayout.Button(this,-.23f,.325f,.22f,step==0 ? "Skip":"Back",()=> {
                if(step==0){c.SetupCompleted=true;CloseScreen();}else {if(measuring)Player.Player.CancelCalibration();measuring=false;step--;RecreateControls(false);}
            });
            next=MenuLayout.Button(this,.23f,.325f,.26f,step==1 ? "Finish":"Next",()=> {
                if(step==0 && manual)BodyFit.SetHeight(height/100);
                if(step==1){c.SetupCompleted=true;CloseScreen();return;}
                step++;RecreateControls(false);
            });
            next.Enabled=!measuring;
        }
    }
}
