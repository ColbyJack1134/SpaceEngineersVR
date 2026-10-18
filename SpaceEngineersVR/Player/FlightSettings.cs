using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using SpaceEngineersVR.Multiplayer;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.Player
{
    internal static class FlightSettings
    {
        private static MyCockpit owner;
        private static readonly FlightTuning defaults=new FlightTuning();
        private static FlightTuning draft;
        private static readonly MenuWindow window=new MenuWindow {Width=.55f,Aspect=.31f/.55f,MinimumWidth=.42f,MaximumWidth=1.2f,BarOffset=.035f};
        private static readonly WindowInteraction interaction=new WindowInteraction(window);
        private static DateTime lastWindow;
        private static int windowHover;
        internal static bool WindowCaptured => IsOpen && !OnWrist && window.Drag!=0;
        private static bool grip,stickHold,leverHold;
        private static int vehiclePage;
        internal static bool OnWrist { get; private set; }
        internal static bool IsOpen => owner!=null;
        internal static string Title => (owner?.CustomName?.ToString() ?? "Cockpit")+
            (CockpitRig.Find(owner?.BlockDefinition.Id.SubtypeName)?.Wheel!=null ? " Controls":" Flight Settings");
        internal static FlightTuning For(MyCockpit seat) => seat!=null && MultiplayerRuntime.Get(seat,out var state) && state.Flight!=null ? state.Flight : defaults;
        internal static void Open(bool wrist)
        {
            if(!SeatFit.Eligible(SeatFit.Seat)) return;
            owner=SeatFit.Seat; OnWrist=wrist; draft=For(owner).Copy(); grip=false; vehiclePage=0;
            stickHold=Common.Config.TapHoldSticks; leverHold=Common.Config.TapHoldLevers;
            window.Place(Player.Headset.pose.deviceToAbsolute.matrix,.55f,new Vector3(0,-.08f,-.60f));
            interaction.Reset(); windowHover=0; lastWindow=DateTime.UtcNow;
            CockpitControls.Release(); CockpitTouch.Reset(); Controls.Static.BlockUntilRelease();
            if(wrist) SpatialUi.Expand();
        }
        internal static void Close()
        {
            owner=null; draft=null; window.Stop(); interaction.Reset(); windowHover=0; lastWindow=DateTime.UtcNow;
            Controls.Static.BlockUntilRelease();
        }
        internal static void Check()
        {
            if(IsOpen && (owner!=SeatFit.Seat || !SeatFit.Eligible(owner) || Main.MenuOpen || ThirdPersonView.Active ||
                !Player.Headset.pose.isTracked || !Player.HandL.pose.isTracked || !Player.HandR.pose.isTracked)) Close();
        }
        internal static bool UpdateWindow(MatrixD pointer)
        {
            if(!IsOpen || OnWrist || SpatialUi.ContentCaptured && !interaction.Active) return false;
            var aim=(Matrix)(pointer*MatrixD.Invert(SpatialUi.DeviceWorld(Matrix.Identity)));
            var now=DateTime.UtcNow; float seconds=(float)(now-lastWindow).TotalSeconds; lastWindow=now;
            bool owns=interaction.Update(Player.HandR,aim,seconds);
            windowHover=interaction.Hover;
            return owns;
        }
        internal static SurfaceView Floating()
        {
            if(!IsOpen || OnWrist) return null;
            var view=View("FlightSettings",SpatialUi.DeviceWorld(window.Pose),window.Width,window.Height,Keys(),Title);
            view.WindowPose=window.Pose; view.WindowHover=window.Drag!=0 ? window.Drag:windowHover;
            return view;
        }
        internal static SurfaceView View(string id,MatrixD transform,float width,float height,SurfaceKey[] keys,string title) =>
            new SurfaceView {Id=id,Pose=transform,Width=width,Height=height,Style=SurfaceStyle.WristMenu,FlightPage=true,Title=title,Keys=keys};
        private static void Apply()
        {
            if(draft.Encode()!=For(owner).Encode())
            {
                if(!MultiplayerRuntime.Get(owner,out _)) { EssentialHud.Notify("Cockpit settings are not ready."); return; }
                MultiplayerRuntime.SaveFlight(owner,draft);
            }
            Common.Config.TapHoldSticks=stickHold; Common.Config.TapHoldLevers=leverHold;
            Close();
        }
        internal static SurfaceKey[] Keys() => Layout(draft,grip,stickHold,leverHold,Title,true,owner?.BlockDefinition.Id.SubtypeName,vehiclePage);
        internal static SurfaceKey[] Layout(FlightTuning value,bool personal,bool sticks,bool levers,string title,bool live=false,string subtype=null,int page=0)
        {
            var keys=new List<SurfaceKey>();
            bool vehicle=CockpitRig.Find(subtype)?.Wheel!=null;
            bool saddle=CockpitRig.Find(subtype)?.Wheel?.ThrottleActor>=0;
            Action<string,float,float,float,float,Action,bool> button=(label,x,y,w,h,action,active)=>keys.Add(new SurfaceKey(label,x,y,w,h) {Action=live ? new ActionChoice(label,action):null,Active=active});
            var tabs=vehicle ? new[] {"Steering","Flight",saddle ? "Bars":"Motion","Triggers"}:new[] {"Tuning","Triggers"};
            float tabStep=.96f/(tabs.Length+1),tabWidth=tabStep-.015f;
            for(int i=0;i<tabs.Length;i++)
            {
                int tab=vehicle || i==0 ? i:TriggerPage;
                button(tabs[i],.025f+i*tabStep,.12f,tabWidth,.075f,()=>{grip=false; vehiclePage=tab;},!personal && page==tab);
            }
            button("Grip",.025f+tabs.Length*tabStep,.12f,tabWidth,.075f,()=>{grip=true;},personal);
            if(personal)
            {
                button(vehicle ? "Tap to hold steering":"Tap to hold joysticks",.035f,.29f,.93f,.12f,()=>stickHold=!stickHold,sticks);
                button("Tap to hold analog levers",.035f,.46f,.93f,.12f,()=>leverHold=!leverHold,levers);
                keys[keys.Count-2].Value=sticks ? "On":"Off"; keys[keys.Count-1].Value=levers ? "On":"Off";
            }
            else if(page==TriggerPage)
            {
                keys.Add(new SurfaceKey("RIGHT TRIGGER",.035f,.215f,.93f,.05f) {Caption=true,Enabled=false});
                button("Forward",.035f,.28f,.3f,.08f,()=>value.RightTrigger=RightTrigger.Forward,value.RightTrigger==RightTrigger.Forward);
                button("Off",.35f,.28f,.3f,.08f,()=>value.RightTrigger=RightTrigger.Off,value.RightTrigger==RightTrigger.Off);
                keys.Add(new SurfaceKey("LEFT TRIGGER",.035f,.42f,.93f,.05f) {Caption=true,Enabled=false});
                button("Brake / up",.035f,.485f,.3f,.08f,()=>value.LeftTrigger=LeftTrigger.Brake,value.LeftTrigger==LeftTrigger.Brake);
                button("Reverse",.35f,.485f,.3f,.08f,()=>value.LeftTrigger=LeftTrigger.Reverse,value.LeftTrigger==LeftTrigger.Reverse);
                button("Off",.665f,.485f,.3f,.08f,()=>value.LeftTrigger=LeftTrigger.Off,value.LeftTrigger==LeftTrigger.Off);
            }
            else if(vehicle && page==0)
            {
                AddSlider(keys,"Steering sensitivity",.035f,.25f,value.Translation,.25f,2,100,"%",v=>value.Translation=v,live);
                AddSlider(keys,"Steering deadzone",.525f,.25f,value.TiltDeadzone,0,.35f,100,"%",v=>value.TiltDeadzone=v,live);
                button("Squared",.035f,.415f,.215f,.08f,()=>value.TranslationCurve=2,value.TranslationCurve==2);
                button("Linear",.265f,.415f,.215f,.08f,()=>value.TranslationCurve=1,value.TranslationCurve==1);
                AddSlider(keys,"Input smoothing",.525f,.405f,value.Smoothing,0,.1f,1000," ms",v=>value.Smoothing=v,live);
                if(saddle)
                {
                    AddSlider(keys,"Throttle sensitivity",.035f,.58f,value.ThrottleSensitivity,.25f,2,100,"%",v=>value.ThrottleSensitivity=v,live);
                    AddSlider(keys,"Throttle deadzone",.525f,.58f,value.TwistDeadzone,0,.35f,100,"%",v=>value.TwistDeadzone=v,live);
                }
            }
            else if(vehicle && page==2 && saddle)
            {
                AddSlider(keys,"Pitch travel",.035f,.225f,value.BarPitchTravel,.02f,.05f,100," cm",v=>{value.BarPitchTravel=v; value.BarPitchDeadzone=Math.Min(value.BarPitchDeadzone,v*.5f);},live);
                AddSlider(keys,"Pitch deadzone",.525f,.225f,value.BarPitchDeadzone,0,.015f,1000," mm",v=>value.BarPitchDeadzone=v,live);
                AddSlider(keys,"Roll travel",.035f,.385f,value.BarRollTravel,MathHelper.ToRadians(5),MathHelper.ToRadians(10),180f/MathHelper.Pi,"°",v=>{value.BarRollTravel=v; value.BarRollDeadzone=Math.Min(value.BarRollDeadzone,v*.5f);},live);
                AddSlider(keys,"Roll deadzone",.525f,.385f,value.BarRollDeadzone,0,MathHelper.ToRadians(4),180f/MathHelper.Pi,"°",v=>value.BarRollDeadzone=v,live);
            }
            else if(vehicle && page==2)
            {
                AddSlider(keys,"Pitch travel",.035f,.225f,value.WheelPitchTravel,.02f,.06f,100," cm",v=>{value.WheelPitchTravel=v; value.WheelPitchDeadzone=Math.Min(value.WheelPitchDeadzone,v*.5f);},live);
                AddSlider(keys,"Pitch deadzone",.525f,.225f,value.WheelPitchDeadzone,0,.015f,1000," mm",v=>value.WheelPitchDeadzone=v,live);
                AddSlider(keys,"Roll travel",.035f,.385f,value.WheelRollTravel,MathHelper.ToRadians(15),MathHelper.ToRadians(60),180f/MathHelper.Pi,"°",v=>value.WheelRollTravel=v,live);
                AddSlider(keys,"Roll deadzone",.525f,.385f,value.TiltDeadzone,0,.35f,100,"%",v=>value.TiltDeadzone=v,live);
            }
            else if(vehicle)
            {
                int mode=VehicleMode(value,saddle);
                var names=new[] {"Driving","Flight","Flight + tilt"};
                for(int i=0;i<3;i++)
                {
                    int choice=i;
                    button(names[i],.035f+i*.315f,.225f,.3f,.075f,()=>SetVehicleMode(value,saddle,choice),mode==i);
                }
                AddSlider(keys,"Yaw sensitivity",.035f,.335f,value.Rotation,.25f,2,100,"%",v=>value.Rotation=v,live);
                AddSlider(keys,"Pitch sensitivity",.525f,.335f,value.PitchSensitivity,.25f,2,100,"%",v=>value.PitchSensitivity=v,live);
                AddSlider(keys,"Roll sensitivity",.035f,.505f,value.RollSensitivity,.25f,2,100,"%",v=>value.RollSensitivity=v,live);
                button("Squared",.525f,.535f,.215f,.08f,()=>value.RotationCurve=2,value.RotationCurve==2);
                button("Linear",.755f,.535f,.215f,.08f,()=>value.RotationCurve=1,value.RotationCurve==1);
            }
            else
            {
                keys.Add(new SurfaceKey("ROTATION",.035f,.215f,.44f,.05f) {Caption=true,Enabled=false});
                keys.Add(new SurfaceKey("TRANSLATION",.525f,.215f,.44f,.05f) {Caption=true,Enabled=false});
                AddSlider(keys,"Sensitivity",.035f,.28f,value.Rotation,.25f,2,100,"%",v=>value.Rotation=v,live);
                AddSlider(keys,"Sensitivity",.525f,.28f,value.Translation,.25f,2,100,"%",v=>value.Translation=v,live);
                button("Squared",.035f,.425f,.215f,.08f,()=>value.RotationCurve=2,value.RotationCurve==2);
                button("Linear",.265f,.425f,.215f,.08f,()=>value.RotationCurve=1,value.RotationCurve==1);
                button("Squared",.525f,.425f,.215f,.08f,()=>value.TranslationCurve=2,value.TranslationCurve==2);
                button("Linear",.755f,.425f,.215f,.08f,()=>value.TranslationCurve=1,value.TranslationCurve==1);
                AddSlider(keys,"Tilt deadzone",.035f,.535f,value.TiltDeadzone,0,.35f,100,"%",v=>value.TiltDeadzone=v,live);
                AddSlider(keys,"Twist deadzone",.525f,.535f,value.TwistDeadzone,0,.35f,100,"%",v=>value.TwistDeadzone=v,live);
                AddSlider(keys,"Input smoothing",.035f,.69f,value.Smoothing,0,.1f,1000," ms",v=>value.Smoothing=v,live);
            }
            button("Reset defaults",.035f,.875f,.25f,.085f,()=>{if(grip) stickHold=leverHold=false; else draft=new FlightTuning();},false);
            button("Cancel",.65f,.875f,.15f,.085f,Close,false);
            button("Apply",.815f,.875f,.15f,.085f,Apply,true);
            return keys.ToArray();
        }
        internal const int TriggerPage=3;
        // One choice over FlightMode and the wheel or bar tilt switch, which only applies in flight.
        internal static int VehicleMode(FlightTuning value,bool saddle) => !value.FlightMode ? 0:(saddle ? value.BarTiltEnabled:value.WheelMotion) ? 2:1;
        internal static void SetVehicleMode(FlightTuning value,bool saddle,int mode)
        {
            value.FlightMode=mode>0;
            if(saddle) value.BarTiltEnabled=mode==2; else value.WheelMotion=mode==2;
        }
        private static void AddSlider(List<SurfaceKey> keys,string label,float x,float y,float value,float min,float max,float scale,string unit,Action<float> change,bool live)
        {
            var key=new SurfaceKey(label,x,y,.44f,.12f) {Slider=(value-min)/(max-min),Value=Math.Round(value*scale)+unit};
            key.Change=live ? (Action<float>)(v=>{float next=(float)Math.Round((min+v*(max-min))*scale)/scale; change(next); key.Slider=(next-min)/(max-min); key.Value=Math.Round(next*scale)+unit;}) : null;
            keys.Add(key);
        }
    }
}
