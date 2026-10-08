using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Graphics.GUI;
using Valve.VR;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class BindingOptions : MyGuiScreenBase
    {
        private int page;
        private static readonly string[] actions={"common/Unequip","common/QuickMenu","common/Interact","common/Primary","common/Secondary","common/Recenter",
            "walking/WalkLongitudinal","walking/WalkRotate","walking/JumpOrClimbUp","walking/CrouchOrClimbDown","common/Jetpack",
            "flying/SeatTerminal","flying/ThrustLRFB","flying/ThrustRotate","flying/ThrustUp","flying/ThrustDown"};
        private static readonly string[] names={"Toolbar / cancel","Quick actions","Use","Primary / click","Secondary / right-click","Recenter",
            "Move","Turn","Jump","Crouch","Jetpack / typing","Dampeners","Flight translation","Flight rotation","Rise","Descend"};
        internal BindingOptions() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.96f,.86f))
        {m_closeOnEsc=true;CloseButtonEnabled=true;}
        public override string GetFriendlyName()=>"SEVR active bindings";
        internal static void OpenEditor()
        {
            var error=OpenVR.Input?.OpenBindingUI(null,0,0,false);
            if(error!=EVRInputError.None)
                MyGuiSandbox.AddScreen(MyGuiSandbox.CreateMessageBox(messageText:new StringBuilder("Open SteamVR > Controllers > Manage bindings."),messageCaption:new StringBuilder("Controller bindings")));
        }
        internal static string Origin(string action)
        {
            int slash=action.IndexOf('/');string setName="/actions/"+action.Substring(0,slash);
            ulong set=0,handle=0;var origins=new ulong[16];var names=new List<string>();
            if(OpenVR.Input!=null && OpenVR.Input.GetActionSetHandle(setName,ref set)==EVRInputError.None &&
                OpenVR.Input.GetActionHandle(setName+"/in/"+action.Substring(slash+1),ref handle)==EVRInputError.None &&
                OpenVR.Input.GetActionOrigins(set,handle,origins)==EVRInputError.None)
                foreach(ulong origin in origins)
                {
                    if(origin==0)continue;var name=new StringBuilder(256);
                    if(OpenVR.Input.GetOriginLocalizedName(origin,name,256,-1)==EVRInputError.None)names.Add(name.ToString());
                }
            return names.Count==0 ? "No binding reported":string.Join(", ",names);
        }
        public override void LoadContent(){base.LoadContent();RecreateControls(true);}
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor);AddCaption("Active bindings "+(page+1)+" / 4");
            for(int i=0;i<4 && page*4+i<actions.Length;i++)
            {
                int index=page*4+i;float y=-.245f+i*.115f;
                MenuLayout.Label(this,-.40f,y,names[index],.75f);
                string origin=Origin(actions[index]);
                var label=MenuLayout.Label(this,-.40f,y+.04f,origin,.58f);
                label.SetMaxWidth(.80f);
            }
            MenuLayout.Button(this,-.29f,.29f,.20f,"Previous",()=> {page=(page+3)%4;RecreateControls(false);});
            MenuLayout.Button(this,0,.29f,.26f,"Rebind in SteamVR",OpenEditor).Enabled=OpenVR.Input!=null;
            MenuLayout.Button(this,.29f,.29f,.20f,"Next",()=> {page=(page+1)%4;RecreateControls(false);});
            MenuLayout.Button(this,0,.36f,.20f,"Back",()=>CloseScreen());
        }
    }
}
