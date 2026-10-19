using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Plugin;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class HudStateOptions : MyGuiScreenBase
    {
        public HudStateOptions() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.95f,.86f))
        {m_closeOnEsc=true; CloseButtonEnabled=true;}
        public override string GetFriendlyName()=>"SEVR HUD state";
        public override void LoadContent(){base.LoadContent();RecreateControls(true);}
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);
            var c=Common.Config;c.InitializeHudProfiles();int index=c.HudProfileIndex;
            AddCaption("HUD state "+(index+1)+" · "+c.HudProfiles[index].Name);
            MenuLayout.Toggle(this,-.29f,"Notifications",c.ShowNotifications,v=>c.ShowNotifications=v,0);
            MenuLayout.Toggle(this,-.225f,"Chat",c.ShowChat,v=>c.ShowChat=v,0);
            MenuLayout.Toggle(this,-.16f,"Distances",c.ShowSignalDistances,v=>c.ShowSignalDistances=v,0);
            MenuLayout.Toggle(this,-.095f,"Hit markers on foot",c.ShowHitMarkers,v=>c.ShowHitMarkers=v,0);
            MenuLayout.Slider(this,-.02f,"Icon size",c.SignalIconScale*100,75,250,25,"%",v=>c.SignalIconScale=v/100,0);
            MenuLayout.Slider(this,.085f,"Text size",c.SignalTextScale*100,75,150,12.5f,"%",v=>c.SignalTextScale=v/100,0);
            MenuLayout.Button(this,0,.195f,.55f,"Reset state",()=> {c.ResetHudProfile(index);RecreateControls(false);});
            MenuLayout.Button(this,0,.26f,.55f,c.HudProfiles.Length<5 ? "Add state":"Remove fifth state",()=> {
                if(c.HudProfiles.Length<5){c.AddHudProfile();c.SelectHudProfile(4);}else c.RemoveExtraHudProfile();RecreateControls(false);
            });
            MenuLayout.Button(this,0,.335f,.25f,"Back",()=>CloseScreen());
        }
    }
}
