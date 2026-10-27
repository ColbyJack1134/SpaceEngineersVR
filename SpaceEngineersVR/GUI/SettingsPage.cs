using System;
using System.Text;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class SettingsPage : MyGuiScreenBase
    {
        private readonly string page;
        private readonly SettingsUndo undo;
        public override string GetFriendlyName() => "SEVR "+page;
        public SettingsPage(string page) : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.8f,.88f))
        { this.page=page; m_closeOnEsc=true; CloseButtonEnabled=true; undo=new SettingsUndo(this); }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption(page);
            var c=Common.Config;
            switch(page)
            {
                case "Character":
                    Toggle(-.31f,"Left-handed",c.LeftHanded,v=>c.LeftHanded=v);
                    Toggle(-.245f,"Body follows head turns",c.UseHeadRotationForCharacter,v=>c.UseHeadRotationForCharacter=v);
                    Toggle(-.18f,"Roomscale body movement",c.RoomscaleMovement,v=>c.RoomscaleMovement=v);
                    Toggle(-.115f,"Controller-relative movement",c.ControllerRelativeMovement,v=>c.ControllerRelativeMovement=v);
                    Toggle(-.05f,"Invert jetpack pitch",c.InvertJetpackPitch,v=>c.InvertJetpackPitch=v);
                    Toggle(.015f,"Hold grip to rotate blocks",c.HoldGripRotation,v=>c.HoldGripRotation=v);
                    Toggle(.08f,"Lock block position while rotating in build mode",c.LockBlockWhileRotating,v=>c.LockBlockWhileRotating=v);
                    Link(.15f,"Body and seated play",()=>new BodyOptions());
                    Link(.21f,"Body visibility",()=>new SettingsPage("Body visibility"));
                    Link(.27f,"Advanced controls",()=>new SettingsPage("Advanced controls"));
                    break;
                case "Character turning":
                    Toggle(-.23f,"Snap turning",c.SnapTurn,v=>c.SnapTurn=v);
                    Slider(-.12f,"Smooth turn speed",c.TurnSensitivity,.25f,v=>c.TurnSensitivity=v,400);
                    Label(.04f,"Snap angle");
                    var angle=new MyGuiControlSlider(new Vector2(-.035f,.09f),minValue:15,maxValue:90,width:.53f,defaultValue:30,
                        labelText:"{0}°",labelDecimalPlaces:0,labelSpaceWidth:.08f,intValue:true,showLabel:true) { Value=c.SnapTurnAngle };
                    angle.ValueChanged+=v=>c.SnapTurnAngle=v.Value;
                    Controls.Add(angle);
                    Reset(()=> { c.SnapTurn=false;c.TurnSensitivity=1.5f;c.SnapTurnAngle=30; });
                    break;
                case "Body visibility":
                    Toggle(-.20f,"Proximity fade on foot",c.OnFootBodyProximityFade,v=>c.OnFootBodyProximityFade=v);
                    Toggle(-.08f,"Proximity fade in seats",c.BodyProximityFade,v=>c.BodyProximityFade=v);
                    Toggle(.04f,"Hide first-person body",c.HideFirstPersonBody,v=>c.HideFirstPersonBody=v);
                    Label(.19f,"Hands and wrist controls stay visible.",.65f);
                    break;
                case "Advanced controls":
                    Toggle(-.32f,"Controller menu pointer",c.ControllerMenuPointer,v=>c.ControllerMenuPointer=v);
                    Toggle(-.24f,"Keyboard / mouse gameplay",c.EnableKeyboardAndMouseControls,v=>c.EnableKeyboardAndMouseControls=v);
                    Toggle(-.16f,"Tracked arms",c.TrackedArms,v=>c.TrackedArms=v);
                    Toggle(-.08f,"Legacy hand-tilt ship steering",c.LegacyShipTilt,v=>c.LegacyShipTilt=v);
                    Toggle(0,"Double-tap to keep tools running",c.DoubleTapToolLock,v=>c.DoubleTapToolLock=v);
                    Toggle(.08f,"Clear terrain toggle",c.ClearTerrainToggle,v=>c.ClearTerrainToggle=v);
                    Toggle(.16f,"Right stick up jumps",c.StickJump,v=>c.StickJump=v);
                    Toggle(.24f,"VR data to [SEVR] programmable blocks",c.ProgrammableBlockData,v=>c.ProgrammableBlockData=v);
                    break;
                case "Turrets and cameras":
                    Slider(-.24f,"Turret aim sensitivity",c.TurretAimSensitivity,.25f,v=>c.TurretAimSensitivity=v,400);
                    Slider(-.10f,"Camera zoom sensitivity",c.CameraZoomSensitivity,.25f,v=>c.CameraZoomSensitivity=v,400);
                    Label(.20f,"Camera zoom: right thumbstick while holding right cockpit stick.",.55f);
                    Reset(()=> { c.TurretAimSensitivity=c.CameraZoomSensitivity=2; });
                    break;
                case "Third person":
                    Slider(-.24f,"Pan sensitivity",c.ThirdPersonPanSensitivity,.25f,v=>c.ThirdPersonPanSensitivity=v);
                    Slider(-.10f,"Zoom sensitivity",c.ThirdPersonZoomSensitivity,.25f,v=>c.ThirdPersonZoomSensitivity=v);
                    Slider(.04f,"Rotation sensitivity",c.ThirdPersonRotationSensitivity,.25f,v=>c.ThirdPersonRotationSensitivity=v);
                    Link(.20f,"Release glide",()=>new SettingsPage("Release glide"));
                    Reset(()=> { c.ThirdPersonPanSensitivity=c.ThirdPersonZoomSensitivity=c.ThirdPersonRotationSensitivity=1; });
                    break;
                case "Release glide":
                    Slider(-.24f,"Pan glide",c.ThirdPersonPanGlide,0,v=>c.ThirdPersonPanGlide=v);
                    Slider(-.10f,"Zoom glide",c.ThirdPersonZoomGlide,0,v=>c.ThirdPersonZoomGlide=v);
                    Slider(.04f,"Rotation glide",c.ThirdPersonRotationGlide,0,v=>c.ThirdPersonRotationGlide=v);
                    Label(.21f,"0% stops on release. 100% uses the tuned default.",.65f);
                    Reset(()=> { c.ThirdPersonPanGlide=c.ThirdPersonZoomGlide=c.ThirdPersonRotationGlide=1; });
                    break;
                case "Signal ranges":
                    Range(-.23f,"Own contacts",c.OwnSignalRange,v=>c.OwnSignalRange=v);
                    Range(-.09f,"Friendly / unowned",c.FriendlySignalRange,v=>c.FriendlySignalRange=v);
                    Range(.05f,"Neutral / hostile",c.OtherSignalRange,v=>c.OtherSignalRange=v);
                    Label(.22f,"Antenna limits. GPS and ore use native visibility.",.6f);
                    Reset(()=> { c.OwnSignalRange=c.FriendlySignalRange=c.OtherSignalRange=1; });
                    break;
                case "Signals":
                    Toggle(-.25f,"Character marker roll",c.CharacterMarkerRoll,v=>c.CharacterMarkerRoll=v);
                    Toggle(-.17f,"Face markers toward viewer",c.FaceMarkersTowardViewer,v=>c.FaceMarkersTowardViewer=v);
                    Toggle(-.09f,"Direction arrows",c.SignalEdges,v=>c.SignalEdges=v);
                    Toggle(-.01f,"Targeting rings",c.SignalRings,v=>c.SignalRings=v);
                    Toggle(.07f,"GPS and objectives",c.ShowGps,v=>c.ShowGps=v);
                    Toggle(.15f,"Contacts",c.ShowContacts,v=>c.ShowContacts=v);
                    Toggle(.23f,"Ore and hacking",c.ShowResources,v=>c.ShowResources=v);
                    Controls.Add(new MyGuiControlButton(new Vector2(-.18f,.34f),text:new StringBuilder("Ranges"),onButtonClick:b=>MyGuiSandbox.AddScreen(new SettingsPage("Signal ranges"))));
                    break;
                case "Weapon & shield warnings":
                    Toggle(-.23f,"Warning pulse",c.WarningPulse,v=>c.WarningPulse=v);
                    var repeat=Label(-.12f,"Repeat pulse every");
                    var interval=new MyGuiControlSlider(new Vector2(-.035f,-.07f),minValue:0,maxValue:Config.PluginConfig.MaxWarningPulseInterval,width:.53f,
                        defaultValue:Config.PluginConfig.DefaultWarningPulseInterval,labelText:"{0} s",labelDecimalPlaces:1,labelSpaceWidth:.08f,showLabel:true) { Value=c.WarningPulseInterval };
                    interval.ValueChanged+=v=>c.WarningPulseInterval=v.Value;
                    Controls.Add(interval); SettingsTooltips.Set("Repeat pulse every",repeat,interval);
                    Label(.06f,"0 s: pulse only when a warning starts.",.6f);
                    Reset(()=> { c.WarningPulse=true; c.WarningPulseInterval=Config.PluginConfig.DefaultWarningPulseInterval; });
                    break;
                case "HUD visibility":
                    Toggle(-.26f,"Show HUD with visor open",c.HudWithVisorOpen,v=>c.HudWithVisorOpen=v);
                    Toggle(-.185f,"Ship crosshair",c.ShipCrosshair,v=>c.ShipCrosshair=v);
                    Toggle(-.11f,"Block info without grip",c.InspectWithoutGrip,v=>c.InspectWithoutGrip=v);
                    Slider(-.035f,"Crosshair size",c.CrosshairScale,.2f,v=>c.CrosshairScale=v,100,Config.PluginConfig.DefaultCrosshairScale);
                    Toggle(.10f,"Reduce screen effects",c.ReduceScreenEffects,v=>c.ReduceScreenEffects=v);
                    Action<bool> hideDust=null;
                    Toggle(.175f,"Visor dust",c.VisorDust,v=> { c.VisorDust=v; hideDust(v); });
                    hideDust=Toggle(.25f,"Hide dust with visor open",c.HideDustWithVisorOpen,v=>c.HideDustWithVisorOpen=v,.03f);
                    hideDust(c.VisorDust);
                    break;
            }
            var restore=new MyGuiControlButton(new Vector2(0,.34f),text:new StringBuilder("Undo"),onButtonClick:b=>undo.Restore());
            Controls.Add(restore); undo.Attach(restore);
            Controls.Add(new MyGuiControlButton(new Vector2(.18f,.34f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private MyGuiControlLabel Label(float y,string text,float scale=.75f,float indent=0)
        {
            var label=new MyGuiControlLabel(new Vector2(-.3f+indent,y),text:text,textScale:scale,originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER);
            Controls.Add(label); return label;
        }
        private Action<bool> Toggle(float y,string text,bool value,Action<bool> store,float indent=0)
        {
            var label=Label(y,text,indent:indent);
            var check=new MyGuiControlCheckbox(new Vector2(.29f,y)) { IsChecked=value };
            check.IsCheckedChanged+=v=>store(v.IsChecked); Controls.Add(check);
            SettingsTooltips.Set(text,label,check);
            return enabled=>label.Enabled=check.Enabled=enabled;
        }
        private void Range(float y,string text,float value,Action<float> store)
        {
            var label=Label(y,text,.7f);
            Action<float> caption=v=>label.Text=text+": "+Player.WorldMarkers.Distance(Sandbox.Game.GUI.HudViewers.MyHudMarkerRender.Denormalize(v));
            caption(value);
            var slider=new MyGuiControlSlider(new Vector2(-.035f,y+.05f),minValue:0,maxValue:1,width:.53f,defaultValue:1) { Value=value };
            slider.ValueChanged+=v=> { store(v.Value); caption(v.Value); }; Controls.Add(slider);
            SettingsTooltips.Set(text,label,slider);
        }
        private void Slider(float y,string text,float value,float min,Action<float> store,float max=200,float fallback=1)
        {
            var label=Label(y,text);
            var slider=new MyGuiControlSlider(new Vector2(-.035f,y+.05f),minValue:min*100,maxValue:max,width:.53f,defaultValue:fallback*100,labelText:"{0}%",labelDecimalPlaces:0,labelSpaceWidth:.08f,intValue:true,showLabel:true) { Value=value*100 };
            slider.ValueChanged+=v=>store(v.Value/100); Controls.Add(slider);
            SettingsTooltips.Set(text,label,slider);
        }
        private void Link(float y,string text,Func<MyGuiScreenBase> screen) => Controls.Add(new MyGuiControlButton(new Vector2(0,y),text:new StringBuilder(text),onButtonClick:b=>MyGuiSandbox.AddScreen(screen())));
        private void Reset(Action reset) => Controls.Add(new MyGuiControlButton(new Vector2(-.18f,.34f),text:new StringBuilder("Reset group"),onButtonClick:b=> { reset(); RecreateControls(false); }));
    }
}
