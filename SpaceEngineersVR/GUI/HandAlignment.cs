using System.Text;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class HandAlignment : MyGuiScreenBase
    {
        private MyGuiControlLabel status,left,right;
        private MyGuiControlButton align;
        private bool aligning;
        private string[] preview;
        public override string GetFriendlyName() => "SEVR Hand alignment";
        public HandAlignment() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.8f,.88f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if(!Main.VrActive) return result;
            if(status!=null) status.Text=GripCalibration.Status;
            if(align!=null) align.Text=GripCalibration.Active ? "Cancel":"Align hands";
            if(aligning && !GripCalibration.Active) Refresh();
            aligning=GripCalibration.Active;
            return result;
        }
        protected override void OnClosed() { if(GripCalibration.Active) GripCalibration.Cancel(); base.OnClosed(); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("Hand alignment");
            Controls.Add(new MyGuiControlLabel(new Vector2(0,-.24f),text:"Use if in-game hands do not match your controllers.",textScale:.65f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
            align=new MyGuiControlButton(new Vector2(0,-.14f),text:new StringBuilder("Align hands"),onButtonClick:b=> {
                if(GripCalibration.Active) GripCalibration.Cancel(); else GripCalibration.Start();
            }) { Enabled=Main.VrActive }; Controls.Add(align);
            status=Centered(-.06f,Main.VrActive ? GripCalibration.Status:"Start VR to align hands.");
            left=Centered(.08f,""); right=Centered(.14f,"");
            Refresh();
            if(preview!=null) { status.Text=preview[0]; left.Text=preview[1]; right.Text=preview[2]; }
            Controls.Add(new MyGuiControlButton(new Vector2(-.18f,.34f),text:new StringBuilder("Reset"),onButtonClick:b=> { GripCalibration.Reset(); Refresh(); }) { Enabled=Main.VrActive });
            Controls.Add(new MyGuiControlButton(new Vector2(.18f,.34f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void Refresh()
        {
            if(!Main.VrActive) return;
            left.Text=Describe("Left",Player.Player.HandL); right.Text=Describe("Right",Player.Player.HandR);
        }
        private static string Describe(string side,Controller hand) => Describe(side,hand.ProfileName,hand.GripSource,Alignment.Get(hand.CalibrationKey));
        internal static string Describe(string side,string profile,string source,Config.AnchorOffsetSetting offset) =>
            side+" ("+profile+", "+source+"): pitch "+offset.Pitch.ToString("0.0")+", yaw "+offset.Yaw.ToString("0.0")+", roll "+offset.Roll.ToString("0.0")+
            ", shift "+(new Vector3(offset.X,offset.Y,offset.Z).Length()*100).ToString("0.0")+" cm";
        internal void Preview(string status,string left,string right) { preview=new[] {status,left,right}; RecreateControls(false); }
        private MyGuiControlLabel Centered(float y,string text)
        {
            var label=new MyGuiControlLabel(new Vector2(0,y),text:text,textScale:.6f,originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER);
            Controls.Add(label); return label;
        }
    }
}
