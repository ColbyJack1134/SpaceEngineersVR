using System.Text;
using Sandbox.Graphics.GUI;
using SpaceEngineersVR.Player;
using SpaceEngineersVR.Plugin;
using VRage.Utils;
using VRageMath;

namespace SpaceEngineersVR.GUI
{
    internal sealed class BodyOptions : MyGuiScreenBase
    {
        private MyGuiControlLabel measurement;
        private MyGuiControlSlider height;
        private MyGuiControlButton measure;
        private bool measuring;
        public override bool Update(bool hasFocus)
        {
            bool result=base.Update(hasFocus);
            if(!Main.VrActive) return result;
            if(measurement!=null) measurement.Text=Player.Player.CalibrationStatus;
            if(measure!=null) measure.Text=Player.Player.IsCalibrating ? "Cancel measurement":"Measure standing height";
            if(measuring && !Player.Player.IsCalibrating) RecreateControls(false);
            measuring=Player.Player.IsCalibrating;
            return result;
        }
        public override string GetFriendlyName() => "SEVR Body and seated play";
        public BodyOptions() : base(new Vector2(.5f),MyGuiConstants.SCREEN_BACKGROUND_COLOR,new Vector2(.8f,.88f))
        { m_closeOnEsc=true; CloseButtonEnabled=true; }
        public override void LoadContent() { base.LoadContent(); RecreateControls(true); }
        public override void RecreateControls(bool constructor)
        {
            base.RecreateControls(constructor); AddCaption("Body and seated play");
            var config=Common.Config;
            Label(-.25f,"Standing height (cm)");
            height=new MyGuiControlSlider(new Vector2(-.035f,-.20f),minValue:100,maxValue:240,width:.53f,defaultValue:180,
                labelText:"{0} cm",labelDecimalPlaces:0,labelSpaceWidth:.09f,intValue:true,showLabel:true) { Value=config.PlayerHeight*100 };
            height.ValueChanged+=s=>BodyFit.SetHeight(s.Value/100); Controls.Add(height);
            measure=new MyGuiControlButton(new Vector2(0,-.115f),text:new StringBuilder("Measure standing height"),onButtonClick:b=> {
                if(Player.Player.IsCalibrating) Player.Player.CancelCalibration(); else Player.Player.StartCalibration();
            }) { Enabled=Main.VrActive }; Controls.Add(measure);
            measurement=new MyGuiControlLabel(new Vector2(0,-.055f),text:Main.VrActive ? Player.Player.CalibrationStatus : "Stand upright to measure, or enter your height.",textScale:.55f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER); Controls.Add(measurement);
            Label(.005f,"Seated play (toggle for standing)");
            var seated=new MyGuiControlCheckbox(new Vector2(.29f,.005f)) { IsChecked=config.SeatedPlay };
            seated.IsCheckedChanged+=v=>BodyFit.SetSeated(v.IsChecked); Controls.Add(seated);
            Controls.Add(new MyGuiControlButton(new Vector2(0,.10f),text:new StringBuilder("Set seated head position"),onButtonClick:b=>BodyFit.CaptureSeat()));
            Label(.195f,"Fit body on foot");
            var fit=new MyGuiControlCheckbox(new Vector2(.29f,.195f)) { IsChecked=config.FitBodyOnFoot };
            fit.IsCheckedChanged+=v=> { config.FitBodyOnFoot=v.IsChecked; config.BodyCalibrated=true; if(Main.VrActive) Player.Player.ApplyCalibrationOrigin(); };
            Controls.Add(fit);
            Controls.Add(new MyGuiControlLabel(new Vector2(0,.265f),text:"Recenter keeps calibration. Cockpits keep native body size.",textScale:.59f,
                originAlign:MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER));
            Controls.Add(new MyGuiControlButton(new Vector2(-.18f,.34f),text:new StringBuilder("Reset calibration"),onButtonClick:b=> {
                BodyFit.ResetCalibration(); RecreateControls(false);
            }));
            Controls.Add(new MyGuiControlButton(new Vector2(.18f,.34f),text:new StringBuilder("Done"),onButtonClick:b=>CloseScreen()));
        }
        private void Label(float y,string text) => Controls.Add(new MyGuiControlLabel(new Vector2(-.3f,y),text:text,textScale:.75f,
            originAlign:MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER));
    }
}
